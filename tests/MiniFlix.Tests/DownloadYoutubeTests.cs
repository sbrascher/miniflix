using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix.Tests;

public sealed class DownloadYoutubeTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    private string Videos => Path.Combine(_raiz, "videos");
    public DownloadYoutubeTests() => Directory.CreateDirectory(Videos);
    public void Dispose() { try { Directory.Delete(_raiz, recursive: true); } catch { } }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://youtube.com/watch?v=BaW_jenozKc&t=42s&list=PL123", "BaW_jenozKc")]
    [InlineData("youtube.com/watch?v=BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://youtu.be/BaW_jenozKc?si=abc", "BaW_jenozKc")]
    [InlineData("https://m.youtube.com/watch?v=BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://music.youtube.com/watch?v=BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://www.youtube.com/shorts/BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://www.youtube.com/embed/BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("https://www.youtube.com/live/BaW_jenozKc", "BaW_jenozKc")]
    [InlineData("  https://www.youtube.com/watch?v=BaW_jenozKc  ", "BaW_jenozKc")]
    public void Extrai_id_de_urls_validas(string url, string id) => Assert.Equal(id, DownloadYoutube.ExtrairId(url));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/@canal")]
    [InlineData("https://www.youtube.com/watch?v=curto")]
    [InlineData("https://evil.com/watch?v=BaW_jenozKc")]
    [InlineData("https://youtube.com.evil.com/watch?v=BaW_jenozKc")]
    [InlineData("file:///C:/watch?v=BaW_jenozKc")]
    [InlineData("javascript:alert(1)")]
    public void Rejeita_urls_invalidas(string? url) => Assert.Null(DownloadYoutube.ExtrairId(url));

    // ---------- Vários links de uma vez ----------

    [Fact]
    public void Extrai_varios_links_de_um_texto_livre()
    {
        var texto = """
            https://www.youtube.com/watch?v=AAAAAAAAAAA
            https://youtu.be/BBBBBBBBBBB?si=x, youtube.com/shorts/CCCCCCCCCCC
            Olha esse aqui: https://m.youtube.com/watch?v=DDDDDDDDDDD&t=10s. E esse (www.youtube.com/watch?v=EEEEEEEEEEE)!
            https://www.youtube.com/watch?v=AAAAAAAAAAA
            """;
        var ids = DownloadYoutube.ExtrairLinks(texto).Select(l => l.Id).ToList();
        Assert.Equal(["AAAAAAAAAAA", "BBBBBBBBBBB", "CCCCCCCCCCC", "DDDDDDDDDDD", "EEEEEEEEEEE", "AAAAAAAAAAA"], ids);
    }

    [Theory]
    [InlineData("https://vimeo.com/123456", "Não é um link do YouTube.")]
    [InlineData("https://www.youtube.com/playlist?list=PL123", "Playlist sem um vídeo específico (ainda não suportado).")]
    [InlineData("https://www.youtube.com/@meucanal", "Link de canal, não de um vídeo.")]
    [InlineData("https://www.youtube.com/watch?v=curto", "Não foi possível identificar o vídeo neste link.")]
    public void Link_invalido_explica_o_motivo(string link, string motivo)
    {
        var l = Assert.Single(DownloadYoutube.ExtrairLinks($"texto {link} mais texto"));
        Assert.Null(l.Id);
        Assert.Equal(motivo, l.Motivo);
    }

    [Fact]
    public void Video_de_playlist_vale_so_o_video()
    {
        var l = Assert.Single(DownloadYoutube.ExtrairLinks("https://www.youtube.com/watch?v=AAAAAAAAAAA&list=PL123&index=2"));
        Assert.Equal("AAAAAAAAAAA", l.Id);
    }

    [Fact]
    public void Texto_sem_links_e_limite()
    {
        Assert.Empty(DownloadYoutube.ExtrairLinks("nenhum link aqui"));
        Assert.Empty(DownloadYoutube.ExtrairLinks(null));
        var muitos = string.Join('\n', Enumerable.Range(0, DownloadYoutube.MaxLinksPorVez + 1).Select(i => $"https://youtu.be/{i:D11}"));
        Assert.Throws<ArgumentException>(() => DownloadYoutube.ExtrairLinks(muitos));
    }

    [Fact]
    public void CriarItem_recusa_video_ja_no_catalogo()
    {
        var dir = Path.Combine(Videos, "existente");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "existente.mp4"), [1]);
        JsonStore.Gravar(Path.Combine(dir, "existente.json"), new VideoMeta { Id = "yt:BaW_jenozKc", Slug = "existente", Titulo = "Já tenho" });
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
        cat.Varrer();

        var ex = Assert.Throws<ArgumentException>(() => new DownloadYoutube(cat).CriarItem("https://youtu.be/BaW_jenozKc", ""));
        Assert.Contains("Já tenho", ex.Message);
    }

    [Fact]
    public void Le_metadados_do_yt_dlp()
    {
        var json = """
            { "id": "abc", "title": "  Aula 1 — Introdução  ", "description": "Linha 1\nLinha 2", "duration": 125.5,
              "upload_date": "20240131", "requested_formats": [ {"format_id": "137"}, {"format_id": "140"} ] }
            """;
        var m = DownloadYoutube.LerMetadados(json, "abc");
        Assert.Equal("Aula 1 — Introdução", m.Titulo);
        Assert.Equal("Linha 1\nLinha 2", m.Descricao);
        Assert.Equal(125.5, m.Duracao);
        Assert.Equal("2024-01-31", m.DataPublicacao);
        Assert.Equal(2, m.PartesDownload);
    }

    [Fact]
    public void Metadados_sem_titulo_usam_id()
    {
        var m = DownloadYoutube.LerMetadados("""{ "id": "abc" }""", "abc");
        Assert.Equal("Vídeo do YouTube abc", m.Titulo);
        Assert.Equal(1, m.PartesDownload);
        Assert.Null(m.DataPublicacao);
    }

    [Theory]
    [InlineData("MFPROG 500 1000 NA 2097152 30", 0.5, "2,0 MB/s · 0:30 restantes")]
    [InlineData("MFPROG 250 NA 1000 51200 3725", 0.25, "50 KB/s · 1:02:05 restantes")]
    [InlineData("MFPROG 1000 1000 NA NA NA", 1.0, "")]
    public void Le_linha_de_progresso(string linha, double fracao, string descricao)
    {
        var p = DownloadYoutube.LerProgresso(linha)!;
        Assert.Equal(fracao, p.Fracao, 3);
        Assert.Equal(descricao, p.Descricao(1, 1));
    }

    [Theory]
    [InlineData("MFPROG NA NA NA NA NA")]
    [InlineData("MFPROG 10 0 0 1 1")]
    [InlineData("lixo")]
    public void Ignora_progresso_invalido(string linha) => Assert.Null(DownloadYoutube.LerProgresso(linha));

    [Theory]
    [InlineData("ERROR: [youtube] x: Unable to download webpage: <urlopen error [Errno 11001] getaddrinfo failed>", "Sem conexão")]
    [InlineData("ERROR: [youtube] x: Private video. Sign in if you've been granted access", "privado")]
    [InlineData("ERROR: [youtube] x: Sign in to confirm your age. This video may be inappropriate for some users.", "restrição de idade")]
    [InlineData("ERROR: [youtube] x: Sign in to confirm you’re not a bot.", "verificação")]
    [InlineData("ERROR: [youtube] x: Video unavailable. This video has been removed by the uploader", "indisponível")]
    [InlineData("ERROR: [youtube] x: Some unexpected thing", "Falha no download. [youtube] x: Some unexpected thing")]
    public void Traduz_erros_do_yt_dlp(string erro, string trecho) =>
        Assert.Contains(trecho, DownloadYoutube.TraduzirErro(erro));

    /// <summary>
    /// Download real (precisa de internet): só roda com MINIFLIX_TESTE_REDE=1.
    /// Usa "Me at the zoo" (19 s), o primeiro vídeo do YouTube — também usado nos testes do yt-dlp.
    /// </summary>
    [Fact]
    public async Task Download_real_do_video_de_teste()
    {
        if (Environment.GetEnvironmentVariable("MINIFLIX_TESTE_REDE") != "1" || Ferramentas.YtDlp is null || Ferramentas.Ffmpeg is null) return;

        var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
        var fila = new FilaService();
        var etapas = new List<string>();
        fila.Alterada += itens => { lock (etapas) etapas.Add(itens[0].Etapa + " | " + itens[0].Detalhe); };
        fila.Adicionar(new DownloadYoutube(cat).CriarItem("https://www.youtube.com/watch?v=jNQXAC9IVRw", "Testes"));
        var fim = DateTime.Now.AddMinutes(3);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(200);

        var item = fila.Itens[0];
        Assert.True(item.Estado == EstadoFila.Concluido, $"{item.Estado}: {item.Mensagem}");
        var v = Assert.Single(cat.Itens);
        Assert.Equal("yt:jNQXAC9IVRw", v.Meta.Id);
        Assert.Equal("youtube", v.Meta.Origem);
        Assert.Equal("Testes", v.Meta.Categoria);
        Assert.False(string.IsNullOrWhiteSpace(v.Meta.Titulo));
        Assert.NotNull(v.CapaUrl);
        var info = await Ffmpeg.AnalisarAsync(Path.Combine(Videos, v.Meta.Slug, v.Meta.Slug + ".mp4"), CancellationToken.None);
        Assert.True(info.CompativelSemConverter, $"{info.CodecVideo}/{info.PixFmt}/{info.CodecAudio}");
        Assert.Empty(Directory.GetDirectories(Videos, "_tmp-*"));
        Assert.Contains(etapas, e => e.StartsWith("Baixando vídeo"));
    }
}
