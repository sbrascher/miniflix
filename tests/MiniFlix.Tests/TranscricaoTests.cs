using System.Text.Json;
using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix.Tests;

/// <summary>Transcrição pelas legendas do YouTube. Os exemplos vêm de arquivos reais do yt-dlp (resumidos).</summary>
public sealed class TranscricaoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_raiz, recursive: true); } catch { } }

    private const string Auto = "https://www.youtube.com/api/timedtext?lang=x";
    private const string Traduzida = "https://www.youtube.com/api/timedtext?lang=x&tlang=y";

    private static JsonElement Info(string json) => JsonDocument.Parse(json).RootElement;

    private static string Legendas(string? manuais = null, string? automaticas = null) =>
        $$"""{ "title": "T", "subtitles": { {{manuais}} }, "automatic_captions": { {{automaticas}} } }""";

    private static string Faixa(string chave, string url = Auto) => $$"""  "{{chave}}": [ { "ext": "json3", "url": "x" }, { "ext": "vtt", "url": "{{url}}" } ] """;

    // ---------- Escolha da legenda ----------

    [Fact]
    public void Video_em_portugues_so_com_automaticas_usa_a_original_nao_a_traducao()
    {
        // Real: "Aprenda Python em 10min" — sem legenda do autor; automática original "pt-orig"/"pt";
        // "en" e "pt-PT" são traduções automáticas (tlang).
        var json = Legendas(automaticas: string.Join(',', Faixa("en", Traduzida), Faixa("pt-orig"), Faixa("pt"), Faixa("pt-PT", Traduzida)));
        Assert.Equal(new Transcricao.Escolha("pt-orig", true), Transcricao.Escolher(Info(json)));
    }

    [Fact]
    public void Legenda_do_autor_em_portugues_vem_primeiro()
    {
        var json = Legendas(manuais: string.Join(',', Faixa("en"), Faixa("pt-BR")), automaticas: Faixa("pt-orig"));
        Assert.Equal(new Transcricao.Escolha("pt-BR", false), Transcricao.Escolher(Info(json)));
    }

    [Fact]
    public void Sem_portugues_usa_ingles_do_autor()
    {
        // Real: "Me at the zoo" — legendas do autor em inglês e alemão.
        var json = Legendas(manuais: string.Join(',', Faixa("en"), Faixa("de")), automaticas: string.Join(',', Faixa("en"), Faixa("de")));
        var e = Transcricao.Escolher(Info(json))!;
        Assert.Equal(new Transcricao.Escolha("en", false), e);
        Assert.Equal("inglês", e.NomeIdioma);
        Assert.False(e.Portugues);
    }

    [Fact]
    public void Traducao_automatica_para_portugues_nao_conta_como_portugues()
    {
        // Vídeo em inglês: o "pt" das automáticas é tradução (tlang) → fica com o inglês original.
        var json = Legendas(automaticas: string.Join(',', Faixa("en-orig"), Faixa("en"), Faixa("pt", Traduzida)));
        Assert.Equal(new Transcricao.Escolha("en-orig", true), Transcricao.Escolher(Info(json)));
    }

    [Fact]
    public void Sem_legenda_em_portugues_ou_ingles_retorna_null()
    {
        Assert.Null(Transcricao.Escolher(Info(Legendas(manuais: Faixa("ja"), automaticas: Faixa("ja")))));
        Assert.Null(Transcricao.Escolher(Info("""{ "title": "sem legendas" }""")));
    }

    // ---------- Leitura do .vtt ----------

    // Trecho real da legenda automática (com o efeito "karaokê": cada linha se repete no bloco seguinte).
    private const string VttAutomatico = """
        WEBVTT
        Kind: captions
        Language: pt

        00:00:00.120 --> 00:00:02.750 align:start position:0%

        fala<00:00:00.840><c> impressionadores</c><00:00:01.740><c> nesse</c><00:00:02.100><c> vídeo</c><00:00:02.280><c> eu</c>

        00:00:02.750 --> 00:00:02.760 align:start position:0%
        fala impressionadores nesse vídeo eu


        00:00:02.760 --> 00:00:05.510 align:start position:0%
        fala impressionadores nesse vídeo eu
        quero<00:00:02.879><c> te</c><00:00:03.600><c> dar</c><00:00:03.720><c> uma</c><00:00:03.959><c> introdução</c><00:00:04.319><c> ao</c><00:00:04.920><c> Python</c>

        00:00:05.510 --> 00:00:05.520 align:start position:0%
        quero te dar uma introdução ao Python


        00:00:05.520 --> 00:00:06.950 align:start position:0%
        quero te dar uma introdução ao Python
        como<00:00:05.580><c> que</c><00:00:05.759><c> funciona</c><00:00:06.000><c> essa</c><00:00:06.480><c> linguagem</c><00:00:06.720><c> de</c>
        """;

    [Fact]
    public void Legenda_automatica_vira_texto_corrido_sem_repeticoes()
    {
        var trechos = Transcricao.LerVtt(VttAutomatico);
        Assert.Equal(
            ["fala impressionadores nesse vídeo eu", "quero te dar uma introdução ao Python", "como que funciona essa linguagem de"],
            trechos.Select(t => t.Texto));
        Assert.Equal(0.12, trechos[0].Segundos, 3);
        Assert.Equal(2.76, trechos[1].Segundos, 3);
    }

    [Fact]
    public void Legenda_do_autor_com_numeros_de_bloco_e_entidades()
    {
        var vtt = "WEBVTT\n\n1\n00:00:01.200 --> 00:00:03.360\nAll right, so here we are, in front of the\nelephants\n\n" +
                  "2\n01:00:05.318 --> 01:00:07.974\nthe cool thing &amp; <i>really</i> long trunks\n";
        var trechos = Transcricao.LerVtt(vtt);
        Assert.Equal(["All right, so here we are, in front of the", "elephants", "the cool thing & really long trunks"],
            trechos.Select(t => t.Texto));
        Assert.Equal(3605.318, trechos[2].Segundos, 3);
    }

    [Fact]
    public void Paragrafos_quebram_por_tempo_e_preferem_fim_de_frase()
    {
        var trechos = new List<Transcricao.Trecho>
        {
            new(0, "Primeira frase."), new(20, "continua"), new(31, "Nova ideia aqui."), new(40, "mais"),
            new(105, "bem depois"),
        };
        var p = Transcricao.Paragrafos(trechos);
        Assert.Equal(["Primeira frase. continua Nova ideia aqui.", "mais", "bem depois"], p.Select(x => x.Texto));
        Assert.Equal([0, 40, 105], p.Select(x => x.Segundos));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(65.9, "01:05")]
    [InlineData(4000, "1:06:40")]
    public void Horario_formatado(double s, string esperado) => Assert.Equal(esperado, Transcricao.Horario(s));

    [Fact]
    public void Markdown_tem_cabecalho_e_paragrafos_com_horario()
    {
        var md = Transcricao.GerarMarkdown("Aprenda Python", "https://www.youtube.com/watch?v=4p7axLXXBGU",
            new Transcricao.Escolha("pt-orig", true), Transcricao.LerVtt(VttAutomatico), new DateTime(2026, 11, 19));
        Assert.StartsWith("# Aprenda Python\n", md.Replace("\r\n", "\n"));
        Assert.Contains("- **Origem:** legenda automática do YouTube", md);
        Assert.Contains("- **Idioma:** português\n", md.Replace("\r\n", "\n"));
        Assert.Contains("- **Gerada pelo MiniFlix em:** 19/11/2026", md);
        Assert.Contains("> Legendas automáticas podem ter erros", md);
        Assert.Contains("**[00:00]** fala impressionadores nesse vídeo eu quero te dar uma introdução ao Python como que funciona", md);

        var ingles = Transcricao.GerarMarkdown("Zoo", "u", new Transcricao.Escolha("en", false), [new(1, "Hi")], DateTime.Now);
        Assert.Contains("- **Idioma:** inglês (o vídeo não tem legenda em português)", ingles);
        Assert.DoesNotContain("Legendas automáticas", ingles);
    }

    // ---------- Gravação no catálogo ----------

    [Fact]
    public void SalvarTranscricao_grava_md_vtt_e_idioma_sem_mexer_no_resto()
    {
        var videos = Path.Combine(_raiz, "videos");
        var dir = Path.Combine(videos, "v");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "v.mp4"), [1]);
        JsonStore.Gravar(Path.Combine(dir, "v.json"),
            new VideoMeta { Id = "yt:AAAAAAAAAAA", Slug = "v", Titulo = "Vídeo", Tags = ["python"], Favorito = true });
        var cat = new CatalogService(videos, Path.Combine(_raiz, "c.json"), p => "url:" + Path.GetFileName(p));
        cat.Varrer();
        Assert.Null(cat.Itens[0].TranscricaoUrl);

        var item = cat.SalvarTranscricao("v", "# Vídeo\n", "WEBVTT\n", new Transcricao.Escolha("pt-BR", false));
        Assert.StartsWith("url:v.transcricao.md?v=", item.TranscricaoUrl);
        Assert.Equal("# Vídeo\n", File.ReadAllText(Path.Combine(dir, "v.transcricao.md")));
        Assert.True(File.Exists(Path.Combine(dir, "v.legenda.vtt")));
        var meta = JsonStore.Ler<VideoMeta>(Path.Combine(dir, "v.json"))!;
        Assert.Equal("pt-BR", meta.TranscricaoIdioma);
        Assert.False(meta.TranscricaoAutomatica);
        Assert.Equal(["python"], meta.Tags);
        Assert.True(meta.Favorito);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
