using System.Text.Json.Serialization;

namespace MiniFlix.Services;

public static class EstadoFila
{
    public const string Aguardando = "aguardando";
    public const string Processando = "processando";
    public const string Concluido = "concluido";
    public const string Erro = "erro";
    public const string Cancelado = "cancelado";
}

public sealed class FilaItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Tipo { get; init; } = "";         // "local" | "youtube"
    public string Titulo { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Origem { get; init; } = "";       // nome do arquivo ou URL (para exibição)
    public string Estado { get; set; } = EstadoFila.Aguardando;
    public string Etapa { get; set; } = "Na fila";
    public string? Detalhe { get; set; }            // ex.: "3,2 MB/s · 0:42 restantes"
    public double Progresso { get; set; }           // 0..1 do item inteiro
    public string? Mensagem { get; set; }
    public string? Slug { get; set; }               // preenchido ao concluir
    public DateTime CriadoEm { get; init; } = DateTime.Now;

    [JsonIgnore] internal CancellationTokenSource Cts { get; } = new();
    [JsonIgnore] internal Func<FilaItem, Relator, CancellationToken, Task>? Executor { get; init; }

    public bool Finalizado => Estado is EstadoFila.Concluido or EstadoFila.Erro or EstadoFila.Cancelado;
}

/// <summary>Usado pelos executores para informar etapa e progresso.</summary>
public sealed class Relator(FilaItem item, Action<bool> notificar)
{
    /// <summary>Muda a etapa. <paramref name="inicio"/>/<paramref name="fim"/> definem a faixa da barra (0..1) desta etapa.</summary>
    public void Etapa(string nome, double inicio, double fim)
    {
        item.Etapa = nome;
        item.Detalhe = null;
        _inicio = inicio;
        _fim = fim;
        item.Progresso = inicio;
        notificar(true);
    }

    /// <summary>Progresso dentro da etapa atual (0..1).</summary>
    public void Progresso(double fracao)
    {
        item.Progresso = _inicio + (_fim - _inicio) * Math.Clamp(fracao, 0, 1);
        notificar(false);
    }

    public void Detalhe(string? texto) => item.Detalhe = texto;

    private double _inicio, _fim = 1;
}

/// <summary>Fila de processamento: executa um item por vez, em segundo plano.</summary>
public sealed class FilaService
{
    private readonly List<FilaItem> _itens = [];
    private readonly object _trava = new();
    private bool _rodando;
    private DateTime _ultimaNotificacao = DateTime.MinValue;

    /// <summary>Disparado (em qualquer thread) com uma cópia segura da fila.</summary>
    public event Action<IReadOnlyList<FilaItem>>? Alterada;

    public bool Ocupada { get { lock (_trava) return _itens.Any(i => !i.Finalizado); } }

    public IReadOnlyList<FilaItem> Itens { get { lock (_trava) return Copiar(); } }

    public FilaItem Adicionar(FilaItem item)
    {
        if (item.Executor is null) throw new ArgumentException("Item sem executor.");
        lock (_trava) _itens.Add(item);
        Notificar(forcar: true);
        _ = ProcessarAsync();
        return item;
    }

    public void Cancelar(string id)
    {
        FilaItem? item;
        lock (_trava) item = _itens.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Finalizado) return;

        if (item.Estado == EstadoFila.Aguardando)
        {
            item.Estado = EstadoFila.Cancelado;
            item.Etapa = "Cancelado";
            Notificar(forcar: true);
        }
        else
        {
            item.Etapa = "Cancelando…";
            Notificar(forcar: true);
            item.Cts.Cancel(); // o executor encerra o processo e o loop marca como cancelado
        }
    }

    public void CancelarTodos()
    {
        foreach (var i in Itens.Where(i => !i.Finalizado)) Cancelar(i.Id);
    }

    public void Remover(string id)
    {
        lock (_trava) _itens.RemoveAll(i => i.Id == id && i.Finalizado);
        Notificar(forcar: true);
    }

    public void LimparFinalizados()
    {
        lock (_trava) _itens.RemoveAll(i => i.Finalizado);
        Notificar(forcar: true);
    }

    /// <summary>Espera a fila esvaziar (usado ao fechar o app depois de cancelar).</summary>
    public async Task AguardarOciosaAsync(TimeSpan limite)
    {
        var fim = DateTime.Now + limite;
        while (Ocupada && DateTime.Now < fim) await Task.Delay(100);
    }

    private async Task ProcessarAsync()
    {
        lock (_trava)
        {
            if (_rodando) return;
            _rodando = true;
        }
        try
        {
            while (true)
            {
                FilaItem? item;
                lock (_trava) item = _itens.FirstOrDefault(i => i.Estado == EstadoFila.Aguardando);
                if (item is null) break;

                item.Estado = EstadoFila.Processando;
                item.Etapa = "Iniciando";
                Notificar(forcar: true);
                try
                {
                    await Task.Run(() => item.Executor!(item, new Relator(item, Notificar), item.Cts.Token));
                    item.Estado = EstadoFila.Concluido;
                    item.Etapa = "Concluído";
                    item.Detalhe = null;
                    item.Progresso = 1;
                }
                catch (OperationCanceledException)
                {
                    item.Estado = EstadoFila.Cancelado;
                    item.Etapa = "Cancelado";
                }
                catch (Exception ex)
                {
                    item.Estado = EstadoFila.Erro;
                    item.Etapa = "Erro";
                    item.Mensagem = ex.Message;
                    Log.Erro($"Item da fila falhou: {item.Titulo}", ex);
                }
                Notificar(forcar: true);
            }
        }
        finally
        {
            lock (_trava) _rodando = false;
        }
    }

    private void Notificar(bool forcar)
    {
        // Progresso chega muitas vezes por segundo; a UI só precisa de ~5 atualizações/s.
        var agora = DateTime.Now;
        if (!forcar && (agora - _ultimaNotificacao).TotalMilliseconds < 200) return;
        _ultimaNotificacao = agora;
        IReadOnlyList<FilaItem> copia;
        lock (_trava) copia = Copiar();
        Alterada?.Invoke(copia);
    }

    private List<FilaItem> Copiar() => _itens.Select(i => new FilaItem
    {
        Id = i.Id, Tipo = i.Tipo, Titulo = i.Titulo, Categoria = i.Categoria, Origem = i.Origem,
        Estado = i.Estado, Etapa = i.Etapa, Detalhe = i.Detalhe, Progresso = i.Progresso, Mensagem = i.Mensagem,
        Slug = i.Slug, CriadoEm = i.CriadoEm,
    }).ToList();
}
