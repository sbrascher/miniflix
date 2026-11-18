namespace MiniFlix.Services;

/// <summary>
/// Verifica se o Windows é compatível ANTES de abrir a interface: .NET 10, WebView2 e Deno exigem
/// Windows 10 ou 11 de 64 bits. Em sistemas antigos, a pessoa recebe uma explicação clara em vez
/// de um erro técnico (ou de nada acontecer).
/// </summary>
public static class Compatibilidade
{
    /// <summary>Devolve null se o sistema é compatível, ou a mensagem a mostrar ao usuário.</summary>
    public static string? VerificarSistema(Version versao, bool sistema64Bits)
    {
        if (versao.Major < 10)
        {
            return $"O MiniFlix precisa do Windows 10 ou 11 (64 bits).\n\n" +
                   $"Este computador usa o {NomeDoWindows(versao)}, que não é compatível: " +
                   "a Microsoft não oferece mais para ele os componentes que o MiniFlix usa.";
        }
        if (!sistema64Bits)
        {
            return "O MiniFlix precisa do Windows 10 ou 11 de 64 bits.\n\n" +
                   "Este computador usa uma versão de 32 bits do Windows.";
        }
        return null;
    }

    /// <summary>Nome amigável a partir do número interno de versão (6.1 = Windows 7, etc.).</summary>
    public static string NomeDoWindows(Version v) => (v.Major, v.Minor) switch
    {
        (6, 3) => "Windows 8.1",
        (6, 2) => "Windows 8",
        (6, 1) => "Windows 7",
        (6, 0) => "Windows Vista",
        (5, _) => "Windows XP",
        (10, _) when v.Build >= 22000 => "Windows 11",
        (10, _) => "Windows 10",
        _ => $"Windows {v.Major}.{v.Minor}",
    };

    /// <summary>
    /// Versão real do Windows. A variável MINIFLIX_TESTE_VERSAO_WINDOWS permite simular um sistema
    /// antigo nos testes (ex.: "6.3.9600" = Windows 8.1).
    /// </summary>
    public static Version VersaoAtual()
    {
        var simulada = Environment.GetEnvironmentVariable("MINIFLIX_TESTE_VERSAO_WINDOWS");
        return Version.TryParse(simulada, out var v) ? v : Environment.OSVersion.Version;
    }
}
