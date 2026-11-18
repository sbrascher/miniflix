using MiniFlix.Services;

namespace MiniFlix.Tests;

public class CompatibilidadeTests
{
    [Theory]
    [InlineData("10.0.19045", true)]   // Windows 10 22H2
    [InlineData("10.0.17763", true)]   // Windows 10 1809
    [InlineData("10.0.26100", true)]   // Windows 11 24H2
    public void Windows_10_e_11_de_64_bits_sao_compativeis(string versao, bool x64) =>
        Assert.Null(Compatibilidade.VerificarSistema(Version.Parse(versao), x64));

    [Theory]
    [InlineData("6.3.9600", "Windows 8.1")]
    [InlineData("6.2.9200", "Windows 8")]
    [InlineData("6.1.7601", "Windows 7")]
    [InlineData("6.0.6002", "Windows Vista")]
    [InlineData("5.1.2600", "Windows XP")]
    public void Windows_antigo_recebe_mensagem_com_o_nome_do_sistema(string versao, string nome)
    {
        var msg = Compatibilidade.VerificarSistema(Version.Parse(versao), true);
        Assert.NotNull(msg);
        Assert.Contains("Windows 10 ou 11", msg);
        Assert.Contains(nome, msg);
    }

    [Fact]
    public void Windows_de_32_bits_recebe_mensagem_propria()
    {
        var msg = Compatibilidade.VerificarSistema(Version.Parse("10.0.19045"), false);
        Assert.NotNull(msg);
        Assert.Contains("32 bits", msg);
    }

    [Theory]
    [InlineData("10.0.19045", "Windows 10")]
    [InlineData("10.0.22631", "Windows 11")]
    public void Nome_do_windows_10_e_11(string versao, string nome) =>
        Assert.Equal(nome, Compatibilidade.NomeDoWindows(Version.Parse(versao)));
}
