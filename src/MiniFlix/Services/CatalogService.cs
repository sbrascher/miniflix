using MiniFlix.Models;

namespace MiniFlix.Services;

/// <summary>
/// Mantém o catálogo em memória. A fonte da verdade são os .json de cada pasta em /videos;
/// o catalog-cache.json só serve para a UI abrir instantaneamente enquanto a pasta é varrida.
/// </summary>
public sealed class CatalogService
{
    public const string SemCategoria = "Sem categoria";

    private volatile string _pastaVideos;
    private volatile bool _criarSeFaltar;
    private readonly string _arquivoCache;
    private readonly Func<string, string> _paraUrl;
    private readonly object _trava = new();
    private List<VideoItem> _itens = [];

    /// <summary>Disparado (em thread de fundo) quando uma varredura muda o catálogo.</summary>
    public event Action? Alterado;

    /// <param name="criarSeFaltar">true para a pasta padrão (é criada); false para uma pasta escolhida
    /// pelo usuário, que pode estar num disco desconectado (aí o catálogo fica vazio e avisa).</param>
    public CatalogService(string pastaVideos, string arquivoCache, Func<string, string> paraUrl, bool criarSeFaltar = true)
    {
        _pastaVideos = pastaVideos;
        _criarSeFaltar = criarSeFaltar;
        _arquivoCache = arquivoCache;
        _paraUrl = paraUrl;
    }

    public IReadOnlyList<VideoItem> Itens { get { lock (_trava) return _itens; } }

    /// <summary>Pasta onde está o acervo agora.</summary>
    public string PastaVideos => _pastaVideos;

    /// <summary>false se a pasta configurada não existe (ex.: HD externo desconectado).</summary>
    public bool PastaDisponivel => Directory.Exists(_pastaVideos);

    /// <summary>Passa a usar outra pasta como acervo (os arquivos não são movidos) e relê o catálogo.</summary>
    public void TrocarPasta(string novaPasta, bool criarSeFaltar)
    {
        lock (_travaPublicacao)
        {
            _pastaVideos = novaPasta;
            _criarSeFaltar = criarSeFaltar;
        }
        Log.Info($"Pasta de vídeos: {novaPasta}");
        try { LimparExclusoesPendentes(); } catch (Exception ex) { Log.Erro("Falha ao limpar a nova pasta de vídeos", ex); }
        Varrer();
    }

    /// <summary>Todas as tags em uso, em ordem alfabética (pt-BR, sem diferenciar maiúsculas).</summary>
    public IReadOnlyList<string> Tags()
    {
        var cultura = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        return Itens.SelectMany(i => i.Meta.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Create(cultura, ignoreCase: true))
            .ToList();
    }

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
            var pastaVideos = _pastaVideos;
            var itens = cache.Select(m => MontarItem(m, pastaVideos)).OfType<VideoItem>().ToList();
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
        var pastaVideos = _pastaVideos;
        if (_criarSeFaltar) Directory.CreateDirectory(pastaVideos);
        var novos = new List<VideoItem>();

        if (!Directory.Exists(pastaVideos))
            Log.Aviso($"Pasta de vídeos não encontrada: {pastaVideos}");
        else foreach (var pasta in Directory.EnumerateDirectories(pastaVideos))
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

            var item = MontarItem(meta, pastaVideos);
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

    public const int MaxTitulo = 200, MaxCategoria = 60, MaxDescricao = 10_000, MaxTag = 30, MaxTags = 20;
    private const string PrefixoExclusao = "_excluindo-";

