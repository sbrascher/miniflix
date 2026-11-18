namespace MiniFlix;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppPaths.EnsureCreated();
        Application.Run(new MainForm());
    }
}
