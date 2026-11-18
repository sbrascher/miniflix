namespace MiniFlix;

/// <summary>Todos os caminhos são relativos à pasta do executável (portátil).</summary>
internal static class AppPaths
{
    public const string HostName = "app.miniflix.example";
    public const string Origin = "https://" + HostName + "/";

    public static string Root { get; } = Path.GetFullPath(AppContext.BaseDirectory);
    public static string Ui => Path.Combine(Root, "ui");
    public static string Bin => Path.Combine(Root, "bin");
    public static string WebView2Fixed => Path.Combine(Bin, "webview2");
    public static string Videos => Path.Combine(Root, "videos");
    public static string Data => Path.Combine(Root, "data");
    public static string WebViewData => Path.Combine(Data, "webview");
    public static string Logs => Path.Combine(Data, "logs");

    public static void EnsureCreated()
    {
        foreach (var dir in new[] { Bin, Videos, Data, WebViewData, Logs })
            Directory.CreateDirectory(dir);
    }

    /// <summary>Converte um caminho absoluto dentro de Root em URL do host virtual.</summary>
    public static string ToUrl(string fullPath)
    {
        var rel = Path.GetRelativePath(Root, fullPath);
        var segments = rel.Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString);
        return Origin + string.Join('/', segments);
    }

    /// <summary>
    /// Resolve um caminho relativo vindo da UI garantindo que fique dentro de <paramref name="baseDir"/>
    /// (bloqueia path traversal). Retorna null se for inválido.
    /// </summary>
    public static string? ResolveInside(string baseDir, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var baseFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
