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
        lock (_travaVarredura) return VarrerCore();
    }

    private bool VarrerCore()
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
        lock (_travaEscrita) return EditarCore(slug, titulo, categoria, descricao, tags);
    }

    private VideoItem EditarCore(string slug, string? titulo, string? categoria, string? descricao, IEnumerable<string?>? tags)
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
    /// Grava a transcrição do vídeo: o .md (e a legenda .vtt original, para o player usar depois) ao lado
    /// do vídeo, e o idioma/origem no .json. Gravação atômica: um .md pela metade nunca substitui o anterior.
    /// </summary>
    public VideoItem SalvarTranscricao(string slug, string markdown, string? vtt, Transcricao.Escolha escolha)
    {
        lock (_travaEscrita)
        {
            var pasta = PastaDoVideo(slug);
            void GravarAtomico(string destino, string conteudo)
            {
                var tmp = destino + ".tmp";
                File.WriteAllText(tmp, conteudo, new System.Text.UTF8Encoding(false));
                File.Move(tmp, destino, overwrite: true);
            }
            GravarAtomico(Path.Combine(pasta, slug + Transcricao.SufixoMarkdown), markdown);
            if (vtt is not null) GravarAtomico(Path.Combine(pasta, slug + Transcricao.SufixoLegenda), vtt);

            var arquivoJson = Path.Combine(pasta, slug + ".json");
            var meta = JsonStore.Ler<VideoMeta>(arquivoJson) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
            meta.TranscricaoIdioma = escolha.Chave;
            meta.TranscricaoAutomatica = escolha.Automatica;
            JsonStore.Gravar(arquivoJson, meta);
            Log.Info($"Transcrição salva: {slug} ({escolha.Chave}{(escolha.Automatica ? ", automática" : "")})");
        }
        Varrer();
        return Obter(slug) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
    }

    /// <summary>Marca ou desmarca o vídeo como favorito.</summary>
    public VideoItem DefinirFavorito(string slug, bool favorito)
    {
        lock (_travaEscrita) return DefinirFavoritoCore(slug, favorito);
    }

    private VideoItem DefinirFavoritoCore(string slug, bool favorito)
    {
        var arquivo = Path.Combine(PastaDoVideo(slug), slug + ".json");
        var meta = JsonStore.Ler<VideoMeta>(arquivo) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
        if (meta.Favorito != favorito)
        {
            meta.Favorito = favorito;
            JsonStore.Gravar(arquivo, meta);
            Log.Info($"Favorito {(favorito ? "marcado" : "desmarcado")}: {slug}");
            Varrer();
        }
        return Obter(slug) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
    }

    /// <summary>
    /// Adiciona e/ou remove tags de vários vídeos de uma vez (as demais tags de cada vídeo continuam).
    /// Valida tudo antes de gravar: se algum vídeo passaria do limite de tags, nenhum é alterado.
    /// Retorna quantos vídeos mudaram de fato.
    /// </summary>
    /// <summary>
    /// Põe vários vídeos na mesma categoria. Vazia ou "Sem categoria" tira a categoria; o nome de uma
    /// categoria existente é reaproveitado com a grafia dela ("cursos" → "Cursos"). Retorna quantos mudaram.
    /// </summary>
    public int DefinirCategoriaEmMassa(IEnumerable<string> slugs, string? categoria)
    {
        lock (_travaEscrita)
        {
            categoria = (categoria ?? "").Trim();
            if (categoria.Length > MaxCategoria) throw new ArgumentException($"A categoria pode ter no máximo {MaxCategoria} caracteres.");
            if (categoria.Equals(SemCategoria, StringComparison.OrdinalIgnoreCase)) categoria = "";
            var existente = Categorias().FirstOrDefault(c => c != SemCategoria && c.Equals(categoria, StringComparison.OrdinalIgnoreCase));
            if (existente is not null) categoria = existente;

            var lista = slugs.Distinct(StringComparer.Ordinal).ToList();
            if (lista.Count == 0) throw new ArgumentException("Nenhum vídeo selecionado.");

            // Lê e valida todos antes de gravar o primeiro.
            var gravar = new List<(string Arquivo, VideoMeta Meta)>();
            foreach (var slug in lista)
            {
                var arquivo = Path.Combine(PastaDoVideo(slug), slug + ".json");
                var meta = JsonStore.Ler<VideoMeta>(arquivo) ?? throw new KeyNotFoundException($"Vídeo não encontrado: {slug}.");
                if ((meta.Categoria ?? "").Trim() == categoria) continue;
                meta.Categoria = categoria;
                gravar.Add((arquivo, meta));
            }
            try
            {
                foreach (var (arquivo, meta) in gravar) JsonStore.Gravar(arquivo, meta);
            }
            finally
            {
                Log.Info($"Categoria em massa → “{(categoria.Length == 0 ? SemCategoria : categoria)}”: {gravar.Count} vídeo(s).");
                if (gravar.Count > 0) Varrer();
            }
            return gravar.Count;
        }
    }

    public int AlterarTagsEmMassa(IEnumerable<string> slugs, IEnumerable<string?> adicionar, IEnumerable<string?> remover)
    {
        lock (_travaEscrita) return AlterarTagsEmMassaCore(slugs, adicionar, remover);
    }

    private int AlterarTagsEmMassaCore(IEnumerable<string> slugs, IEnumerable<string?> adicionar, IEnumerable<string?> remover)
    {
        var novas = NormalizarTags(adicionar);
        var tirar = remover.Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).ToList();
        if (novas.Count == 0 && tirar.Count == 0) throw new ArgumentException("Informe ao menos uma tag.");

        var lista = slugs.Distinct(StringComparer.Ordinal).ToList();
        if (lista.Count == 0) throw new ArgumentException("Nenhum vídeo selecionado.");

        // 1) Calcula tudo (e valida) sem gravar nada.
        var gravar = new List<(string Arquivo, VideoMeta Meta)>();
        foreach (var slug in lista)
        {
            var arquivo = Path.Combine(PastaDoVideo(slug), slug + ".json");
            var meta = JsonStore.Ler<VideoMeta>(arquivo) ?? throw new KeyNotFoundException($"Vídeo não encontrado: {slug}.");
            var atuais = meta.Tags ?? [];
            var resultado = atuais.Where(t => !tirar.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var t in novas)
                if (!resultado.Contains(t, StringComparer.OrdinalIgnoreCase)) resultado.Add(t);
            if (resultado.Count > MaxTags)
                throw new ArgumentException($"“{meta.Titulo}” ficaria com {resultado.Count} tags; o máximo é {MaxTags}. Nada foi alterado.");
            if (resultado.SequenceEqual(atuais)) continue;
            meta.Tags = resultado;
            gravar.Add((arquivo, meta));
        }

        // 2) Grava só o que mudou.
        try
        {
            foreach (var (arquivo, meta) in gravar) JsonStore.Gravar(arquivo, meta);
        }
        finally
        {
            Log.Info($"Tags em massa (+{string.Join(", ", novas)} −{string.Join(", ", tirar)}): {gravar.Count} vídeo(s).");
            if (gravar.Count > 0) Varrer();
        }
        return gravar.Count;
    }

    /// <summary>
    /// Troca a categoria de todos os vídeos de <paramref name="antiga"/> para <paramref name="nova"/>.
    /// Se <paramref name="nova"/> já existir (outra categoria), as duas são unidas usando a grafia existente;
    /// vazia ou "Sem categoria" tira a categoria dos vídeos. Retorna quantos vídeos foram alterados.
    /// </summary>
    public int RenomearCategoria(string antiga, string? nova)
    {
        lock (_travaEscrita) return RenomearCategoriaCore(antiga, nova);
    }

    // ---------- Descrição das categorias ----------
    // Fica na própria pasta dos vídeos (acompanha o acervo em backups e na troca de pasta); a varredura
    // só lê subpastas, então o arquivo não atrapalha.

    public const int MaxDescricaoCategoria = 500;
    private const string NomeArquivoCategorias = "_categorias.json";
    private string ArquivoCategorias => Path.Combine(_pastaVideos, NomeArquivoCategorias);

    /// <summary>Descrição de cada categoria (só as que têm), sem diferenciar maiúsculas no nome.</summary>
    public IReadOnlyDictionary<string, string> DescricoesCategorias()
    {
        var lidas = JsonStore.LerSeguro<Dictionary<string, string>>(ArquivoCategorias) ?? [];
        return new Dictionary<string, string>(lidas, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Define (ou apaga, se vazia) a descrição de uma categoria.</summary>
    public void DefinirDescricaoCategoria(string categoria, string? descricao)
    {
        lock (_travaEscrita) DefinirDescricaoCore(categoria, descricao);
        Alterado?.Invoke(); // a varredura não percebe: avisa a UI diretamente
    }

    private void DefinirDescricaoCore(string categoria, string? descricao)
    {
        categoria = (categoria ?? "").Trim();
        descricao = (descricao ?? "").Trim();
        if (categoria.Length == 0 || categoria.Equals(SemCategoria, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("“Sem categoria” não tem descrição.");
        if (descricao.Length > MaxDescricaoCategoria)
            throw new ArgumentException($"A descrição da categoria pode ter no máximo {MaxDescricaoCategoria} caracteres.");

        var todas = new Dictionary<string, string>(DescricoesCategorias(), StringComparer.OrdinalIgnoreCase);
        var chave = todas.Keys.FirstOrDefault(k => k.Equals(categoria, StringComparison.OrdinalIgnoreCase));
        if (chave is not null) todas.Remove(chave);
        if (descricao.Length > 0) todas[categoria] = descricao;
        JsonStore.Gravar(ArquivoCategorias, todas.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.Key, p => p.Value));
    }

    /// <summary>
    /// Edita uma categoria: renomeia (todos os vídeos dela; se o nome novo já existir, une) e define a
    /// descrição (null = manter). A descrição acompanha o nome novo. Retorna quantos vídeos mudaram.
    /// </summary>
    public int EditarCategoria(string antiga, string? nova, string? descricao)
    {
        int alterados;
        lock (_travaEscrita)
        {
            antiga = (antiga ?? "").Trim();
            if (descricao is not null && descricao.Trim().Length > MaxDescricaoCategoria)
                throw new ArgumentException($"A descrição da categoria pode ter no máximo {MaxDescricaoCategoria} caracteres.");
            var descricaoAntiga = DescricoesCategorias().GetValueOrDefault(antiga);
            alterados = RenomearCategoriaCore(antiga, nova);

            // Nome final (com a grafia de uma categoria existente, se houve união).
            var final = (nova ?? "").Trim();
            if (final.Equals(SemCategoria, StringComparison.OrdinalIgnoreCase)) final = "";
            final = Categorias().FirstOrDefault(c => c.Equals(final, StringComparison.OrdinalIgnoreCase)) ?? final;

            if (final.Length > 0 && final != SemCategoria)
            {
                var descricaoFinal = DescricoesCategorias().GetValueOrDefault(final);
                // Pedida explicitamente > a que o destino já tinha (união) > a da categoria antiga.
                var aplicar = descricao ?? descricaoFinal ?? descricaoAntiga;
                if (aplicar != descricaoFinal) DefinirDescricaoCore(final, aplicar);
            }
            if (!antiga.Equals(final, StringComparison.OrdinalIgnoreCase) && descricaoAntiga is not null
                && antiga != SemCategoria)
                DefinirDescricaoCore(antiga, null); // o nome antigo deixou de existir
        }
        Alterado?.Invoke();
        return alterados;
    }

    private int RenomearCategoriaCore(string antiga, string? nova)
    {
        antiga = (antiga ?? "").Trim();
        nova = (nova ?? "").Trim();
        if (nova.Length > MaxCategoria) throw new ArgumentException($"A categoria pode ter no máximo {MaxCategoria} caracteres.");
        if (nova.Equals(SemCategoria, StringComparison.OrdinalIgnoreCase)) nova = "";

        // Une com outra categoria existente reaproveitando a grafia dela; mudar só maiúsculas/minúsculas da própria vale.
        var existente = Categorias().FirstOrDefault(c => c != SemCategoria &&
            c.Equals(nova, StringComparison.OrdinalIgnoreCase) && !c.Equals(antiga, StringComparison.OrdinalIgnoreCase));
        if (existente is not null) nova = existente;

        var alvo = Itens.Where(i => i.Meta.Categoria.Equals(antiga, StringComparison.OrdinalIgnoreCase)).ToList();
        if (alvo.Count == 0) throw new KeyNotFoundException($"A categoria “{antiga}” não existe mais.");
        if (nova == (antiga == SemCategoria ? "" : antiga)) return 0; // nada muda

        int alterados = 0;
        try
        {
            foreach (var item in alvo)
            {
                // Lê do disco (o item em memória tem campos preenchidos para exibição, como "Sem categoria").
                var arquivo = Path.Combine(PastaDoVideo(item.Meta.Slug), item.Meta.Slug + ".json");
                var meta = JsonStore.Ler<VideoMeta>(arquivo);
                if (meta is null) continue;
                meta.Categoria = nova;
                JsonStore.Gravar(arquivo, meta);
                alterados++;
            }
        }
        finally
        {
            Log.Info($"Categoria “{antiga}” → “{(nova.Length == 0 ? SemCategoria : nova)}”: {alterados} vídeo(s).");
            Varrer(); // mesmo se falhar no meio, o catálogo mostra o que já foi gravado
        }
        return alterados;
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
            var nome = Path.GetFileName(dir);
            var previa = Path.Combine(dir, nome + SufixoPrevia);
            try { if (File.Exists(previa)) File.Delete(previa); } catch (IOException) { }

            // Reimportação interrompida (app fechado no meio da troca): se o vídeo novo não chegou, volta o antigo.
            var backup = Path.Combine(dir, nome + SufixoSubstituindo);
            if (!File.Exists(backup)) continue;
            var mp4 = Path.Combine(dir, nome + ".mp4");
            try { if (File.Exists(mp4)) File.Delete(backup); else File.Move(backup, mp4); }
            catch (IOException ex) { Log.Aviso($"Não foi possível resolver {backup}: {ex.Message}"); }
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
        lock (_travaEscrita) return ConfirmarCapaCore(slug);
    }

    private VideoItem ConfirmarCapaCore(string slug)
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
    /// <summary>
    /// Serializa as alterações de metadados: a ponte atende comandos em paralelo, e duas edições do
    /// mesmo .json ao mesmo tempo (ex.: favoritar e logo em seguida aplicar tags em massa) fariam a
    /// segunda gravar por cima da primeira.
    /// </summary>
    private readonly object _travaEscrita = new();
    private readonly object _travaVarredura = new();

    public bool ExisteId(string id) => Itens.Any(i => i.Meta.Id == id);

    public VideoItem? ObterPorId(string id) => Itens.FirstOrDefault(i => i.Meta.Id == id);

    /// <summary>Vídeo do catálogo importado deste mesmo arquivo (compara com a impressão atual e com a antiga).</summary>
    public VideoItem? ObterPorImpressao(ImpressaoDigital.Ids ids) => Itens.FirstOrDefault(i => ids.Contem(i.Meta.Id));

    // ---------- Reimportação ----------

    private const string SufixoSubstituindo = ".mp4.substituindo";

    /// <summary>
    /// Troca só o arquivo de vídeo de um vídeo existente, mantendo título, categoria, tags, favorito,
    /// descrição, capa, Id (e com ele o "continuar assistindo") e o nome da pasta.
    /// <paramref name="pastaTemporaria"/> contém "video.mp4" (e opcionalmente "capa.jpg", usada só se o
    /// vídeo não tiver capa). O vídeo antigo vira um backup e só é apagado depois que o novo está no lugar.
    /// </summary>
    public VideoItem SubstituirVideo(string slug, string pastaTemporaria, double duracaoSegundos)
    {
        lock (_travaEscrita)
        {
            var pasta = PastaDoVideo(slug);
            var mp4 = Path.Combine(pasta, slug + ".mp4");
            var backup = Path.Combine(pasta, slug + SufixoSubstituindo);
            var arquivoJson = Path.Combine(pasta, slug + ".json");
            var meta = JsonStore.Ler<VideoMeta>(arquivoJson) ?? throw new KeyNotFoundException("Vídeo não encontrado.");

            // 1) Tira o antigo do caminho (falha se estiver aberto no player).
            if (File.Exists(backup)) File.Delete(backup);
            bool movido = false;
            for (int tentativa = 0; tentativa < 10 && !movido; tentativa++)
            {
                try { File.Move(mp4, backup); movido = true; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
            }
            if (!movido)
                throw new InvalidOperationException("Não foi possível reimportar: o vídeo está aberto. Feche-o e tente novamente.");

            // 2) Coloca o novo; se der errado, devolve o antigo.
            try { File.Move(Path.Combine(pastaTemporaria, "video.mp4"), mp4); }
            catch
            {
                File.Move(backup, mp4);
                throw;
            }

            var capaNova = Path.Combine(pastaTemporaria, "capa.jpg");
            var capa = Path.Combine(pasta, slug + ".jpg");
            if (!File.Exists(capa) && File.Exists(capaNova)) File.Move(capaNova, capa);

            meta.Slug = slug;
            meta.DuracaoSegundos = Math.Round(duracaoSegundos, 2);
            JsonStore.Gravar(arquivoJson, meta);
            try { File.Delete(backup); } catch (IOException) { /* apagado na próxima abertura */ }
            Log.Info($"Vídeo reimportado: {slug} (metadados mantidos)");
        }
        Varrer();
        return Obter(slug) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
    }

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
        var transcricao = new FileInfo(Path.Combine(pasta, meta.Slug + Transcricao.SufixoMarkdown));
        return new VideoItem(meta, _paraUrl(mp4.FullName), capa.Exists ? UrlVersionada(capa) : null, mp4.Length,
            transcricao.Exists ? UrlVersionada(transcricao) : null);
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
                !x.Meta.Tags.SequenceEqual(y.Meta.Tags) || x.Meta.Favorito != y.Meta.Favorito || x.VideoUrl != y.VideoUrl ||
                x.TranscricaoUrl != y.TranscricaoUrl || x.Meta.TranscricaoIdioma != y.Meta.TranscricaoIdioma ||
                x.CapaUrl != y.CapaUrl || x.TamanhoBytes != y.TamanhoBytes)
                return false;
        }
        return true;
    }
}
