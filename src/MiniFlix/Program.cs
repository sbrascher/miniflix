namespace MiniFlix;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppPaths.EnsureCreated();
        Services.Log.Arquivo = Path.Combine(AppPaths.Logs, "miniflix.log");
        Application.ThreadException += (_, e) => Services.Log.Erro("Exceção não tratada (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.Log.Erro("Exceção não tratada", e.ExceptionObject as Exception);
        Application.Run(new MainForm());
    }
}
