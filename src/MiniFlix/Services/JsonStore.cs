using System.Text.Encodings.Web;
using System.Text.Json;

namespace MiniFlix.Services;

/// <summary>Leitura e gravação de JSON. A gravação é atômica (tmp + replace) para resistir a quedas e à remoção do pendrive.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // mantém acentos legíveis
    };

    public static T? Ler<T>(string caminho) where T : class
    {
        if (!File.Exists(caminho)) return null;
        using var fs = File.OpenRead(caminho);
        return JsonSerializer.Deserialize<T>(fs, Opcoes);
    }

    /// <summary>Como <see cref="Ler{T}"/>, mas retorna null (e registra em log) se o arquivo estiver corrompido.</summary>
    public static T? LerSeguro<T>(string caminho) where T : class
    {
        try { return Ler<T>(caminho); }
        catch (Exception ex)
        {
            Log.Aviso($"JSON inválido ignorado: {caminho} ({ex.Message})");
            return null;
        }
    }

    public static void Gravar<T>(string caminho, T valor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var tmp = caminho + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(fs, valor, Opcoes);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, caminho, overwrite: true);
    }
}
