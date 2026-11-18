using MiniFlix.Services;

namespace MiniFlix;

internal static class Program
{
    // Sem isto, se um componente estiver corrompido (ex.: bloqueado por antivírus), o próprio
    // Windows mostra uma caixa técnica ("não pode ser iniciado…") antes do aviso do MiniFlix.
    // Com SEM_FAILCRITICALERRORS o erro chega ao app, que explica em português.
    private const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOOPENFILEERRORBOX = 0x8000;
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint modo);

    [STAThread]
    static void Main()
    {
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
        ApplicationConfiguration.Initialize();

        // Antes de tudo (inclusive de criar pastas): Windows antigo ou de 32 bits recebe uma
        // explicação clara e o app fecha sem deixar nada para trás.
        var incompatibilidade = Compatibilidade.VerificarSistema(Compatibilidade.VersaoAtual(), Environment.Is64BitOperatingSystem);
        if (incompatibilidade is not null)
        {
            MessageBox.Show(incompatibilidade, "MiniFlix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppPaths.EnsureCreated();
        Log.Arquivo = Path.Combine(AppPaths.Logs, "miniflix.log");
        Application.ThreadException += (_, e) => Log.Erro("Exceção não tratada (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Erro("Exceção não tratada", e.ExceptionObject as Exception);
        Application.Run(new MainForm());
    }
}
