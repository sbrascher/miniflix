using MiniFlix.Services;

namespace MiniFlix.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Aula #01: Introdução!", "aula-01-introducao")]
    [InlineData("  Ação   e   Reação  ", "acao-e-reacao")]
    [InlineData("Pão de Queijo — Receita da Vovó", "pao-de-queijo-receita-da-vovo")]
    [InlineData("C# & .NET 10", "c-net-10")]
    [InlineData("ÇÃÕ ÉÊ ü ñ", "cao-ee-u-n")]
    [InlineData("---", "video")]
    [InlineData("", "video")]
    [InlineData(null, "video")]
    [InlineData("日本語", "video")]
    public void Gerar_normaliza_titulos(string? titulo, string esperado) =>
        Assert.Equal(esperado, Slug.Gerar(titulo));

    [Fact]
    public void Gerar_limita_tamanho_cortando_em_fronteira_de_palavra()
    {
        var slug = Slug.Gerar("Uma introdução muito longa sobre programação orientada a objetos com exemplos práticos");
        Assert.True(slug.Length <= Slug.TamanhoMaximo);
        Assert.False(slug.EndsWith('-'));
        Assert.Equal("uma-introducao-muito-longa-sobre-programacao-orientada-a", slug);
    }

    [Fact]
    public void Unico_acrescenta_sufixo_numerico()
    {
        var existentes = new HashSet<string> { "aula", "aula-2" };
        Assert.Equal("aula-3", Slug.Unico("aula", existentes.Contains));
        Assert.Equal("outra", Slug.Unico("outra", existentes.Contains));
    }

    [Theory]
    [InlineData("aula-01", true)]
    [InlineData("a", true)]
    [InlineData("Aula", false)]
    [InlineData("aula--01", false)]
    [InlineData("-aula", false)]
    [InlineData("..", false)]
    [InlineData("..\\windows", false)]
    [InlineData("aula 01", false)]
    [InlineData(null, false)]
    public void EhValido_bloqueia_nomes_perigosos(string? slug, bool esperado) =>
        Assert.Equal(esperado, Slug.EhValido(slug));
}
