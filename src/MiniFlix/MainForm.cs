using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniFlix;

internal sealed class MainForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private string _runtimeVersion = "?";
    private string _runtimeSource = "?";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public MainForm()
    {
        Text = "MiniFlix — Protótipo";
        Width = 1280;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 20);
        Controls.Add(_webView);
        Load += async (_, _) => await InitWebViewAsync();
    }

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

        // 2) Dados do WebView2 ficam dentro da pasta do app (/data/webview), não em %LocalAppData%.
        var env = await CoreWebView2Environment.CreateAsync(browserFolder, AppPaths.WebViewData);
        await _webView.EnsureCoreWebView2Async(env);
        var core = _webView.CoreWebView2;

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

        core.WebMessageReceived += OnWebMessage;
        var autoteste = Environment.GetCommandLineArgs().Contains("--autoteste");
        core.Navigate(AppPaths.Origin + "ui/index.html" + (autoteste ? "?autoteste=1" : ""));
    }

    private static void OpenExternal(string uri)
    {
        if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
    }

    // ---------- Ponte JS <-> C# ----------

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string tipo = "?";
        string? id = null;
        try
        {
            var msg = JsonNode.Parse(e.WebMessageAsJson)?.AsObject()
                      ?? throw new InvalidOperationException("Mensagem vazia.");
            tipo = msg["tipo"]?.GetValue<string>() ?? throw new InvalidOperationException("Campo 'tipo' ausente.");
            id = msg["idRequisicao"]?.GetValue<string>();
            var dados = msg["dados"] as JsonObject;

            object? resultado = tipo switch
            {
                "ping" => new { pong = DateTime.Now.ToString("O") },
                "sistema.info" => SistemaInfo(),
                "teste.listarVideos" => ListarVideosTeste(),
                "teste.arquivoTravado" => await ArquivoTravadoAsync(dados?["caminho"]?.GetValue<string>()),
                "teste.relatorio" => SalvarRelatorio(dados),
                _ => throw new InvalidOperationException($"Comando desconhecido: {tipo}")
            };
            Responder(tipo, id, resultado, null);
        }
        catch (Exception ex)
        {
            Responder(tipo, id, null, ex.Message);
        }
    }

    private void Responder(string tipo, string? id, object? dados, string? erro)
    {
        var json = JsonSerializer.Serialize(new { tipo, idRequisicao = id, dados, erro }, JsonOpts);
        _webView.CoreWebView2?.PostWebMessageAsJson(json);
    }

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

    private object SalvarRelatorio(JsonObject? dados)
    {
        var arquivo = Path.Combine(AppPaths.Logs, "autoteste.json");
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(arquivo, dados?.ToJsonString(opts) ?? "{}");
        BeginInvoke(Close);
        return new { arquivo };
    }

    private static object ListarVideosTeste()
    {
        return Directory.EnumerateFiles(AppPaths.Videos, "*.mp4", SearchOption.AllDirectories)
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
    }

    /// <summary>
    /// Verifica se o arquivo pode ser aberto com acesso exclusivo (ou seja, se poderia ser excluído).
    /// Tenta algumas vezes porque o navegador pode demorar um pouco para soltar o handle.
    /// </summary>
    private static async Task<object> ArquivoTravadoAsync(string? caminho)
    {
        var full = AppPaths.ResolveInside(AppPaths.Videos, caminho ?? "")
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
