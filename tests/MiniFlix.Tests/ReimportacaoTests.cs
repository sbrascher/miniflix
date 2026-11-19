using System.Security.Cryptography;
using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix.Tests;

/// <summary>Impressão digital, varredura de pastas e reimportação mantendo os metadados.</summary>
public sealed class ReimportacaoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    private string Videos => Path.Combine(_raiz, "videos");
    private string Origem => Path.Combine(_raiz, "origem");

    public ReimportacaoTests()
    {
        Directory.CreateDirectory(Videos);
        Directory.CreateDirectory(Origem);
    }

    public void Dispose() { try { Directory.Delete(_raiz, recursive: true); } catch { } }

    private string Arquivo(string nome, byte[] conteudo)
    {
        var caminho = Path.Combine(Origem, nome);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllBytes(caminho, conteudo);
        return caminho;
    }

    private static byte[] Aleatorio(int tamanho, int semente)
    {
        var b = new byte[tamanho];
        new Random(semente).NextBytes(b);
        return b;
    }

    // ---------- Impressão digital ----------

    [Fact]
    public void Mesmo_conteudo_com_outro_nome_tem_a_mesma_impressao()
    {
        var dados = Aleatorio(3 * 1024 * 1024, 1);
        var a = ImpressaoDigital.Calcular(Arquivo("aula.mp4", dados));
        var b = ImpressaoDigital.Calcular(Arquivo(Path.Combine("outra", "renomeado.mkv"), dados));
        Assert.Equal(a, b);
        Assert.StartsWith("local2:", a.Atual);
    }

    [Theory]
    [InlineData(100)]                   // muda o meio
    [InlineData(3 * 1024 * 1024 - 10)]  // muda o fim
    public void Mudanca_no_meio_ou_no_fim_muda_a_impressao(int posicao)
    {
        var dados = Aleatorio(3 * 1024 * 1024, 2);
        var a = ImpressaoDigital.Calcular(Arquivo("a.mp4", dados));
        var alterado = (byte[])dados.Clone();
        alterado[posicao == 100 ? dados.Length / 2 : posicao] ^= 0xFF;
        var b = ImpressaoDigital.Calcular(Arquivo("b.mp4", alterado));
        Assert.NotEqual(a.Atual, b.Atual);
        Assert.Equal(a.Antiga, b.Antiga); // o formato antigo só olhava o primeiro 1 MB: não percebia
    }

    [Fact]
    public void Formato_antigo_e_igual_ao_que_ja_esta_gravado_nos_videos()
    {
        var dados = Aleatorio(2 * 1024 * 1024, 3);
        var caminho = Arquivo("v.mp4", dados);
        // Fórmula usada até agora em ImportacaoLocal.CalcularId.
        var esperado = "local:" + Convert.ToHexStringLower(
            SHA256.HashData([.. dados.AsSpan(0, 1024 * 1024).ToArray(), .. BitConverter.GetBytes((long)dados.Length)]))[..16];
        Assert.Equal(esperado, ImpressaoDigital.Calcular(caminho).Antiga);
    }

    [Fact]
    public void Arquivo_pequeno_funciona()
    {
        var a = ImpressaoDigital.Calcular(Arquivo("p.mp4", Aleatorio(1000, 4)));
        var b = ImpressaoDigital.Calcular(Arquivo("q.mp4", Aleatorio(1000, 5)));
        Assert.NotEqual(a.Atual, b.Atual);
    }

    [Fact]
    public void Catalogo_reconhece_pela_impressao_antiga_e_pela_atual()
    {
        var dados = Aleatorio(1500 * 1024, 6);
        var ids = ImpressaoDigital.Calcular(Arquivo("x.mp4", dados));
        foreach (var (slug, id) in new[] { ("velho", ids.Antiga), ("novo", ids.Atual) })
        {
            var dir = Path.Combine(Videos, slug);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, slug + ".mp4"), [1]);
            JsonStore.Gravar(Path.Combine(dir, slug + ".json"), new VideoMeta { Id = id, Slug = slug, Titulo = slug });
            var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
            cat.Varrer();
            Assert.Equal(slug, cat.ObterPorImpressao(ids)?.Meta.Slug);
            Directory.Delete(dir, true);
        }
    }

    // ---------- Varredura de pastas ----------

    [Fact]
    public void Pasta_inclui_subpastas_e_ignora_ocultas_nao_videos_e_o_acervo()
    {
        Arquivo("a.mp4", [1]);
        Arquivo(Path.Combine("Cursos", "Python", "aula1.MKV"), [2]);
        Arquivo(Path.Combine("Cursos", "leia-me.txt"), [3]);
        var oculta = Path.Combine(Origem, "oculta");
        Arquivo(Path.Combine("oculta", "x.mp4"), [4]);
        File.SetAttributes(oculta, FileAttributes.Directory | FileAttributes.Hidden);

        var cat = new CatalogService(Path.Combine(Origem, "acervo"), Path.Combine(_raiz, "c.json"), p => p);
        Arquivo(Path.Combine("acervo", "v", "v.mp4"), [5]); // a própria pasta de vídeos do MiniFlix

        var lista = new ImportacaoLocal(cat).ListarVideosDaPasta(Origem);
        Assert.Equal([Path.Combine(Origem, "a.mp4"), Path.Combine(Origem, "Cursos", "Python", "aula1.MKV")], lista);
    }

    [Fact]
    public async Task Analise_marca_existentes_e_subpasta()
    {
        var conhecido = Arquivo(Path.Combine("Cursos", "Python", "aula1.mp4"), Aleatorio(2048, 7));
        var novo = Arquivo("solto.mp4", Aleatorio(2048, 8));
        var dir = Path.Combine(Videos, "aula-1");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "aula-1.mp4"), [1]);
        JsonStore.Gravar(Path.Combine(dir, "aula-1.json"),
            new VideoMeta { Id = ImpressaoDigital.Calcular(conhecido).Antiga, Slug = "aula-1", Titulo = "Aula 1" });
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
        cat.Varrer();

        var progresso = new List<int>();
        var r = await new ImportacaoLocal(cat).AnalisarAsync([conhecido, novo], Origem, (f, _) => { lock (progresso) progresso.Add(f); },
            CancellationToken.None);
        Assert.Equal("aula-1", r[0].Existente?.Meta.Slug);
        Assert.Equal("Python", r[0].Subpasta);
        Assert.Equal(Path.Combine("Cursos", "Python", "aula1.mp4"), r[0].CaminhoRelativo);
        Assert.Null(r[1].Existente);
        Assert.Null(r[1].Subpasta);   // direto na pasta escolhida: sem categoria por subpasta
        Assert.Equal(2, progresso.Max());
    }

    // ---------- Reimportação ----------

    private static bool TemFfmpeg => Ferramentas.Ffmpeg is not null && Ferramentas.Ffprobe is not null;

    private async Task<string> GerarVideo(string nome, params string[] codecs)
    {
        var destino = Path.Combine(Origem, nome);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=15", "-f", "lavfi", "-i", "sine", "-t", "2" };
        args.AddRange(codecs);
        args.Add(destino);
        Assert.Equal(0, (await Processo.ExecutarAsync(Ferramentas.Ffmpeg!, args, CancellationToken.None)).CodigoSaida);
        return destino;
    }

    private static async Task Executar(FilaService fila, FilaItem item)
    {
        fila.Adicionar(item);
        var fim = DateTime.Now.AddSeconds(60);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(50);
    }

    [Fact]
    public async Task Reimportar_troca_so_o_video_e_mantem_tudo()
    {
        if (!TemFfmpeg) return;
        // Importado antes "com perda": HEVC; depois o usuário organizou o vídeo.
        var original = await GerarVideo("celular.mp4", "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "log-level=error", "-c:a", "aac");
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
        var imp = new ImportacaoLocal(cat);
        var fila = new FilaService();
        await Executar(fila, imp.CriarItem(original, "Viagem", "Férias", ["praia"]));
        var slug = Assert.Single(cat.Itens).Meta.Slug;
        var idAntes = cat.Itens[0].Meta.Id;
        cat.Editar(slug, "Viagem 2024 (editado)", "Família", "Descrição minha", ["praia", "verão"]);
        cat.DefinirFavorito(slug, true);
        var capa = Path.Combine(Videos, slug, slug + ".jpg");
        File.WriteAllBytes(capa, [9, 9, 9]); // capa escolhida pelo usuário

        // Sem pedir reimportação: recusa (duplicado).
        await Executar(fila, imp.CriarItem(original, "Outro título", ""));
        Assert.Equal(EstadoFila.Erro, fila.Itens[^1].Estado);
        Assert.Contains("já está no catálogo", fila.Itens[^1].Mensagem);

        // Reimportando.
        await Executar(fila, imp.CriarItemReimportacao(original, slug));
        var item = fila.Itens[^1];
        Assert.True(item.Estado == EstadoFila.Concluido, item.Mensagem);
        Assert.StartsWith("Reimportado", item.Mensagem);

        var v = Assert.Single(cat.Itens).Meta;
        Assert.Equal(slug, v.Slug);
        Assert.Equal(idAntes, v.Id);                 // "continuar assistindo" continua valendo
        Assert.Equal("Viagem 2024 (editado)", v.Titulo);
        Assert.Equal("Família", v.Categoria);
        Assert.Equal("Descrição minha", v.Descricao);
        Assert.Equal(["praia", "verão"], v.Tags);
        Assert.True(v.Favorito);
        Assert.Equal([9, 9, 9], File.ReadAllBytes(capa));
        Assert.False(File.Exists(Path.Combine(Videos, slug, slug + ".mp4.substituindo")));
        Assert.Empty(Directory.GetDirectories(Videos, "_tmp-*"));
    }

    [Fact]
    public void Reimportacao_interrompida_e_recuperada_ao_abrir()
    {
        var dir = Path.Combine(Videos, "v");
        Directory.CreateDirectory(dir);
        JsonStore.Gravar(Path.Combine(dir, "v.json"), new VideoMeta { Slug = "v", Titulo = "V" });
        File.WriteAllBytes(Path.Combine(dir, "v.mp4.substituindo"), [1, 2, 3]); // o novo não chegou a ser movido
        var cat = new CatalogService(Videos, Path.Combine(_raiz, "c.json"), p => p);
        cat.LimparExclusoesPendentes();
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dir, "v.mp4")));
        Assert.False(File.Exists(Path.Combine(dir, "v.mp4.substituindo")));
    }
}
