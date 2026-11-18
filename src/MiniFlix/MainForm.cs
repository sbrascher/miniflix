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
        _catalogo = new CatalogService(AppPaths.Videos, Path.Combine(AppPaths.Data, "catalog-cache.json"), AppPaths.ToUrl);
        _catalogo.Alterado += () => _bridge.Emitir("catalogo.atualizado");
        Ferramentas.PastaBin = AppPaths.Bin;
        _importacao = new ImportacaoLocal(_catalogo, AppPaths.Videos);
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
        Load += async (_, _) => await InitWebViewAsync();
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
            var novo = d.Deserialize<Settings>(JsonStore.Opcoes) ?? throw new ArgumentException("Configuração vazia.");
            var salvo = _config.Salvar(novo);
            RunOnUi(() => BackColor = salvo.Tema == "claro" ? Color.FromArgb(250, 250, 250) : Color.FromArgb(12, 12, 14));
            return salvo;
        });

        _bridge.Registrar("catalogo.listar", _ => new
        {
            videos = _catalogo.Itens.Select(ParaUi),
            categorias = _catalogo.Categorias(),
        });
        _bridge.Registrar("catalogo.recarregar", async _ =>
        {
            await Task.Run(_catalogo.Varrer);
            return (object?)null;
        });
        _bridge.Registrar("video.editar", async d =>
        {
            var slug = Bridge.Texto(d, "slug");
            string? Campo(string n) => d?[n]?.GetValue<string>();
            var item = await Task.Run(() => _catalogo.Editar(slug, Campo("titulo"), Campo("categoria"), Campo("descricao")));
            return ParaUi(item);
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

        _bridge.Registrar("importar.escolherArquivos", _ =>
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

            return escolhidos.Select(caminho =>
            {
                var token = Guid.NewGuid().ToString("N");
                _arquivosEscolhidos[token] = caminho;
                return (object)new
                {
                    token,
                    nome = Path.GetFileName(caminho),
                    tamanhoBytes = new FileInfo(caminho).Length,
                    tituloSugerido = ImportacaoLocal.TituloSugerido(caminho),
                };
            }).ToArray();
        });

        _bridge.Registrar("fila.adicionarLocal", d =>
        {
            Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
            Ferramentas.Exigir(Ferramentas.Ffprobe, "ffprobe");
            var categoria = d?["categoria"]?.GetValue<string>() ?? "";
            if (categoria.Trim().Length > CatalogService.MaxCategoria) throw new ArgumentException("Categoria muito longa.");
            var itens = d?["itens"] as JsonArray ?? throw new ArgumentException("Nenhum arquivo informado.");
            int n = 0;
            foreach (var no in itens)
            {
                var token = no?["token"]?.GetValue<string>() ?? "";
                if (!_arquivosEscolhidos.Remove(token, out var caminho)) continue; // token desconhecido ou já usado
                var titulo = no?["titulo"]?.GetValue<string>() ?? "";
                if (titulo.Trim().Length > CatalogService.MaxTitulo) throw new ArgumentException("Título muito longo.");
                _fila.Adicionar(_importacao.CriarItem(caminho, titulo, categoria));
                n++;
            }
            return new { adicionados = n };
        });

        _bridge.Registrar("fila.adicionarYoutube", d =>
        {
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

    private static string ChaveProgresso(VideoItem i) => ProgressService.Chave(i.Meta.Id, i.Meta.Slug);

    private static object ParaUi(VideoItem i) => new
    {
        i.Meta.Slug,
        i.Meta.Titulo,
        i.Meta.Descricao,
        i.Meta.Categoria,
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
            MessageBox.Show(this,
                "Não foi encontrado o componente WebView2.\n\n" +
                "Coloque o WebView2 Fixed Version Runtime na pasta:\n" + AppPaths.WebView2Fixed,
                "MiniFlix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        Log.Info($"Iniciando. WebView2 {_runtimeVersion} ({_runtimeSource}). Raiz: {AppPaths.Root}");

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

        // 3) Host virtual único -> raiz do app (UI e vídeos na mesma origem, sem CORS).
        core.SetVirtualHostNameToFolderMapping(AppPaths.HostName, AppPaths.Root, CoreWebView2HostResourceAccessKind.DenyCors);

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
            pastaDadosWebView = AppPaths.WebViewData,
            unidade = drive.Name,
            unidadeTipo = drive.DriveType.ToString(),
            sistemaArquivos = drive.DriveFormat,
            espacoLivreBytes = drive.AvailableFreeSpace,
        };
    }

    private static object ListarVideosTeste() =>
        Directory.EnumerateFiles(AppPaths.Videos, "*.mp4", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.Length)
            .Select(f => new
            {
                nome = Path.GetRelativePath(AppPaths.Videos, f.FullName),
                caminho = Path.GetRelativePath(AppPaths.Root, f.FullName).Replace('\\', '/'),
                url = AppPaths.ToUrl(f.FullName),
                tamanhoBytes = f.Length
            })
            .ToList();

    /// <summary>Verifica se o arquivo pode ser aberto com acesso exclusivo (ou seja, se poderia ser excluído).</summary>
    private static async Task<object?> ArquivoTravadoAsync(string caminho)
    {
        var full = AppPaths.ResolveInside(AppPaths.Videos, caminho)
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
