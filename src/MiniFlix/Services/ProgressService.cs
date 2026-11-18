namespace MiniFlix.Services;

public sealed class ProgressoVideo
{
    public double Posicao { get; set; }          // segundos
    public double Duracao { get; set; }          // segundos
    public bool Assistido { get; set; }          // já foi visto até o fim alguma vez
    public DateTime AtualizadoEm { get; set; }
}

/// <summary>
/// "Continuar assistindo": posição de cada vídeo em data/progress.json.
/// As gravações em disco são agrupadas (debounce) para não desgastar o pendrive:
/// o player informa a posição a cada poucos segundos, mas o arquivo só é escrito
/// depois de um intervalo sem novidades — e sempre ao fechar o app (<see cref="Gravar"/>).
/// </summary>
public sealed class ProgressService : IDisposable
{
    /// <summary>A partir desta fração o vídeo conta como assistido e a posição volta ao início.</summary>
    public const double FracaoAssistido = 0.95;

    private readonly string _arquivo;
    private readonly Dictionary<string, ProgressoVideo> _itens;
    private readonly object _trava = new();
    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _atraso;
    private bool _pendente;

    public ProgressService(string arquivo, TimeSpan? atrasoGravacao = null)
    {
        _arquivo = arquivo;
        _atraso = atrasoGravacao ?? TimeSpan.FromSeconds(3);
        _itens = JsonStore.LerSeguro<Dictionary<string, ProgressoVideo>>(arquivo) ?? [];
        _timer = new System.Threading.Timer(_ => Gravar(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Chave estável: o ID do vídeo (yt:…/local:…) ou, na falta dele, o slug.</summary>
    public static string Chave(string? id, string slug) => string.IsNullOrWhiteSpace(id) ? "slug:" + slug : id;

    public ProgressoVideo? Obter(string chave)
    {
        lock (_trava) return _itens.TryGetValue(chave, out var p) ? Copia(p) : null;
    }

    public ProgressoVideo Registrar(string chave, double posicao, double duracao)
    {
        if (!double.IsFinite(posicao) || !double.IsFinite(duracao) || duracao <= 0 || posicao < 0)
            throw new ArgumentException("Posição ou duração inválida.");
        posicao = Math.Min(posicao, duracao);

        ProgressoVideo p;
        lock (_trava)
        {
            if (!_itens.TryGetValue(chave, out p!)) _itens[chave] = p = new ProgressoVideo();
            p.Duracao = duracao;
            p.AtualizadoEm = DateTime.Now;
            if (posicao / duracao >= FracaoAssistido)
            {
                p.Assistido = true;
                p.Posicao = 0;            // na próxima vez começa do início
            }
            else
            {
                p.Posicao = Math.Round(posicao, 1);
            }
            AgendarGravacao();
            return Copia(p);
        }
    }

    /// <summary>"Começar do início": zera a posição, mantém o selo de assistido.</summary>
    public void Reiniciar(string chave)
    {
        lock (_trava)
        {
            if (!_itens.TryGetValue(chave, out var p)) return;
            p.Posicao = 0;
            p.AtualizadoEm = DateTime.Now;
            AgendarGravacao();
        }
    }

    /// <summary>Esquece o vídeo (ex.: ao excluí-lo do acervo).</summary>
    public void Remover(string chave)
    {
        lock (_trava)
        {
            if (_itens.Remove(chave)) AgendarGravacao();
        }
    }

    /// <summary>Grava agora se houver alterações pendentes.</summary>
    public void Gravar()
    {
        Dictionary<string, ProgressoVideo> copia;
        lock (_trava)
        {
            if (!_pendente) return;
            _pendente = false;
            copia = _itens.ToDictionary(kv => kv.Key, kv => Copia(kv.Value));
        }
        try { JsonStore.Gravar(_arquivo, copia); }
        catch (Exception ex)
        {
            Log.Erro("Falha ao gravar progress.json", ex);
            lock (_trava) _pendente = true; // tenta de novo na próxima
        }
    }

    private void AgendarGravacao()
    {
        _pendente = true;
        _timer.Change(_atraso, Timeout.InfiniteTimeSpan);
    }

    private static ProgressoVideo Copia(ProgressoVideo p) => new()
    {
        Posicao = p.Posicao, Duracao = p.Duracao, Assistido = p.Assistido, AtualizadoEm = p.AtualizadoEm,
    };

    public void Dispose()
    {
        _timer.Dispose();
        Gravar();
    }
}
