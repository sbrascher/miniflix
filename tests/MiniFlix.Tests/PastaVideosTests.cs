using MiniFlix.Services;

namespace MiniFlix.Tests;

public sealed class PastaVideosTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    private string Raiz => Path.Combine(_base, "MiniFlix");
    private string Padrao => Path.Combine(Raiz, "videos");

    public PastaVideosTests() => Directory.CreateDirectory(Padrao);
    public void Dispose() { try { Directory.Delete(_base, recursive: true); } catch { } }

    [Fact]
    public void Sem_configuracao_usa_a_pasta_padrao()
    {
        Assert.Equal(Padrao, PastaVideos.Resolver(null, Raiz, Padrao));
        Assert.Equal(Padrao, PastaVideos.Resolver("  ", Raiz, Padrao));
    }

    [Fact]
    public void Mesma_unidade_grava_relativo_e_resolve_de_volta()
    {
        var filmes = Path.Combine(_base, "Filmes");
        var valor = PastaVideos.ParaConfig(filmes, Raiz, Padrao);
        Assert.Equal(Path.Combine("..", "Filmes"), valor);
        Assert.Equal(filmes, PastaVideos.Resolver(valor, Raiz, Padrao));
    }

    [Fact]
    public void Pasta_padrao_grava_null()
    {
        Assert.Null(PastaVideos.ParaConfig(Padrao + Path.DirectorySeparatorChar, Raiz, Padrao));
    }

    [Fact]
    public void Outra_unidade_grava_absoluto()
    {
        // Escolhe uma letra diferente da unidade dos testes.
        var outra = Path.GetPathRoot(_base)!.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? @"Y:\Filmes" : @"Z:\Filmes";
        Assert.Equal(outra, PastaVideos.ParaConfig(outra, Raiz, Padrao));
        Assert.Equal(outra, PastaVideos.Resolver(outra, Raiz, Padrao));
    }

    [Fact]
    public void Validar_aceita_pasta_existente_e_gravavel()
    {
        var filmes = Directory.CreateDirectory(Path.Combine(_base, "Filmes")).FullName;
        Assert.Null(PastaVideos.Validar(filmes, Raiz, Padrao));
        Assert.Empty(Directory.GetFiles(filmes)); // o arquivo de teste de gravação não fica para trás
        Assert.Null(PastaVideos.Validar(Padrao, Raiz, Padrao));
    }

    [Fact]
    public void Validar_rejeita_pastas_perigosas_ou_inexistentes()
    {
        Assert.NotNull(PastaVideos.Validar(Raiz, Raiz, Padrao));                          // a própria pasta do app
        Assert.NotNull(PastaVideos.Validar(Path.Combine(Raiz, "data"), Raiz, Padrao));    // dentro do app
        Assert.NotNull(PastaVideos.Validar(_base, Raiz, Padrao));                         // contém o app
        Assert.NotNull(PastaVideos.Validar(Path.Combine(_base, "nao-existe"), Raiz, Padrao));
    }
}