    /// <summary>
    /// Limpa e valida as tags: tira espaços extras, ignora vazias e repetidas (sem diferenciar
    /// maiúsculas) e reaproveita a grafia de uma tag já existente no acervo ("Python" -> "python").
    /// </summary>
    public List<string> NormalizarTags(IEnumerable<string?> tags)
    {
        var existentes = Tags();
        var resultado = new List<string>();
        foreach (var bruta in tags)
        {
            var tag = string.Join(' ', (bruta ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTag) throw new ArgumentException($"Cada tag pode ter no máximo {MaxTag} caracteres (“{tag}”).");
            tag = existentes.FirstOrDefault(e => e.Equals(tag, StringComparison.OrdinalIgnoreCase)) ?? tag;
            if (!resultado.Contains(tag, StringComparer.OrdinalIgnoreCase)) resultado.Add(tag);
        }
        if (resultado.Count > MaxTags) throw new ArgumentException($"Um vídeo pode ter no máximo {MaxTags} tags.");
        return resultado;
    }

    /// <summary>Altera título, categoria, descrição e tags (null = manter). O slug (nome da pasta) nunca muda.</summary>
    public VideoItem Editar(string slug, string? titulo, string? categoria, string? descricao, IEnumerable<string?>? tags = null)
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
        if (tags is not null) meta.Tags = NormalizarTags(tags);
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

    /// <summary>Remove restos de exclusões, importações/downloads e prévias de capa interrompidos (app fechado no meio).</summary>
    public void LimparExclusoesPendentes()
    {
        if (!Directory.Exists(_pastaVideos)) return;
        foreach (var prefixo in new[] { PrefixoExclusao, PrefixoTemporario })
            foreach (var d in Directory.EnumerateDirectories(_pastaVideos, prefixo + "*"))
                ApagarPastaTemporaria(d);
        // Só um nível (<slug>/<slug>.previa.jpg): a pasta pode ser escolhida pelo usuário e ter outras coisas.
        foreach (var dir in Directory.EnumerateDirectories(_pastaVideos))
        {
            var previa = Path.Combine(dir, Path.GetFileName(dir) + SufixoPrevia);
            try { if (File.Exists(previa)) File.Delete(previa); } catch (IOException) { }
        }
    }

    // ---------- Troca de capa ----------
    // A capa nova é gerada primeiro como prévia (<slug>.previa.jpg, ignorada pela varredura) e só
    // substitui <slug>.jpg ao confirmar — assim "Cancelar" no modal não perde a capa atual.

    private const string SufixoPrevia = ".previa.jpg";

    /// <summary>Caminho onde a prévia da nova capa deve ser gravada.</summary>
    public string CaminhoPreviaCapa(string slug) => Path.Combine(PastaDoVideo(slug), slug + SufixoPrevia);

    public string UrlPreviaCapa(string slug)
    {
        var previa = new FileInfo(CaminhoPreviaCapa(slug));
        if (!previa.Exists) throw new InvalidOperationException("A prévia da capa não foi gerada.");
        return UrlVersionada(previa);
    }

    public string CaminhoVideo(string slug) => Path.Combine(PastaDoVideo(slug), slug + ".mp4");

    /// <summary>Substitui a capa pela prévia (rename atômico na mesma pasta) e atualiza o catálogo.</summary>
    public VideoItem ConfirmarCapa(string slug)
    {
        var previa = CaminhoPreviaCapa(slug);
        if (!File.Exists(previa)) throw new InvalidOperationException("Não há uma nova capa para salvar.");
        File.Move(previa, Path.Combine(PastaDoVideo(slug), slug + ".jpg"), overwrite: true);
        Log.Info($"Capa alterada: {slug}");
        Varrer();
        return Obter(slug) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
    }

    public void DescartarPreviaCapa(string slug)
    {
        var previa = CaminhoPreviaCapa(slug);
        if (File.Exists(previa)) File.Delete(previa);
    }

    // ---------- Novos vídeos (importação e download) ----------

    private const string PrefixoTemporario = "_tmp-";
    private readonly object _travaPublicacao = new();

    public bool ExisteId(string id) => Itens.Any(i => i.Meta.Id == id);

    public VideoItem? ObterPorId(string id) => Itens.FirstOrDefault(i => i.Meta.Id == id);

    /// <summary>
    /// Cria uma pasta de trabalho dentro de /videos (mesma unidade: a publicação é só um rename).
    /// O prefixo faz a varredura ignorá-la e a limpeza do início apagá-la se o app cair.
    /// </summary>
    public string CriarPastaTemporaria()
    {
        var pasta = Path.Combine(_pastaVideos, PrefixoTemporario + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        return pasta;
    }

    public static void DescartarPastaTemporaria(string pasta)
    {
        if (Directory.Exists(pasta)) ApagarPastaTemporaria(pasta);
    }

    /// <summary>
    /// Publica um vídeo pronto: a pasta temporária deve conter "video.mp4" (e opcionalmente "capa.jpg").
    /// Escolhe um slug livre, renomeia os arquivos, grava o JSON e move a pasta. Retorna o slug final.
    /// </summary>
    public string Publicar(string pastaTemporaria, VideoMeta meta)
    {
        lock (_travaPublicacao)
        {
            var slug = Slug.Unico(Slug.Gerar(meta.Titulo), s => Directory.Exists(Path.Combine(_pastaVideos, s)));
            File.Move(Path.Combine(pastaTemporaria, "video.mp4"), Path.Combine(pastaTemporaria, slug + ".mp4"));
            var capa = Path.Combine(pastaTemporaria, "capa.jpg");
            if (File.Exists(capa)) File.Move(capa, Path.Combine(pastaTemporaria, slug + ".jpg"));

            meta.Slug = slug;
            if (meta.DataAdicionado == default) meta.DataAdicionado = DateTime.Now;
            JsonStore.Gravar(Path.Combine(pastaTemporaria, slug + ".json"), meta);

            Directory.Move(pastaTemporaria, Path.Combine(_pastaVideos, slug));
            Log.Info($"Vídeo publicado: {slug} ({meta.Origem})");
        }
        Varrer();
        return meta.Slug;
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

    private VideoItem? MontarItem(VideoMeta meta, string pastaVideos)
    {
        if (!Slug.EhValido(meta.Slug)) return null;
        var pasta = Path.Combine(pastaVideos, meta.Slug);
        var mp4 = new FileInfo(Path.Combine(pasta, meta.Slug + ".mp4"));
        if (!mp4.Exists) return null;

        if (string.IsNullOrWhiteSpace(meta.Titulo)) meta.Titulo = meta.Slug;
        if (string.IsNullOrWhiteSpace(meta.Categoria)) meta.Categoria = SemCategoria;
        meta.Categoria = meta.Categoria.Trim();
        meta.Tags = (meta.Tags ?? []).Select(t => t?.Trim() ?? "").Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var capa = new FileInfo(Path.Combine(pasta, meta.Slug + ".jpg"));
        return new VideoItem(meta, _paraUrl(mp4.FullName), capa.Exists ? UrlVersionada(capa) : null, mp4.Length);
    }

    /// <summary>URL com a data de modificação: quando a capa muda, o navegador não usa a imagem antiga do cache.</summary>
    private string UrlVersionada(FileInfo arquivo) => $"{_paraUrl(arquivo.FullName)}?v={arquivo.LastWriteTimeUtc.Ticks}";

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
                !x.Meta.Tags.SequenceEqual(y.Meta.Tags) || x.VideoUrl != y.VideoUrl ||
                x.CapaUrl != y.CapaUrl || x.TamanhoBytes != y.TamanhoBytes)
                return false;
        }
        return true;
    }
}
