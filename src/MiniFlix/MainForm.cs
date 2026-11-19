using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix;

internal sealed class MainForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Bridge _bridge;
    private readonly CatalogService _catalogo;
    private readonly SettingsService _config;
    private readonly FilaService _fila = new();
    private readonly ProgressService _progresso = new(Path.Combine(AppPaths.Data, "progress.json"));
    private readonly ImportacaoLocal _importacao;
    private readonly DownloadYoutube _youtube;
    // Arquivos escolhidos no diálogo nativo. A UI só recebe um token: não pode pedir para importar caminhos arbitrários.
    private readonly Dictionary<string, string> _arquivosEscolhidos = [];
    private bool _saindo;
    private readonly string[] _args = Environment.GetCommandLineArgs();
    private string _runtimeVersion = "?";
    private string _runtimeSource = "?";

    public MainForm()
    {
        Text = "MiniFlix";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;
        // Mesmo ícone do .exe (barra de título e barra de tarefas).
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _config = new SettingsService(Path.Combine(AppPaths.Data, "settings.json"));
        BackColor = _config.Atual.Tema == "claro" ? Color.FromArgb(250, 250, 250) : Color.FromArgb(12, 12, 14);

        _bridge = new Bridge(json => RunOnUi(() => _webView.CoreWebView2?.PostWebMessageAsJson(json)));
        var pastaVideos = PastaVideos.Resolver(_config.Atual.PastaVideos, AppPaths.Root, AppPaths.VideosPadrao);
        _catalogo = new CatalogService(pastaVideos, Path.Combine(AppPaths.Data, "catalog-cache.json"),
            p => AppPaths.ToVideoUrl(_catalogo!.PastaVideos, p),
            criarSeFaltar: PastaVideos.MesmoCaminho(pastaVideos, AppPaths.VideosPadrao));
        _catalogo.Alterado += () => _bridge.Emitir("catalogo.atualizado");
        Ferramentas.PastaBin = AppPaths.Bin;
        _importacao = new ImportacaoLocal(_catalogo);
        _youtube = new DownloadYoutube(_catalogo);
        _fila.Alterada += itens => _bridge.Emitir("fila.atualizada", itens);
        RegistrarComandos();

        FormClosed += (_, _) => _progresso.Dispose(); // grava posições pendentes

        // Fechar com a fila trabalhando: a UI pergunta (modal próprio) e responde com "app.sair".
        FormClosing += (_, e) =>
        {
            if (_saindo || !_fila.Ocupada || _webView.CoreWebView2 is null) return;
            e.Cancel = true;
            _bridge.Emitir("app.confirmarSaida");
        };

        Controls.Add(_webView);
        Load += async (_, _) =>
        {
            // Qualquer falha ao iniciar a interface vira uma explicação, nunca um erro técnico solto.
            try { await InitWebViewAsync(); }
            catch (Exception ex) { FalhaAoIniciar(ex); }
        };
    }

    private void RunOnUi(Action acao)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(acao); else acao();
    }

    // ---------- Comandos da ponte ----------

    private void RegistrarComandos()
    {
        _bridge.Registrar("ping", _ => new { pong = DateTime.Now.ToString("O") });
        _bridge.Registrar("sistema.info", _ => SistemaInfo());

        _bridge.Registrar("config.obter", _ => _config.Atual);
        _bridge.Registrar("config.salvar", d =>
        {
            // Só o tema vem da UI; a pasta de vídeos tem comandos próprios (com validação e diálogo nativo).
            var tema = Bridge.Texto(d, "tema");
            var salvo = _config.Alterar(c => c.Tema = tema);
            RunOnUi(() => BackColor = salvo.Tema == "claro" ? Color.FromArgb(250, 250, 250) : Color.FromArgb(12, 12, 14));
            return salvo;
        });

        _bridge.Registrar("catalogo.listar", _ => new
        {
            videos = _catalogo.Itens.Select(ParaUi),
            categorias = _catalogo.Categorias(),
            tags = _catalogo.Tags(),
            pasta = InfoPasta(),
        });
        _bridge.Registrar("catalogo.recarregar", async _ =>
        {
            await Task.Run(_catalogo.Varrer);
            var recarregarPagina = MapearPastaVideos(); // o disco pode ter sido reconectado
            _bridge.Emitir("catalogo.atualizado");
            return (object?)new { recarregarPagina, pasta = InfoPasta() };
        });

        // ---- Pasta dos vídeos ----
        _bridge.Registrar("pasta.escolher", async _ =>
        {
            string? escolhida = Environment.GetEnvironmentVariable("MINIFLIX_TESTE_PASTA"); // gancho de teste
            if (string.IsNullOrEmpty(escolhida))
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Escolha a pasta onde o MiniFlix vai guardar e ler os vídeos",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true,
                    InitialDirectory = _catalogo.PastaDisponivel ? _catalogo.PastaVideos : AppPaths.Root,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                escolhida = dlg.SelectedPath;
            }
            return (object?)await TrocarPastaAsync(escolhida);
        });
        _bridge.Registrar("pasta.usarPadrao", async _ => (object?)await TrocarPastaAsync(AppPaths.VideosPadrao));
        _bridge.Registrar("pasta.abrir", _ =>
        {
            if (!_catalogo.PastaDisponivel) throw new InvalidOperationException("A pasta de vídeos não está disponível.");
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _catalogo.PastaVideos } });
            return (object?)null;
        });
        _bridge.Registrar("video.editar", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            string? Campo(string n) => d?[n]?.GetValue<string>();
            var tags = (d?["tags"] as JsonArray)?.Select(t => t?.GetValue<string>()).ToList();
            var item = await Task.Run(() => _catalogo.Editar(slug, Campo("titulo"), Campo("categoria"), Campo("descricao"), tags));
            return ParaUi(item);
        });
        _bridge.Registrar("video.favoritar", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            var favorito = d?["favorito"]?.GetValue<bool>() ?? throw new ArgumentException("Parâmetro 'favorito' ausente.");
            return (object?)ParaUi(await Task.Run(() => _catalogo.DefinirFavorito(slug, favorito)));
        });
        _bridge.Registrar("videos.alterarTags", async d =>
        {
            static List<string?> Lista(JsonObject? d, string campo) =>
                (d?[campo] as JsonArray)?.Select(n => n?.GetValue<string>()).ToList() ?? [];
            var slugs = Lista(d, "slugs").OfType<string>().ToList();
            var alterados = await Task.Run(() => _catalogo.AlterarTagsEmMassa(slugs, Lista(d, "adicionar"), Lista(d, "remover")));
            return (object?)new { alterados };
        });
        _bridge.Registrar("categoria.renomear", async d =>
        {
            var antiga = Bridge.Texto(d, "antiga");
            var nova = d?["nova"]?.GetValue<string>();
            var alterados = await Task.Run(() => _catalogo.RenomearCategoria(antiga, nova));
            return (object?)new { alterados };
        });
        _bridge.Registrar("video.excluir", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            var chave = _catalogo.Obter(slug) is { } item ? ChaveProgresso(item) : null;
            await _catalogo.ExcluirAsync(slug);
            if (chave is not null) _progresso.Remover(chave);
            return (object?)null;
        });

        // ---- Troca de capa (prévia → confirmar/descartar) ----
        _bridge.Registrar("capa.prepararDeImagem", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            var destino = _catalogo.CaminhoPreviaCapa(slug); // valida o slug antes de abrir o diálogo
            string? imagem = Environment.GetEnvironmentVariable("MINIFLIX_TESTE_IMAGEM"); // gancho de teste
            if (string.IsNullOrEmpty(imagem))
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Escolha a imagem de capa",
                    Filter = "Imagens (*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif",
                    RestoreDirectory = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                imagem = dlg.FileName;
            }
            await Ffmpeg.ConverterImagemParaCapaAsync(imagem, destino, CancellationToken.None);
            return (object?)new { url = _catalogo.UrlPreviaCapa(slug) };
        });
        _bridge.Registrar("capa.prepararDeQuadro", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            var segundos = d?["segundos"]?.GetValue<double>() ?? throw new ArgumentException("Parâmetro 'segundos' ausente.");
            if (!double.IsFinite(segundos) || segundos < 0) throw new ArgumentException("Instante inválido.");
            var ok = await Ffmpeg.GerarCapaNoInstanteAsync(_catalogo.CaminhoVideo(slug), _catalogo.CaminhoPreviaCapa(slug),
                segundos, CancellationToken.None);
            if (!ok) throw new InvalidOperationException("Não foi possível extrair esse quadro do vídeo.");
            return (object?)new { url = _catalogo.UrlPreviaCapa(slug) };
        });
        _bridge.Registrar("capa.confirmar", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            return (object?)ParaUi(await Task.Run(() => _catalogo.ConfirmarCapa(slug)));
        });
        _bridge.Registrar("capa.descartar", d => { _catalogo.DescartarPreviaCapa(Bridge.Texto(d, "slug")); return (object?)null; });

        // ---- Continuar assistindo ----
        _bridge.Registrar("progresso.listar", _ =>
        {
            var mapa = new Dictionary<string, ProgressoVideo>();
            foreach (var item in _catalogo.Itens)
                if (_progresso.Obter(ChaveProgresso(item)) is { } p) mapa[item.Meta.Slug] = p;
            return mapa;
        });
        _bridge.Registrar("progresso.salvar", d =>
        {
            var item = _catalogo.Obter(Bridge.Texto(d, "slug")) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
            var posicao = d?["posicao"]?.GetValue<double>() ?? throw new ArgumentException("Parâmetro 'posicao' ausente.");
            var duracao = d?["duracao"]?.GetValue<double>() ?? throw new ArgumentException("Parâmetro 'duracao' ausente.");
            return _progresso.Registrar(ChaveProgresso(item), posicao, duracao);
        });
        _bridge.Registrar("progresso.reiniciar", d =>
        {
            var item = _catalogo.Obter(Bridge.Texto(d, "slug")) ?? throw new KeyNotFoundException("Vídeo não encontrado.");
            _progresso.Reiniciar(ChaveProgresso(item));
            return (object?)null;
        });
        _bridge.Registrar("video.obter", d =>
        {
            var slug = Bridge.Texto(d, "slug");
            return _catalogo.Obter(slug) is { } item ? ParaUi(item) : throw new KeyNotFoundException("Vídeo não encontrado.");
        });

        // ---- Ferramentas, importação e fila ----
        _bridge.Registrar("sistema.ferramentas", _ => Ferramentas.Situacao());

        _bridge.Registrar("importar.escolherArquivos", async _ =>
        {
            var filtro = string.Join(';', ImportacaoLocal.Extensoes.Select(x => "*" + x));
            using var dlg = new OpenFileDialog
            {
                Title = "Escolha os vídeos para importar",
                Multiselect = true,
                Filter = $"Vídeos ({filtro})|{filtro}|Todos os arquivos (*.*)|*.*",
                RestoreDirectory = true,
            };
            // Gancho para testes automatizados (o diálogo nativo não pode ser clicado por automação).
            var arquivosTeste = Environment.GetEnvironmentVariable("MINIFLIX_TESTE_ARQUIVOS");
            string[] escolhidos;
            if (!string.IsNullOrEmpty(arquivosTeste)) escolhidos = arquivosTeste.Split(';', StringSplitOptions.RemoveEmptyEntries);
            else if (dlg.ShowDialog(this) == DialogResult.OK) escolhidos = dlg.FileNames;
            else return Array.Empty<object>();

            // Analisa cada arquivo (ffprobe, rápido) para o modal mostrar, antes de confirmar,
            // o que será copiado sem perda e o que precisa ser reconvertido.
            using var limite = new SemaphoreSlim(4);
            var itens = await Task.WhenAll(escolhidos.Select(async caminho =>
            {
                var token = Guid.NewGuid().ToString("N");
                object? analise = null;
                await limite.WaitAsync();
                try
                {
                    var info = await Ffmpeg.AnalisarAsync(caminho, CancellationToken.None);
                    if (info.TemVideo)
                        analise = new
                        {
                            tratamento = info.Tratamento() switch
                            {
                                Tratamento.CopiarTudo => "copiar",
                                Tratamento.ConverterSoAudio => "audio",
                                _ => "converter",
                            },
                            codec = info.NomeCodecVideo,
                            largura = info.Largura,
                            altura = info.Altura,
                        };
                }
                catch (Exception ex) { Log.Aviso($"Análise prévia falhou para {caminho}: {ex.Message}"); } // o erro aparece na fila
                finally { limite.Release(); }
                return (token, caminho, analise);
            }));

            return itens.Select(i =>
            {
                _arquivosEscolhidos[i.token] = i.caminho;
                return (object)new
                {
                    i.token,
                    nome = Path.GetFileName(i.caminho),
                    tamanhoBytes = new FileInfo(i.caminho).Length,
                    tituloSugerido = ImportacaoLocal.TituloSugerido(i.caminho),
                    i.analise,
                };
            }).ToArray();
        });

        _bridge.Registrar("fila.adicionarLocal", d =>
        {
            ExigirPastaDisponivel();
            Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
            Ferramentas.Exigir(Ferramentas.Ffprobe, "ffprobe");
            var categoria = d?["categoria"]?.GetValue<string>() ?? "";
            if (categoria.Trim().Length > CatalogService.MaxCategoria) throw new ArgumentException("Categoria muito longa.");
            var itens = d?["itens"] as JsonArray ?? throw new ArgumentException("Nenhum arquivo informado.");
            // Valida as tags antes de enfileirar: o erro aparece no modal, não minutos depois na fila.
            var tags = _catalogo.NormalizarTags((d?["tags"] as JsonArray)?.Select(t => t?.GetValue<string>()) ?? []);
            var reduzirPara1080 = d?["reduzirPara1080"]?.GetValue<bool>() ?? false;
            int n = 0;
            foreach (var no in itens)
            {
                var token = no?["token"]?.GetValue<string>() ?? "";
                if (!_arquivosEscolhidos.Remove(token, out var caminho)) continue; // token desconhecido ou já usado
                var titulo = no?["titulo"]?.GetValue<string>() ?? "";
                if (titulo.Trim().Length > CatalogService.MaxTitulo) throw new ArgumentException("Título muito longo.");
                _fila.Adicionar(_importacao.CriarItem(caminho, titulo, categoria, tags, reduzirPara1080));
                n++;
            }
            return new { adicionados = n };
        });

        _bridge.Registrar("fila.adicionarYoutube", d =>
        {
            ExigirPastaDisponivel();
            Ferramentas.Exigir(Ferramentas.YtDlp, "yt-dlp");
            Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
            var categoria = d?["categoria"]?.GetValue<string>() ?? "";
            if (categoria.Trim().Length > CatalogService.MaxCategoria) throw new ArgumentException("Categoria muito longa.");
            var url = Bridge.Texto(d, "url");
            // Evita adicionar o mesmo link duas vezes enquanto ainda está na fila.
            if (DownloadYoutube.ExtrairId(url) is { } id &&
                _fila.Itens.Any(i => !i.Finalizado && i.Origem == DownloadYoutube.UrlCanonica(id)))
                throw new ArgumentException("Este vídeo já está na fila.");
            var item = _fila.Adicionar(_youtube.CriarItem(url, categoria));
            return new { item.Id };
        });

        _bridge.Registrar("ytdlp.versao", async _ => (object?)new { versao = await DownloadYoutube.VersaoAsync(CancellationToken.None) });
        _bridge.Registrar("ytdlp.atualizar", async _ =>
        {
            if (_fila.Itens.Any(i => i.Tipo == "youtube" && !i.Finalizado))
                throw new InvalidOperationException("Aguarde os downloads da fila terminarem para atualizar o yt-dlp.");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            return (object?)new { mensagem = await DownloadYoutube.AtualizarAsync(cts.Token) };
        });

        _bridge.Registrar("fila.listar", _ => _fila.Itens);
        _bridge.Registrar("fila.cancelar", d => { _fila.Cancelar(Bridge.Texto(d, "id")); return (object?)null; });
        _bridge.Registrar("fila.remover", d => { _fila.Remover(Bridge.Texto(d, "id")); return (object?)null; });
        _bridge.Registrar("fila.limparFinalizados", _ => { _fila.LimparFinalizados(); return (object?)null; });

        _bridge.Registrar("app.sair", async _ =>
        {
            _saindo = true;
            _fila.CancelarTodos();
            await _fila.AguardarOciosaAsync(TimeSpan.FromSeconds(8));
            RunOnUi(Close);
            return (object?)null;
        });

        // Diagnóstico (página ui/diagnostico.html)
        _bridge.Registrar("teste.listarVideos", _ => ListarVideosTeste());
        _bridge.Registrar("teste.arquivoTravado", async d => await ArquivoTravadoAsync(Bridge.Texto(d, "caminho")));
        _bridge.Registrar("teste.relatorio", d =>
        {
            var arquivo = Path.Combine(AppPaths.Logs, "autoteste.json");
            File.WriteAllText(arquivo, d?.ToJsonString(JsonStore.Opcoes) ?? "{}");
            RunOnUi(Close);
            return new { arquivo };
        });
    }

    // ---------- Pasta dos vídeos ----------

    private object InfoPasta() => new
    {
        caminho = _catalogo.PastaVideos,
        padrao = PastaVideos.MesmoCaminho(_catalogo.PastaVideos, AppPaths.VideosPadrao),
        disponivel = _catalogo.PastaDisponivel,
    };

    private void ExigirPastaDisponivel()
    {
        if (!_catalogo.PastaDisponivel)
            throw new InvalidOperationException(
                $"A pasta de vídeos não foi encontrada ({_catalogo.PastaVideos}). Conecte o disco ou escolha outra pasta na administração.");
    }

    /// <summary>Valida a pasta, grava na configuração, aponta o host virtual para ela e relê o catálogo.</summary>
    private async Task<object> TrocarPastaAsync(string pasta)
    {
        if (_fila.Ocupada)
            throw new InvalidOperationException("Aguarde a fila de importação/download terminar para trocar a pasta dos vídeos.");
        pasta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pasta));
        if (PastaVideos.Validar(pasta, AppPaths.Root, AppPaths.VideosPadrao) is { } erro)
            throw new ArgumentException(erro);

        var padrao = PastaVideos.MesmoCaminho(pasta, AppPaths.VideosPadrao);
        _config.Alterar(c => c.PastaVideos = PastaVideos.ParaConfig(pasta, AppPaths.Root, AppPaths.VideosPadrao));
        await Task.Run(() => _catalogo.TrocarPasta(pasta, criarSeFaltar: padrao));
        var recarregarPagina = MapearPastaVideos();
        _bridge.Emitir("catalogo.atualizado"); // mesmo que a varredura não mude nada (duas pastas vazias)
        return new { pasta = InfoPasta(), videos = _catalogo.Itens.Count, recarregarPagina };
    }

    private string? _pastaMapeada;

    /// <summary>
    /// Aponta o host virtual dos vídeos para a pasta atual (se ela existir). Chamar na thread da UI.
    /// Retorna true se o mapeamento mudou: o WebView2 só aplica a mudança na próxima navegação,
    /// então a página precisa ser recarregada para os vídeos da pasta nova abrirem.
    /// </summary>
    private bool MapearPastaVideos()
    {
        var core = _webView.CoreWebView2;
        if (core is null || !_catalogo.PastaDisponivel) return false; // pasta sumiu: mantém o mapeamento antigo (inofensivo)
        if (_pastaMapeada is not null && PastaVideos.MesmoCaminho(_pastaMapeada, _catalogo.PastaVideos)) return false;
        if (_pastaMapeada is not null) core.ClearVirtualHostNameToFolderMapping(AppPaths.HostVideos);
        // Allow (e não DenyCors): a página do app é outra origem e o diagnóstico lê trechos via fetch.
        core.SetVirtualHostNameToFolderMapping(AppPaths.HostVideos, _catalogo.PastaVideos, CoreWebView2HostResourceAccessKind.Allow);
        _pastaMapeada = _catalogo.PastaVideos;
        return true;
    }

    private static string ChaveProgresso(VideoItem i) => ProgressService.Chave(i.Meta.Id, i.Meta.Slug);

    private static object ParaUi(VideoItem i) => new
    {
        i.Meta.Slug,
        i.Meta.Titulo,
        i.Meta.Descricao,
        i.Meta.Categoria,
        i.Meta.Tags,
        i.Meta.Favorito,
        i.Meta.Origem,
        i.Meta.UrlOrigem,
        i.Meta.DuracaoSegundos,
        i.Meta.DataPublicacao,
        i.Meta.DataAdicionado,
        i.VideoUrl,
        i.CapaUrl,
        i.TamanhoBytes,
    };

    // ---------- WebView2 ----------

    private async Task InitWebViewAsync()
    {
        // 0) O build move o WebView2Loader.dll para /bin (a raiz fica só com o MiniFlix.exe).
        //    Precisa ser definido antes de qualquer outra chamada ao WebView2.
        if (File.Exists(Path.Combine(AppPaths.Bin, "WebView2Loader.dll")))
            CoreWebView2Environment.SetLoaderDllFolderPath(AppPaths.Bin);

        // 1) Runtime: Fixed Version em /bin/webview2, senão o instalado no sistema.
        string? browserFolder = File.Exists(Path.Combine(AppPaths.WebView2Fixed, "msedgewebview2.exe"))
            ? AppPaths.WebView2Fixed
            : null;

        try
        {
            _runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder);
            _runtimeSource = browserFolder is null ? "sistema (instalado no Windows)" : "fixo (/bin/webview2)";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Só acontece na versão "leve" (ou se bin\webview2 foi apagado): o Windows não tem o WebView2.
            Log.Erro("WebView2 não encontrado (nem em bin\\webview2, nem instalado no Windows).");
            MessageBox.Show(this,
                "Este computador não tem o componente WebView2, que o MiniFlix usa para mostrar a interface.\n\n" +
                "Você pode:\n" +
                "  • baixar a versão COMPLETA do MiniFlix, que já inclui o WebView2; ou\n" +
                "  • instalar o \"Microsoft Edge WebView2 Runtime\" pelo site da Microsoft.",
                "MiniFlix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }
        Log.Info($"Iniciando. WebView2 {_runtimeVersion} ({_runtimeSource}). Raiz: {AppPaths.Root}. Vídeos: {_catalogo.PastaVideos}");

        // 2) Dados do WebView2 dentro da pasta do app (/data/webview), não em %LocalAppData%.
        // App local de mídia: permite autoplay com som ao abrir um vídeo.
        var opcoes = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(browserFolder, AppPaths.WebViewData, opcoes);
        await _webView.EnsureCoreWebView2Async(env);
        var core = _webView.CoreWebView2;
        core.Profile.PreferredColorScheme = _config.Atual.Tema == "claro"
            ? CoreWebView2PreferredColorScheme.Light : CoreWebView2PreferredColorScheme.Dark;
        // Tema disponível antes do primeiro paint (evita "piscar"). Valor já validado pelo SettingsService.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.__miniflix = {JsonSerializer.Serialize(new { tema = _config.Atual.Tema })};");

        // 3) Hosts virtuais: um para o app (UI) e outro para a pasta dos vídeos, que pode estar em
        //    qualquer lugar (outra unidade, HD externo). Se ela não existir agora, fica sem mapeamento.
        core.SetVirtualHostNameToFolderMapping(AppPaths.HostName, AppPaths.Root, CoreWebView2HostResourceAccessKind.DenyCors);
        MapearPastaVideos();

#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        // 4) Bloqueia navegação para fora do app; links externos vão para o navegador padrão.
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(AppPaths.Origin, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.WebMessageReceived += async (_, e) => await _bridge.ProcessarAsync(e.WebMessageAsJson);
        core.ProcessFailed += (_, e) => AoFalharProcesso(core, e);

        // Tela cheia de verdade: sem isso, o elemento só ocupa a área do WebView dentro da janela.
        FormBorderStyle bordaAnterior = FormBorderStyle;
        FormWindowState estadoAnterior = WindowState;
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (core.ContainsFullScreenElement)
            {
                bordaAnterior = FormBorderStyle;
                estadoAnterior = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;     // necessário para o Maximized cobrir a barra de tarefas
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = bordaAnterior;
                WindowState = estadoAnterior;
            }
        };

        _catalogo.Iniciar();

        // Modos de linha de comando:
        //   --autoteste              roda a página de diagnóstico e grava data/logs/autoteste.json
        //   --captura <rota> <png>   abre a rota (#/...), salva um PNG da tela e fecha
        string pagina = "ui/index.html";
        if (_args.Contains("--autoteste")) pagina = "ui/diagnostico.html?autoteste=1";
        var iCap = Array.IndexOf(_args, "--captura");
        if (iCap >= 0 && iCap + 2 < _args.Length)
        {
            pagina += _args[iCap + 1];
            CapturarAoCarregar(core, _args[iCap + 2]);
        }
        core.Navigate(AppPaths.Origin + pagina);
    }

    private void CapturarAoCarregar(CoreWebView2 core, string arquivoPng)
    {
        core.NavigationCompleted += async (_, _) =>
        {
            await Task.Delay(1500); // tempo para render + carregamento das capas
            var destino = Path.GetFullPath(arquivoPng);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            await using (var fs = File.Create(destino))
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            Close();
        };
    }

    private void FalhaAoIniciar(Exception ex)
    {
        Log.Erro("Falha ao iniciar a interface (WebView2)", ex);
        MessageBox.Show(this,
            "Não foi possível abrir a interface do MiniFlix.\n\n" +
            "Causas mais comuns:\n" +
            "  • o arquivo .zip não foi totalmente extraído (extraia tudo de novo);\n" +
            "  • um antivírus bloqueou arquivos da pasta \"bin\";\n" +
            "  • a pasta do MiniFlix está incompleta ou foi alterada.\n\n" +
            $"Detalhe técnico: {ex.Message}\n" +
            "(registrado em data\\logs\\miniflix.log)",
            "MiniFlix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    private DateTime _janelaRecargas = DateTime.MinValue;
    private int _recargas;

    /// <summary>
    /// Um processo do WebView2 caiu durante o uso. A parte que desenha a página pode ser
    /// recarregada (até 3 vezes por minuto, para não entrar em laço); se o navegador inteiro
    /// caiu, não há como continuar e o app explica e fecha.
    /// </summary>
    private void AoFalharProcesso(CoreWebView2 core, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Aviso($"WebView2: processo falhou ({e.ProcessFailedKind}, motivo {e.Reason}, código {e.ExitCode}).");
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                MessageBox.Show(this,
                    "A interface do MiniFlix parou inesperadamente e precisa ser reaberta.\n\n" +
                    "Abra o MiniFlix novamente. Se acontecer de novo, veja data\\logs\\miniflix.log.",
                    "MiniFlix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _saindo = true; // não há interface para perguntar sobre a fila
                Close();
                break;

            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                if (DateTime.Now - _janelaRecargas > TimeSpan.FromMinutes(1)) { _janelaRecargas = DateTime.Now; _recargas = 0; }
                if (++_recargas <= 3)
                {
                    Log.Info("Recarregando a interface após falha do processo de renderização.");
                    core.Reload();
                }
                else
                {
                    FalhaAoIniciar(new InvalidOperationException("A interface falhou várias vezes seguidas."));
                }
                break;
            // Demais processos (GPU, utilitários) o próprio WebView2 reinicia sozinho.
        }
    }

    private static void OpenExternal(string uri)
    {
        if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
    }

    // ---------- Diagnóstico ----------

    private object SistemaInfo()
    {
        var drive = new DriveInfo(Path.GetPathRoot(AppPaths.Root)!);
        return new
        {
            versaoApp = Application.ProductVersion,
            dotnet = Environment.Version.ToString(),
            webview2Versao = _runtimeVersion,
            webview2Origem = _runtimeSource,
            raiz = AppPaths.Root,
            pastaVideos = _catalogo.PastaVideos,
            pastaDadosWebView = AppPaths.WebViewData,
            unidade = drive.Name,
            unidadeTipo = drive.DriveType.ToString(),
            sistemaArquivos = drive.DriveFormat,
            espacoLivreBytes = drive.AvailableFreeSpace,
        };
    }

    private object ListarVideosTeste() =>
        _catalogo.Itens
            .Select(i => new FileInfo(_catalogo.CaminhoVideo(i.Meta.Slug)))
            .OrderByDescending(f => f.Length)
            .Select(f => new
            {
                nome = Path.GetRelativePath(_catalogo.PastaVideos, f.FullName),
                caminho = Path.GetRelativePath(_catalogo.PastaVideos, f.FullName).Replace('\\', '/'),
                url = AppPaths.ToVideoUrl(_catalogo.PastaVideos, f.FullName),
                tamanhoBytes = f.Length
            })
            .ToList();

    /// <summary>Verifica se o arquivo pode ser aberto com acesso exclusivo (ou seja, se poderia ser excluído).</summary>
    private async Task<object?> ArquivoTravadoAsync(string caminho)
    {
        var full = AppPaths.ResolveInside(_catalogo.PastaVideos, caminho)
                   ?? throw new InvalidOperationException("Caminho inválido.");
        if (!File.Exists(full)) throw new FileNotFoundException("Arquivo não encontrado.");

        var sw = Stopwatch.StartNew();
        for (int tentativa = 1; tentativa <= 10; tentativa++)
        {
            try
            {
                using var fs = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return new { travado = false, tentativas = tentativa, ms = sw.ElapsedMilliseconds };
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
        }
        return new { travado = true, tentativas = 10, ms = sw.ElapsedMilliseconds };
    }
}
