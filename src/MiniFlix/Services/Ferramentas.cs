namespace MiniFlix.Services;

/// <summary>
/// Localiza os executáveis externos. Ordem: pasta /bin do app (modo portátil) → PATH do sistema (desenvolvimento).
/// </summary>
public static class Ferramentas
{
    public static string PastaBin { get; set; } = Path.Combine(AppContext.BaseDirectory, "bin");

    public static string? Ffmpeg => Resolver("ffmpeg");
    public static string? Ffprobe => Resolver("ffprobe");
    public static string? YtDlp => Resolver("yt-dlp");
    public static string? Deno => Resolver("deno");

    public static string? Resolver(string nome)
    {
        var local = Path.Combine(PastaBin, nome + ".exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidato = Path.Combine(dir.Trim('"'), nome + ".exe");
                if (File.Exists(candidato)) return candidato;
            }
            catch (ArgumentException) { /* entrada inválida no PATH */ }
        }
        return null;
    }

    public static object Situacao() => new
    {
        ffmpeg = Descrever(Ffmpeg),
        ffprobe = Descrever(Ffprobe),
        ytDlp = Descrever(YtDlp),
        deno = Descrever(Deno),
    };

    private static object? Descrever(string? caminho) => caminho is null ? null : new
    {
        caminho,
        origem = caminho.StartsWith(PastaBin, StringComparison.OrdinalIgnoreCase) ? "bin" : "PATH",
    };

    public static string Exigir(string? caminho, string nome) =>
        caminho ?? throw new InvalidOperationException(
            $"{nome} não encontrado. Coloque {nome}.exe na pasta bin do MiniFlix.");
}
