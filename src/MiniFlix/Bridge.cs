using System.Text.Json;
using System.Text.Json.Nodes;
using MiniFlix.Services;

namespace MiniFlix;

/// <summary>
/// Ponte JS ↔ C#. Mensagens da UI: { tipo, idRequisicao, dados }.
/// Respostas: { tipo, idRequisicao, dados | erro }. Eventos espontâneos: { tipo, evento: true, dados }.
/// </summary>
public sealed class Bridge
{
    private readonly Dictionary<string, Func<JsonObject?, Task<object?>>> _comandos = new(StringComparer.Ordinal);
    private readonly Action<string> _enviar;

    public Bridge(Action<string> enviar) => _enviar = enviar;

    public void Registrar(string tipo, Func<JsonObject?, Task<object?>> handler) => _comandos[tipo] = handler;
    public void Registrar(string tipo, Func<JsonObject?, object?> handler) => _comandos[tipo] = d => Task.FromResult(handler(d));

    public async Task ProcessarAsync(string json)
    {
        string tipo = "?";
        string? id = null;
        try
        {
            var msg = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Mensagem inválida.");
            tipo = msg["tipo"]?.GetValue<string>() ?? throw new InvalidOperationException("Campo 'tipo' ausente.");
            id = msg["idRequisicao"]?.GetValue<string>();
            if (!_comandos.TryGetValue(tipo, out var handler))
                throw new InvalidOperationException($"Comando desconhecido: {tipo}");

            var resultado = await handler(msg["dados"] as JsonObject);
            Enviar(new { tipo, idRequisicao = id, dados = resultado });
        }
        catch (Exception ex)
        {
            Log.Erro($"Comando '{tipo}' falhou", ex);
            Enviar(new { tipo, idRequisicao = id, erro = ex.Message });
        }
    }

    public void Emitir(string tipo, object? dados = null) => Enviar(new { tipo, evento = true, dados });

    private void Enviar(object mensagem) => _enviar(JsonSerializer.Serialize(mensagem, JsonStore.Opcoes));

    // Helpers para ler parâmetros com mensagens de erro claras.
    public static string Texto(JsonObject? d, string campo) =>
        d?[campo]?.GetValue<string>() ?? throw new ArgumentException($"Parâmetro '{campo}' ausente.");
}
