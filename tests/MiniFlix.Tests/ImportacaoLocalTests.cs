using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix.Tests;

public sealed class ImportacaoLocalTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    private string Videos => Path.Combine(_raiz, "videos");
    private string Origem => Path.Combine(_raiz, "origem");

    public ImportacaoLocalTests()
    {
        Directory.CreateDirectory(Videos);
        Directory.CreateDirectory(Origem);
    }

    public void Dispose() { try { Directory.Delete(_raiz, recursive: true); } catch { } }

    [Theory]
    [InlineData(@"C:\x\aula_01.intro-final.mp4", "aula 01 intro-final")]
    [InlineData(@"C:\x\Meu   Vídeo.MKV", "Meu Vídeo")]
    [InlineData(@"C:\x\___.mp4", "Vídeo")]
    public void Titulo_sugerido_a_partir_do_nome(string arquivo, string esperado) =>
        Assert.Equal(esperado, ImportacaoLocal.TituloSugerido(arquivo));

    [Fact]
    public void Id_e_estavel_e_diferencia_conteudo()
    {
        var a = Path.Combine(Origem, "a.bin");
        var b = Path.Combine(Origem, "b.bin");
        File.WriteAllBytes(a, [1, 2, 3]);
        File.WriteAllBytes(b, [1, 2, 4]);
        Assert.Equal(ImportacaoLocal.CalcularId(a), ImportacaoLocal.CalcularId(a));
        Assert.NotEqual(ImportacaoLocal.CalcularId(a), ImportacaoLocal.CalcularId(b));
        Assert.StartsWith("local:", ImportacaoLocal.CalcularId(a));
    }

    // ---------- Integração com ffmpeg (ignorados se o ffmpeg não estiver disponível) ----------

    private static bool TemFfmpeg => Ferramentas.Ffmpeg is not null && Ferramentas.Ffprobe is not null;

    private async Task<string> GerarVideo(string nome, params string[] codecs)
    {
        var destino = Path.Combine(Origem, nome);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=15", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "3" };
        args.AddRange(codecs);
        args.Add(destino);
        var r = await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, args, CancellationToken.None);
        Assert.Equal(0, r.CodigoSaida);
        return destino;
    }

    private async Task<(FilaService fila, CatalogService cat)> Importar(string arquivo, string titulo, string categoria,
        IReadOnlyList<string>? tags = null, bool reduzirPara1080 = false)
    {
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "cache.json"), p => p);
        var fila = new FilaService();
        fila.Adicionar(new ImportacaoLocal(cat).CriarItem(arquivo, titulo, categoria, tags, reduzirPara1080));
        var fim = DateTime.Now.AddSeconds(60);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(50);
        return (fila, cat);
    }

    [Fact]
    public async Task Mp4_compativel_e_copiado_sem_conversao()
    {
        if (!TemFfmpeg) return;
        var mp4 = await GerarVideo("compat.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");

        var (fila, cat) = await Importar(mp4, "Aula #01: Introdução!", "Cursos");
        var item = Assert.Single(fila.Itens);
        Assert.Equal(EstadoFila.Concluido, item.Estado);
        Assert.Equal("aula-01-introducao", item.Slug);
        Assert.Contains("sem perda", item.Mensagem);

        var dir = Path.Combine(Videos, "aula-01-introducao");
        Assert.True(File.Exists(Path.Combine(dir, "aula-01-introducao.mp4")));
        Assert.True(File.Exists(Path.Combine(dir, "aula-01-introducao.jpg")));
        var meta = JsonStore.Ler<VideoMeta>(Path.Combine(dir, "aula-01-introducao.json"))!;
        Assert.Equal("Aula #01: Introdução!", meta.Titulo);
        Assert.Equal("Cursos", meta.Categoria);
        Assert.Equal("local", meta.Origem);
        Assert.InRange(meta.DuracaoSegundos, 2.5, 3.5);
        Assert.Single(cat.Itens);
        Assert.Empty(Directory.GetDirectories(Videos, "_tmp-*"));
        Assert.True(File.Exists(mp4), "o arquivo original não pode ser alterado");
    }

    [Fact]
    public async Task Importacao_grava_as_tags()
    {
        if (!TemFfmpeg) return;
        var mp4 = await GerarVideo("tags.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        var (fila, cat) = await Importar(mp4, "Com tags", "Cursos", ["python", "iniciante"]);
        Assert.Equal(EstadoFila.Concluido, Assert.Single(fila.Itens).Estado);
        Assert.Equal(["python", "iniciante"], Assert.Single(cat.Itens).Meta.Tags);
        Assert.Equal(["python", "iniciante"],
            JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "com-tags", "com-tags.json"))!.Tags);
    }

    /// <summary>Hash do conteúdo da trilha de vídeo (pacotes), para provar que ela foi copiada bit a bit.</summary>
    private static async Task<string> HashDoVideo(string arquivo)
    {
        var saida = new System.Text.StringBuilder();
        var r = await Processo.ExecutarAsync(Ferramentas.Ffmpeg!,
            ["-hide_banner", "-loglevel", "error", "-i", arquivo, "-map", "0:v:0", "-c", "copy", "-f", "hash", "-hash", "md5", "-"],
            CancellationToken.None, aoLerSaida: l => saida.AppendLine(l));
        Assert.Equal(0, r.CodigoSaida);
        return saida.ToString().Trim();
    }

    [Fact]
    public async Task Video_compativel_e_copiado_bit_a_bit()
    {
        if (!TemFfmpeg) return;
        var mp4 = await GerarVideo("original.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        var (fila, _) = await Importar(mp4, "Original", "");
        Assert.Equal(EstadoFila.Concluido, Assert.Single(fila.Itens).Estado);
        Assert.Equal(await HashDoVideo(mp4), await HashDoVideo(Path.Combine(Videos, "original", "original.mp4")));
    }

    [Fact]
    public async Task Audio_incompativel_vira_flac_e_o_video_e_copiado_sem_perda()
    {
        if (!TemFfmpeg) return;
        // MKV de câmera/filme: H.264 4:2:2 + AC3 5.1. Antes, o vídeo inteiro era reconvertido por causa do áudio.
        var destino = Path.Combine(Origem, "filme.mkv");
        var r = await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=15", "-f", "lavfi", "-i", "sine", "-t", "1",
            "-filter_complex", "[1:a]pan=5.1|c0=c0|c1=c0|c2=c0|c3=c0|c4=c0|c5=c0[a]", "-map", "0:v", "-map", "[a]",
            "-c:v", "libx264", "-pix_fmt", "yuv422p", "-c:a", "ac3", destino], CancellationToken.None);
        Assert.Equal(0, r.CodigoSaida);

        var (fila, _) = await Importar(destino, "Filme", "");
        var item = Assert.Single(fila.Itens);
        Assert.True(item.Estado == EstadoFila.Concluido, item.Mensagem);
        Assert.Contains("áudio convertido para FLAC", item.Mensagem);
        var saida = Path.Combine(Videos, "filme", "filme.mp4");
        var info = await Ffmpeg.AnalisarAsync(saida, CancellationToken.None);
        Assert.Equal("h264", info.CodecVideo);
        Assert.Equal("yuv422p", info.PixFmt);
        Assert.Equal("flac", info.CodecAudio);
        Assert.Equal(6, info.Canais);                                  // 5.1 preservado
        Assert.Equal(await HashDoVideo(destino), await HashDoVideo(saida)); // vídeo idêntico
    }

    [Fact]
    public async Task Avi_e_convertido_para_h264_aac()
    {
        if (!TemFfmpeg) return;
        var avi = await GerarVideo("antigo.avi", "-c:v", "mpeg4", "-c:a", "mp3");

        var (fila, _) = await Importar(avi, "", "");
        var item = Assert.Single(fila.Itens);
        Assert.Equal(EstadoFila.Concluido, item.Estado);
        Assert.Equal("antigo", item.Slug);
        Assert.Contains("Convertido", item.Mensagem);

        var info = await Ffmpeg.AnalisarAsync(Path.Combine(Videos, "antigo", "antigo.mp4"), CancellationToken.None);
        Assert.Equal("h264", info.CodecVideo);
        Assert.Equal("mp3", info.CodecAudio); // o áudio MP3 já tocava: copiado, não reconvertido
        Assert.True(info.CompativelSemConverter);
    }

    [Fact]
    public async Task Mkv_com_vp9_e_opus_e_copiado_sem_conversao()
    {
        if (!TemFfmpeg) return;
        var mkv = await GerarVideo("youtube.mkv", "-c:v", "libvpx-vp9", "-b:v", "500k", "-pix_fmt", "yuv420p", "-c:a", "libopus");

        var (fila, _) = await Importar(mkv, "Baixado do YouTube", "");
        var item = Assert.Single(fila.Itens);
        Assert.True(item.Estado == EstadoFila.Concluido, item.Mensagem);
        Assert.Contains("sem perda", item.Mensagem);
        var info = await Ffmpeg.AnalisarAsync(Path.Combine(Videos, item.Slug!, item.Slug + ".mp4"), CancellationToken.None);
        Assert.Equal("vp9", info.CodecVideo);   // mesma trilha, só mudou o contêiner
        Assert.Equal("opus", info.CodecAudio);
    }

    private async Task<string> GerarHevc(string nome, string pix = "yuv420p")
    {
        // 2560×1440 em HEVC: precisa converter (o player não mostra a imagem).
        var destino = Path.Combine(Origem, nome);
        var r = await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=2560x1440:rate=15", "-f", "lavfi", "-i", "sine", "-t", "1",
            "-c:v", "libx265", "-preset", "ultrafast", "-pix_fmt", pix, "-x265-params", "log-level=error", "-c:a", "aac", destino],
            CancellationToken.None);
        Assert.Equal(0, r.CodigoSaida);
        return destino;
    }

    [Fact]
    public async Task Hevc_e_convertido_em_alta_qualidade_na_resolucao_original()
    {
        if (!TemFfmpeg) return;
        var (fila, _) = await Importar(await GerarHevc("hevc.mp4", "yuv420p10le"), "HEVC", "");
        var item = Assert.Single(fila.Itens);
        Assert.True(item.Estado == EstadoFila.Concluido, item.Mensagem);
        Assert.Equal("Convertido de HEVC/H.265 para H.264 em alta qualidade, na resolução original.", item.Mensagem);
        var info = await Ffmpeg.AnalisarAsync(Path.Combine(Videos, item.Slug!, item.Slug + ".mp4"), CancellationToken.None);
        Assert.Equal("h264", info.CodecVideo);
        Assert.Equal(2560, info.Largura);           // sem reduzir
        Assert.Equal(1440, info.Altura);
        Assert.Equal("yuv420p10le", info.PixFmt);   // 10 bits preservados
        Assert.Equal("aac", info.CodecAudio);       // áudio compatível: copiado
    }

    [Fact]
    public async Task Hevc_com_opcao_de_reduzir_fica_em_1080p()
    {
        if (!TemFfmpeg) return;
        var (fila, _) = await Importar(await GerarHevc("hevc-menor.mp4"), "HEVC menor", "", reduzirPara1080: true);
        var item = Assert.Single(fila.Itens);
        Assert.True(item.Estado == EstadoFila.Concluido, item.Mensagem);
        Assert.Contains("reduzido para 1080p", item.Mensagem);
        var info = await Ffmpeg.AnalisarAsync(Path.Combine(Videos, item.Slug!, item.Slug + ".mp4"), CancellationToken.None);
        Assert.Equal(1920, info.Largura);
        Assert.Equal(1080, info.Altura);
    }

    [Fact]
    public async Task Mesmo_arquivo_duas_vezes_e_recusado()
    {
        if (!TemFfmpeg) return;
        var mp4 = await GerarVideo("dup.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "cache.json"), p => p);
        var imp = new ImportacaoLocal(cat);
        var fila = new FilaService();
        fila.Adicionar(imp.CriarItem(mp4, "Primeiro", ""));
        fila.Adicionar(imp.CriarItem(mp4, "Segundo", ""));
        var fim = DateTime.Now.AddSeconds(60);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(50);

        Assert.Equal(EstadoFila.Concluido, fila.Itens[0].Estado);
        Assert.Equal(EstadoFila.Erro, fila.Itens[1].Estado);
        Assert.Contains("já está no catálogo", fila.Itens[1].Mensagem);
        Assert.Single(cat.Itens);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_video_gera_erro_e_nao_deixa_lixo()
    {
        if (!TemFfmpeg) return;
        var falso = Path.Combine(Origem, "falso.mp4");
        File.WriteAllText(falso, "isto não é um vídeo");

        var (fila, cat) = await Importar(falso, "Falso", "");
        Assert.Equal(EstadoFila.Erro, fila.Itens[0].Estado);
        Assert.Empty(cat.Itens);
        Assert.Empty(Directory.GetDirectories(Videos));
    }

    [Fact]
    public async Task Capa_de_imagem_png_vira_jpg_de_640px()
    {
        if (!TemFfmpeg) return;
        var png = Path.Combine(Origem, "arte.png");
        await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1600x900", "-frames:v", "1", png], CancellationToken.None);

        var jpg = Path.Combine(Origem, "capa.jpg");
        await Ffmpeg.ConverterImagemParaCapaAsync(png, jpg, CancellationToken.None);
        var info = await Ffmpeg.AnalisarAsync(jpg, CancellationToken.None);
        Assert.Equal("mjpeg", info.CodecVideo);
        Assert.Equal(640, info.Largura);
        Assert.Equal(360, info.Altura);
    }

    [Fact]
    public async Task Capa_de_imagem_pequena_nao_e_ampliada()
    {
        if (!TemFfmpeg) return;
        var png = Path.Combine(Origem, "pequena.png");
        await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180", "-frames:v", "1", png], CancellationToken.None);
        var jpg = Path.Combine(Origem, "capa.jpg");
        await Ffmpeg.ConverterImagemParaCapaAsync(png, jpg, CancellationToken.None);
        Assert.Equal(320, (await Ffmpeg.AnalisarAsync(jpg, CancellationToken.None)).Largura);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_imagem_gera_erro_amigavel()
    {
        if (!TemFfmpeg) return;
        var falso = Path.Combine(Origem, "falso.png");
        File.WriteAllText(falso, "não sou uma imagem");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Ffmpeg.ConverterImagemParaCapaAsync(falso, Path.Combine(Origem, "c.jpg"), CancellationToken.None));
        Assert.Contains("imagem", ex.Message);
    }

    [Fact]
    public async Task Capa_de_quadro_no_instante_escolhido()
    {
        if (!TemFfmpeg) return;
        var mp4 = await GerarVideo("quadro.mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        var jpg = Path.Combine(Origem, "quadro.jpg");
        Assert.True(await Ffmpeg.GerarCapaNoInstanteAsync(mp4, jpg, 2.0, CancellationToken.None));
        // Quadros de vídeo sempre saem na largura padrão (igual às capas automáticas).
        Assert.Equal(Ffmpeg.LarguraCapa, (await Ffmpeg.AnalisarAsync(jpg, CancellationToken.None)).Largura);
    }

    [Fact]
    public async Task Cancelar_durante_conversao_apaga_pasta_temporaria()
    {
        if (!TemFfmpeg) return;
        // Vídeo mais longo e conversão forçada (avi) para dar tempo de cancelar.
        var destino = Path.Combine(Origem, "longo.avi");
        await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "60", "-c:v", "mpeg4", "-q:v", "2", destino], CancellationToken.None);

        var cat = new CatalogService(Videos, Path.Combine(_raiz, "cache.json"), p => p);
        var fila = new FilaService();
        var item = fila.Adicionar(new ImportacaoLocal(cat).CriarItem(destino, "Longo", ""));
        var fim = DateTime.Now.AddSeconds(30);
        while (!fila.Itens[0].Etapa.StartsWith("Convertendo") && DateTime.Now < fim) await Task.Delay(20);
        await Task.Delay(300);
        fila.Cancelar(item.Id);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(50);

        Assert.Equal(EstadoFila.Cancelado, fila.Itens[0].Estado);
        Assert.Empty(Directory.GetDirectories(Videos));
    }
}
