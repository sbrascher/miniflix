using MiniFlix.Services;

namespace MiniFlix.Tests;

public sealed class ProgressServiceTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"), "progress.json");
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_arquivo)!, true); } catch { } }

    private ProgressService Novo(int atrasoMs = 50) => new(_arquivo, TimeSpan.FromMilliseconds(atrasoMs));

    [Fact]
    public void Registra_posicao_no_meio_do_video()
    {
        using var s = Novo();
        var p = s.Registrar("yt:abc", 123.456, 600);
        Assert.Equal(123.5, p.Posicao);
        Assert.Equal(600, p.Duracao);
        Assert.False(p.Assistido);
    }

    [Fact]
    public void Perto_do_fim_marca_assistido_e_volta_ao_inicio()
    {
        using var s = Novo();
        var p = s.Registrar("v", 580, 600); // 96,7%
        Assert.True(p.Assistido);
        Assert.Equal(0, p.Posicao);
    }

    [Fact]
    public void Rever_um_video_assistido_guarda_nova_posicao_e_mantem_selo()
    {
        using var s = Novo();
        s.Registrar("v", 600, 600);
        var p = s.Registrar("v", 200, 600);
        Assert.True(p.Assistido);
        Assert.Equal(200, p.Posicao);
    }

    [Fact]
    public void Reiniciar_zera_posicao()
    {
        using var s = Novo();
        s.Registrar("v", 200, 600);
        s.Reiniciar("v");
        Assert.Equal(0, s.Obter("v")!.Posicao);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(-1, 100)]
    [InlineData(double.NaN, 100)]
    [InlineData(10, double.PositiveInfinity)]
    public void Rejeita_valores_invalidos(double posicao, double duracao)
    {
        using var s = Novo();
        Assert.Throws<ArgumentException>(() => s.Registrar("v", posicao, duracao));
    }

    [Fact]
    public void Posicao_maior_que_duracao_e_limitada()
    {
        using var s = Novo();
        Assert.True(s.Registrar("v", 999, 100).Assistido);
    }

    [Fact]
    public async Task Gravacao_e_agrupada_e_persiste_entre_instancias()
    {
        using (var s = Novo(atrasoMs: 300))
        {
            for (int i = 1; i <= 20; i++) s.Registrar("yt:abc", i * 10, 600);
            Assert.False(File.Exists(_arquivo), "não deveria gravar a cada chamada");
            await Task.Delay(800);
            Assert.True(File.Exists(_arquivo));
        }
        using var outra = Novo();
        Assert.Equal(200, outra.Obter("yt:abc")!.Posicao);
    }

    [Fact]
    public void Dispose_grava_pendencias_imediatamente()
    {
        using (var s = Novo(atrasoMs: 60_000)) s.Registrar("v", 42, 100);
        using var outra = Novo();
        Assert.Equal(42, outra.Obter("v")!.Posicao);
    }

    [Fact]
    public void Remover_esquece_o_video()
    {
        using (var s = Novo())
        {
            s.Registrar("v", 42, 100);
            s.Remover("v");
        }
        using var outra = Novo();
        Assert.Null(outra.Obter("v"));
    }

    [Fact]
    public void Arquivo_corrompido_nao_impede_o_inicio()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
        File.WriteAllText(_arquivo, "{ isto não é json");
        using var s = Novo();
        Assert.Null(s.Obter("v"));
        s.Registrar("v", 10, 100);
        Assert.Equal(10, s.Obter("v")!.Posicao);
    }

    [Fact]
    public void Chave_usa_id_ou_slug()
    {
        Assert.Equal("yt:abc", ProgressService.Chave("yt:abc", "x"));
        Assert.Equal("slug:x", ProgressService.Chave("", "x"));
        Assert.Equal("slug:x", ProgressService.Chave(null, "x"));
    }
}
