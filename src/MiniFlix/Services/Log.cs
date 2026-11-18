namespace MiniFlix.Services;

/// <summary>Log simples em arquivo texto (data/logs/miniflix.log). Nunca lança exceção.</summary>
public static class Log
{
    private static readonly object Trava = new();
    public static string? Arquivo { get; set; }

    public static void Info(string msg) => Escrever("INFO", msg);
    public static void Aviso(string msg) => Escrever("AVISO", msg);
    public static void Erro(string msg, Exception? ex = null) =>
        Escrever("ERRO", ex is null ? msg : $"{msg} :: {ex.GetType().Name}: {ex.Message}");

    private static void Escrever(string nivel, string msg)
    {
        if (Arquivo is null) return;
        try
        {
            lock (Trava)
                File.AppendAllText(Arquivo, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{nivel}] {msg}{Environment.NewLine}");
        }
        catch { /* log nunca derruba o app */ }
    }
}
