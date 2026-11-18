using MiniFlix.Models;

namespace MiniFlix.Services;

/// <summary>
/// Mantém o catálogo em memória. A fonte da verdade são os .json de cada pasta em /videos;
/// o catalog-cache.json só serve para a UI abrir instantaneamente enquanto a pasta é varrida.
/// </summary>
public sealed class CatalogService
{
    public const string SemCategoria = "Sem categoria";

    private readonly string _pastaVideos;
    private readonly string _arquivoCache;
    private readonly Func<string, string> _paraUrl;
    private readonly object _trava = new();
    private List<VideoItem> _itens = [];

    /// <summary>Disparado (em thread de fundo) quando uma varredura muda o catálogo.</summary>
    public event Action? Alterado;

    public CatalogService(string pastaVideos, string arquivoCache, Func<string, string> paraUrl)
    {
        _pastaVideos = pastaVideos;
        _arquivoCache = arquivoCache;
        _paraUrl = paraUrl;
    }

    public IReadOnlyList<VideoItem> Itens { get { lock (_trava) return _itens; } }

    public IReadOnlyList<string> Categorias()
    {
        var cultura = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        return Itens.Select(i => i.Meta.Categoria)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c == SemCategoria ? 1 : 0)
            .ThenBy(c => c, StringComparer.Create(cultura, ignoreCase: true))
            .ToList();
    }

    public VideoItem? Obter(string slug) => Itens.FirstOrDefault(i => i.Meta.Slug == slug);

    /// <summary>Carrega o cache (rápido) e dispara a varredura real em segundo plano.</summary>
    public void Iniciar()
    {
        var cache = JsonStore.LerSeguro<List<VideoMeta>>(_arquivoCache);
        if (cache is not null)
        {
            // Só aproveita do cache o que ainda existe no disco.
            var itens = cache.Select(MontarItem).OfType<VideoItem>().ToList();
            lock (_trava) _itens = Ordenar(itens);
        }
        Task.Run(() =>
        {
            try { LimparExclusoesPendentes(); Varrer(); }
            catch (Exception ex) { Log.Erro("Falha ao varrer /videos", ex); }
        });
    }

    /// <summary>Lê todas as pastas de /videos, atualiza memória e cache. Retorna true se algo mudou.</summary>
    public bool Varrer()
    {
        Directory.CreateDirectory(_pastaVideos);
        var novos = new List<VideoItem>();

        foreach (var pasta in Directory.EnumerateDirectories(_pastaVideos))
        {
            var slug = Path.GetFileName(pasta);
            if (!Slug.EhValido(slug))
            {
                Log.Aviso($"Pasta ignorada (nome fora do padrão de slug): {slug}");
                continue;
            }

            var arquivoJson = Path.Combine(pasta, slug + ".json");
            if (!File.Exists(arquivoJson)) continue; // download/importação incompleta: tratada na etapa da fila

            var meta = JsonStore.LerSeguro<VideoMeta>(arquivoJson);
            if (meta is null) continue;

            if (meta.Slug != slug)
            {
                Log.Aviso($"Slug divergente em {arquivoJson} ('{meta.Slug}'); usando o nome da pasta.");
                meta.Slug = slug;
            }

            var item = MontarItem(meta);
            if (item is null) Log.Aviso($"Vídeo sem .mp4 ignorado: {slug}");
            else novos.Add(item);
        }

        novos = Ordenar(novos);
        bool mudou;
        lock (_trava)
        {
            mudou = !Iguais(_itens, novos);
            _itens = novos;
        }

        if (mudou)
        {
            try { JsonStore.Gravar(_arquivoCache, novos.Select(i => i.Meta).ToList()); }
            catch (Exception ex) { Log.Erro("Falha ao gravar o cache do catálogo", ex); }
            Alterado?.Invoke();
        }
        Log.Info($"Catálogo: {novos.Count} vídeo(s){(mudou ? " (alterado)" : "")}.");
        return mudou;
    }

    // ---------- Edição e exclusão ----------

    public const int MaxTitulo = 200, MaxCategoria = 60, MaxDescricao = 10_000;
    private const string PrefixoExclusao = "_excluindo-";

    /// <summary>Altera título, categoria e descrição. O slug (nome da pasta) nunca muda.</summary>
    public VideoItem Editar(string slug, string? titulo, string? categoria, string? descricao)
    {
        var pasta = PastaDoVideo(slug);
        var arquivo = Path.Combine(pasta, slug + ".json");
        var meta = JsonStore.Ler<VideoMeta>(arquivo) ?? throw new KeyNotFoundException("Vídeo não encontrado.");

        titulo = (titulo ?? "").Trim();
        categoria = (categoria ?? "").Trim();
        descricao = (descricao ?? "").Trim();
        if (titulo.Length == 0) throw new ArgumentException("O título não pode ficar vazio.");
        if (titulo.Length > MaxTitulo) throw new ArgumentException($"O título pode ter no máximo {MaxTitulo} caracteres.");
        if (categoria.Length > MaxCategoria) throw new ArgumentException($"A categoria pode ter no máximo {MaxCategoria} caracteres.");
        if (descricao.Length > MaxDescricao) throw new ArgumentException($"A descrição pode ter no máximo {MaxDescricao} caracteres.");
        if (categoria.Equals(SemCategoria, StringComparison.OrdinalIgnoreCase)) categoria = "";

        // Reaproveita a grafia de uma categoria existente ("cursos" -> "Cursos").
        var existente = Categorias().FirstOrDefault(c => c.Equals(categoria, StringComparison.OrdinalIgnoreCase));
        if (existente is not null && existente != SemCategoria) categoria = existente;

        meta.Slug = slug;
        meta.Titulo = titulo;
        meta.Categoria = categoria;
        meta.Descricao = descricao;
        JsonStore.Gravar(arquivo, meta);
        Log.Info($"Vídeo editado: {slug}");

        Varrer();
        return Obter(slug) ?? throw new InvalidOperationException("Vídeo sumiu após a edição.");
    }

    /// <summary>
    /// Apaga a pasta do vídeo. Primeiro renomeia a pasta (operação atômica que falha se algum arquivo
    /// estiver em uso), então nunca sobra um vídeo "meio apagado" no catálogo.
    /// </summary>
    public async Task ExcluirAsync(string slug)
    {
        var pasta = PastaDoVideo(slug);
        var temporaria = Path.Combine(_pastaVideos, $"{PrefixoExclusao}{slug}-{Guid.NewGuid():N}");

        bool movida = false;
        for (int tentativa = 0; tentativa < 10 && !movida; tentativa++)
        {
            try { Directory.Move(pasta, temporaria); movida = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(250); }
        }
        if (!movida)
            throw new InvalidOperationException("Não foi possível excluir: o vídeo está em uso. Feche-o e tente novamente.");

        ApagarPastaTemporaria(temporaria);
        Log.Info($"Vídeo excluído: {slug}");
        Varrer();
    }

    /// <summary>Remove restos de exclusões interrompidas (app fechado no meio).</summary>
    public void LimparExclusoesPendentes()
    {
        if (!Directory.Exists(_pastaVideos)) return;
        foreach (var d in Directory.EnumerateDirectories(_pastaVideos, PrefixoExclusao + "*"))
            ApagarPastaTemporaria(d);
    }

    private static void ApagarPastaTemporaria(string pasta)
    {
        try { Directory.Delete(pasta, recursive: true); }
        catch (Exception ex) { Log.Aviso($"Não foi possível apagar {pasta} agora (será tentado no próximo início): {ex.Message}"); }
    }

    private string PastaDoVideo(string slug)
    {
        if (!Slug.EhValido(slug)) throw new ArgumentException("Identificador de vídeo inválido.");
        var pasta = Path.GetFullPath(Path.Combine(_pastaVideos, slug));
        if (!string.Equals(Path.GetDirectoryName(pasta), Path.GetFullPath(_pastaVideos).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Identificador de vídeo inválido.");
        if (!Directory.Exists(pasta)) throw new KeyNotFoundException("Vídeo não encontrado.");
        return pasta;
    }

    private VideoItem? MontarItem(VideoMeta meta)
    {
        if (!Slug.EhValido(meta.Slug)) return null;
        var pasta = Path.Combine(_pastaVideos, meta.Slug);
        var mp4 = new FileInfo(Path.Combine(pasta, meta.Slug + ".mp4"));
        if (!mp4.Exists) return null;

        if (string.IsNullOrWhiteSpace(meta.Titulo)) meta.Titulo = meta.Slug;
        if (string.IsNullOrWhiteSpace(meta.Categoria)) meta.Categoria = SemCategoria;
        meta.Categoria = meta.Categoria.Trim();

        var capa = Path.Combine(pasta, meta.Slug + ".jpg");
        return new VideoItem(meta, _paraUrl(mp4.FullName), File.Exists(capa) ? _paraUrl(capa) : null, mp4.Length);
    }

    private static List<VideoItem> Ordenar(List<VideoItem> itens) =>
        itens.OrderByDescending(i => i.Meta.DataAdicionado).ThenBy(i => i.Meta.Slug, StringComparer.Ordinal).ToList();

    private static bool Iguais(List<VideoItem> a, List<VideoItem> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (x.Meta.Slug != y.Meta.Slug || x.Meta.Titulo != y.Meta.Titulo || x.Meta.Categoria != y.Meta.Categoria ||
                x.Meta.Descricao != y.Meta.Descricao || x.Meta.DuracaoSegundos != y.Meta.DuracaoSegundos ||
                x.CapaUrl != y.CapaUrl || x.TamanhoBytes != y.TamanhoBytes)
                return false;
        }
        return true;
    }
}
