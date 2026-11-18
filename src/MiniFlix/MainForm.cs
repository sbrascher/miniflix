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

        _config = new SettingsService(Path.Combine(AppPaths.Data, "settings.json"));
        BackColor = _config.Atual.Tema == "claro" ? Color.FromArgb(250, 250, 250) : Color.FromArgb(12, 12, 14);

        _bridge = new Bridge(json => RunOnUi(() => _webView.CoreWebView2?.PostWebMessageAsJson(json)));
        _catalogo = new CatalogService(AppPaths.Videos, Path.Combine(AppPaths.Data, "catalog-cache.json"), AppPaths.ToUrl);
        _catalogo.Alterado += () => _bridge.Emitir("catalogo.atualizado");
        RegistrarComandos();

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
            await _catalogo.ExcluirAsync(Bridge.Texto(d, "slug"));
            return (object?)null;
        });
        _bridge.Registrar("video.obter", d =>
        {
            var slug = Bridge.Texto(d, "slug");
            return _catalogo.Obter(slug) is { } item ? ParaUi(item) : throw new KeyNotFoundException("Vídeo não encontrado.");
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

    private static object ParaUi(VideoItem i) => new
    {
        i.Meta.Slug,
        i.Meta.Titulo,
        i.Meta.Descricao,
        i.Meta.Categoria,
        i.Meta.Origem,
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
