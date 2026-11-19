namespace MiniFlix;

/// <summary>Todos os caminhos são relativos à pasta do executável (portátil).</summary>
internal static class AppPaths
{
    public const string HostName = "app.miniflix.example";
    public const string Origin = "https://" + HostName + "/";
    /// <summary>Host virtual separado para a pasta dos vídeos (que pode ficar em qualquer lugar do disco).</summary>
    public const string HostVideos = "videos.miniflix.example";
    public const string OrigemVideos = "https://" + HostVideos + "/";

    public static string Root { get; } = Path.GetFullPath(AppContext.BaseDirectory);
    public static string Ui => Path.Combine(Root, "ui");
    public static string Bin => Path.Combine(Root, "bin");
    public static string WebView2Fixed => Path.Combine(Bin, "webview2");
    /// <summary>Pasta de vídeos padrão (quando o usuário não escolheu outra).</summary>
    public static string VideosPadrao => Path.Combine(Root, "videos");
    public static string Data => Path.Combine(Root, "data");
    public static string WebViewData => Path.Combine(Data, "webview");
    public static string Logs => Path.Combine(Data, "logs");

    public static void EnsureCreated()
    {
        foreach (var dir in new[] { Bin, VideosPadrao, Data, WebViewData, Logs })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Converte um caminho absoluto dentro de Root em URL do host virtual.</summary>
    public static string ToUrl(string fullPath) => Montar(Origin, Root, fullPath);

    /// <summary>Converte um caminho dentro da pasta de vídeos em URL do host dos vídeos.</summary>
    public static string ToVideoUrl(string pastaVideos, string fullPath) => Montar(OrigemVideos, pastaVideos, fullPath);

    private static string Montar(string origem, string baseDir, string fullPath)
    {
        var rel = Path.GetRelativePath(baseDir, fullPath);
        var segments = rel.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString);
        return origem + string.Join('/', segments);
    }

    /// <summary>
    /// Resolve um caminho relativo vindo da UI garantindo que fique dentro de <paramref name="baseDir"/>
    /// (bloqueia path traversal). Retorna null se for inválido.
    /// </summary>
    public static string? ResolveInside(string baseDir, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(baseDir, relative.Replace('/', Path.DirectorySeparatorChar)));
        var baseFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
