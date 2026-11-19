using MiniFlix.Models;
using MiniFlix.Services;

namespace MiniFlix.Tests;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "miniflix-testes", Guid.NewGuid().ToString("N"));
    private string Videos => Path.Combine(_raiz, "videos");
    private string Cache => Path.Combine(_raiz, "data", "catalog-cache.json");

    public CatalogServiceTests() => Directory.CreateDirectory(Videos);
    public void Dispose() { try { Directory.Delete(_raiz, recursive: true); } catch { } }

    private CatalogService Novo() => new(Videos, Cache, p => "url:" + Path.GetFileName(p));

    private void CriarVideo(string slug, string titulo = "T", string categoria = "Cursos",
        bool mp4 = true, bool capa = true, bool json = true, DateTime? adicionado = null, string? slugNoJson = null)
    {
        var dir = Path.Combine(Videos, slug);
        Directory.CreateDirectory(dir);
        if (mp4) File.WriteAllBytes(Path.Combine(dir, slug + ".mp4"), new byte[123]);
        if (capa) File.WriteAllBytes(Path.Combine(dir, slug + ".jpg"), new byte[10]);
        if (json)
            JsonStore.Gravar(Path.Combine(dir, slug + ".json"), new VideoMeta
            {
                Id = "local:" + slug, Slug = slugNoJson ?? slug, Titulo = titulo, Categoria = categoria,
                DataAdicionado = adicionado ?? DateTime.Now,
            });
    }

    [Fact]
    public void Varrer_le_videos_validos_e_monta_urls()
    {
        CriarVideo("aula-01", "Aula 1");
        var cat = Novo();
        Assert.True(cat.Varrer());

        var item = Assert.Single(cat.Itens);
        Assert.Equal("Aula 1", item.Meta.Titulo);
        Assert.Equal("url:aula-01.mp4", item.VideoUrl);
        Assert.StartsWith("url:aula-01.jpg?v=", item.CapaUrl);
        Assert.Equal(123, item.TamanhoBytes);
    }

    [Fact]
    public void Varrer_ignora_pastas_incompletas_ou_invalidas()
    {
        CriarVideo("ok");
        CriarVideo("sem-mp4", mp4: false);
        CriarVideo("sem-json", json: false);           // download em andamento
        CriarVideo("Nome Invalido");                   // fora do padrão de slug
        var corrompido = Path.Combine(Videos, "corrompido");
        Directory.CreateDirectory(corrompido);
        File.WriteAllText(Path.Combine(corrompido, "corrompido.json"), "{ isto não é json");
        File.WriteAllBytes(Path.Combine(corrompido, "corrompido.mp4"), [1]);

        var cat = Novo();
        cat.Varrer();
        Assert.Equal(["ok"], cat.Itens.Select(i => i.Meta.Slug));
    }

    [Fact]
    public void Capa_ausente_resulta_em_url_nula()
    {
        CriarVideo("sem-capa", capa: false);
        var cat = Novo();
        cat.Varrer();
        Assert.Null(cat.Itens[0].CapaUrl);
    }

    [Fact]
    public void Nome_da_pasta_prevalece_sobre_slug_do_json()
    {
        CriarVideo("pasta-certa", slugNoJson: "outro-slug");
        var cat = Novo();
        cat.Varrer();
        Assert.Equal("pasta-certa", cat.Itens[0].Meta.Slug);
    }

    [Fact]
    public void Categorias_ordenadas_com_sem_categoria_por_ultimo()
    {
        CriarVideo("a", categoria: "Música");
        CriarVideo("b", categoria: "");
        CriarVideo("c", categoria: "Documentários");
        CriarVideo("d", categoria: "cursos");
        CriarVideo("e", categoria: "Ária");
        var cat = Novo();
        cat.Varrer();
        Assert.Equal(["Ária", "cursos", "Documentários", "Música", CatalogService.SemCategoria], cat.Categorias());
    }

    [Fact]
    public void Itens_ordenados_do_mais_recente_para_o_mais_antigo()
    {
        CriarVideo("antigo", adicionado: new DateTime(2025, 1, 1));
        CriarVideo("novo", adicionado: new DateTime(2026, 1, 1));
        var cat = Novo();
        cat.Varrer();
        Assert.Equal(["novo", "antigo"], cat.Itens.Select(i => i.Meta.Slug));
    }

    [Fact]
    public void Segunda_varredura_sem_mudancas_nao_dispara_evento()
    {
        CriarVideo("x");
        var cat = Novo();
        int eventos = 0;
        cat.Alterado += () => eventos++;
        Assert.True(cat.Varrer());
        Assert.False(cat.Varrer());
        Assert.Equal(1, eventos);
    }

    [Fact]
    public void Varredura_detecta_pasta_apagada_manualmente()
    {
        CriarVideo("fica");
        CriarVideo("sai");
        var cat = Novo();
        cat.Varrer();
        Directory.Delete(Path.Combine(Videos, "sai"), recursive: true);
        Assert.True(cat.Varrer());
        Assert.Equal(["fica"], cat.Itens.Select(i => i.Meta.Slug));
    }

    [Fact]
    public void Cache_e_gravado_e_reaproveitado_so_para_videos_existentes()
    {
        CriarVideo("um");
        CriarVideo("dois");
        Novo().Varrer();
        Assert.True(File.Exists(Cache));

        // Simula um vídeo apagado com o app fechado: o cache ainda o lista, mas não pode aparecer.
        Directory.Delete(Path.Combine(Videos, "dois"), recursive: true);
        var cat = Novo();
        cat.Iniciar();
        Assert.DoesNotContain(cat.Itens, i => i.Meta.Slug == "dois");
        Assert.Contains(cat.Itens, i => i.Meta.Slug == "um");
    }

    // ---------- Edição ----------

    [Fact]
    public void Editar_altera_json_sem_mudar_slug()
    {
        CriarVideo("aula-01", "Antigo", "Cursos");
        var cat = Novo();
        cat.Varrer();

        var item = cat.Editar("aula-01", "  Título Novo  ", "Documentários", "Desc\nlinha 2");
        Assert.Equal("aula-01", item.Meta.Slug);
        Assert.Equal("Título Novo", item.Meta.Titulo);
        Assert.Equal("Documentários", item.Meta.Categoria);

        var noDisco = JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "aula-01", "aula-01.json"))!;
        Assert.Equal("Título Novo", noDisco.Titulo);
        Assert.Equal("Desc\nlinha 2", noDisco.Descricao);
        Assert.True(Directory.Exists(Path.Combine(Videos, "aula-01")));
    }

    [Fact]
    public void Editar_reaproveita_grafia_de_categoria_existente()
    {
        CriarVideo("a", categoria: "Cursos");
        CriarVideo("b", categoria: "Outra");
        var cat = Novo();
        cat.Varrer();
        Assert.Equal("Cursos", cat.Editar("b", "B", "cursos", "").Meta.Categoria);
    }

    [Fact]
    public void Editar_sem_categoria_grava_vazio()
    {
        CriarVideo("a", categoria: "Cursos");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("a", "A", "Sem categoria", "");
        Assert.Equal("", JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "a", "a.json"))!.Categoria);
        Assert.Equal(CatalogService.SemCategoria, cat.Itens[0].Meta.Categoria);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void Editar_rejeita_titulo_vazio(string titulo)
    {
        CriarVideo("a");
        var cat = Novo();
        cat.Varrer();
        Assert.Throws<ArgumentException>(() => cat.Editar("a", titulo, "X", ""));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\..\\windows")]
    [InlineData("nao-existe")]
    public void Editar_e_excluir_rejeitam_slugs_invalidos_ou_inexistentes(string slug)
    {
        var cat = Novo();
        Assert.ThrowsAny<Exception>(() => cat.Editar(slug, "T", "", ""));
        Assert.ThrowsAnyAsync<Exception>(() => cat.ExcluirAsync(slug)).GetAwaiter().GetResult();
    }

    // ---------- Exclusão ----------

    [Fact]
    public async Task Excluir_apaga_pasta_e_dispara_evento()
    {
        CriarVideo("fica");
        CriarVideo("sai");
        var cat = Novo();
        cat.Varrer();
        int eventos = 0;
        cat.Alterado += () => eventos++;

        await cat.ExcluirAsync("sai");
        Assert.False(Directory.Exists(Path.Combine(Videos, "sai")));
        Assert.Equal(["fica"], cat.Itens.Select(i => i.Meta.Slug));
        Assert.Equal(1, eventos);
        Assert.Empty(Directory.GetDirectories(Videos, "_excluindo-*"));
    }

    [Fact]
    public async Task Excluir_falha_sem_apagar_nada_se_arquivo_estiver_em_uso()
    {
        CriarVideo("em-uso");
        var cat = Novo();
        cat.Varrer();

        var mp4 = Path.Combine(Videos, "em-uso", "em-uso.mp4");
        using (new FileStream(mp4, FileMode.Open, FileAccess.Read, FileShare.Read)) // simula o player
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => cat.ExcluirAsync("em-uso"));
            Assert.Contains("em uso", ex.Message);
        }
        // Tudo intacto: json, mp4 e catálogo.
        Assert.True(File.Exists(Path.Combine(Videos, "em-uso", "em-uso.json")));
        Assert.True(File.Exists(mp4));
        Assert.Single(cat.Itens);
    }

    [Fact]
    public void Limpa_restos_de_exclusao_interrompida()
    {
        var resto = Path.Combine(Videos, "_excluindo-abc-123");
        Directory.CreateDirectory(resto);
        File.WriteAllText(Path.Combine(resto, "x.mp4"), "x");
        Novo().LimparExclusoesPendentes();
        Assert.False(Directory.Exists(resto));
    }

    // ---------- Troca de capa ----------

    [Fact]
    public void Confirmar_capa_substitui_jpg_e_muda_a_url()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        var urlAntes = cat.Itens[0].CapaUrl;

        var previa = cat.CaminhoPreviaCapa("v");
        File.WriteAllBytes(previa, [9, 9, 9]);
        File.SetLastWriteTimeUtc(previa, DateTime.UtcNow.AddMinutes(1)); // garante data diferente
        Assert.StartsWith("url:v.previa.jpg?v=", cat.UrlPreviaCapa("v"));

        var item = cat.ConfirmarCapa("v");
        Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(Videos, "v", "v.jpg")));
        Assert.False(File.Exists(previa));
        Assert.NotEqual(urlAntes, item.CapaUrl); // o navegador não reaproveita a imagem antiga
    }

    [Fact]
    public void Confirmar_sem_previa_falha_e_mantem_capa()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        Assert.Throws<InvalidOperationException>(() => cat.ConfirmarCapa("v"));
        Assert.True(File.Exists(Path.Combine(Videos, "v", "v.jpg")));
    }

    [Fact]
    public void Descartar_previa_mantem_capa_original()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        File.WriteAllBytes(cat.CaminhoPreviaCapa("v"), [1]);
        cat.DescartarPreviaCapa("v");
        Assert.False(File.Exists(cat.CaminhoPreviaCapa("v")));
        Assert.Equal(10, new FileInfo(Path.Combine(Videos, "v", "v.jpg")).Length);
    }

    [Fact]
    public void Previa_e_ignorada_pela_varredura_e_limpa_no_inicio()
    {
        CriarVideo("v", capa: false);
        var cat = Novo();
        File.WriteAllBytes(cat.CaminhoPreviaCapa("v"), [1]);
        cat.Varrer();
        Assert.Null(cat.Itens[0].CapaUrl); // prévia não vira capa sozinha
        cat.LimparExclusoesPendentes();
        Assert.False(File.Exists(cat.CaminhoPreviaCapa("v")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("nao-existe")]
    public void Capa_rejeita_slug_invalido(string slug)
    {
        var cat = Novo();
        Assert.ThrowsAny<Exception>(() => cat.CaminhoPreviaCapa(slug));
    }

    [Fact]
    public void JsonStore_gravacao_atomica_nao_deixa_tmp()
    {
        var arq = Path.Combine(_raiz, "data", "x.json");
        JsonStore.Gravar(arq, new Settings { Tema = "claro" });
        JsonStore.Gravar(arq, new Settings { Tema = "escuro" });
        Assert.False(File.Exists(arq + ".tmp"));
        Assert.Equal("escuro", JsonStore.Ler<Settings>(arq)!.Tema);
        Assert.Contains("\"tema\"", File.ReadAllText(arq)); // camelCase, como no plano
    }

    // ---------- Tags ----------

    [Fact]
    public void Editar_grava_tags_limpas_e_sem_repeticao()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        var item = cat.Editar("v", "T", "", "", ["  Python ", "python", "", "aula   ao vivo", null]);
        Assert.Equal(["Python", "aula ao vivo"], item.Meta.Tags);
        Assert.Equal(["Python", "aula ao vivo"], JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "v", "v.json"))!.Tags);
    }

    [Fact]
    public void Editar_sem_tags_mantem_as_existentes_e_reaproveita_grafia()
    {
        CriarVideo("a");
        CriarVideo("b");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("a", "A", "", "", ["Python", "Básico"]);
        Assert.Equal(["Python", "Básico"], cat.Editar("a", "A2", "", "").Meta.Tags); // null = manter

        Assert.Equal(["Python"], cat.Editar("b", "B", "", "", ["PYTHON"]).Meta.Tags);
        Assert.Equal(["Básico", "Python"], cat.Tags());
    }

    [Fact]
    public void Tags_invalidas_sao_rejeitadas()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        Assert.Throws<ArgumentException>(() => cat.Editar("v", "T", "", "", [new string('x', CatalogService.MaxTag + 1)]));
        Assert.Throws<ArgumentException>(() =>
            cat.Editar("v", "T", "", "", Enumerable.Range(1, CatalogService.MaxTags + 1).Select(i => $"t{i}")));
    }

    [Fact]
    public void Json_antigo_sem_tags_vira_lista_vazia()
    {
        var dir = Path.Combine(Videos, "velho");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "velho.mp4"), [1]);
        File.WriteAllText(Path.Combine(dir, "velho.json"), """{ "slug": "velho", "titulo": "Velho", "tags": null }""");
        var cat = Novo();
        cat.Varrer();
        Assert.Empty(Assert.Single(cat.Itens).Meta.Tags);
    }

    // ---------- Favorito ----------

    [Fact]
    public void Favorito_e_gravado_e_preserva_os_outros_campos()
    {
        CriarVideo("v", titulo: "Meu vídeo");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("v", "Meu vídeo", "Cursos", "desc", ["python"]);

        Assert.True(cat.DefinirFavorito("v", true).Meta.Favorito);
        var meta = JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "v", "v.json"))!;
        Assert.True(meta.Favorito);
        Assert.Equal(["python"], meta.Tags);
        Assert.Equal("desc", meta.Descricao);

        // editar o vídeo não tira o favorito
        Assert.True(cat.Editar("v", "Outro título", "Cursos", "", null).Meta.Favorito);
        Assert.False(cat.DefinirFavorito("v", false).Meta.Favorito);
        Assert.False(cat.DefinirFavorito("v", false).Meta.Favorito); // repetir não falha
        Assert.ThrowsAny<Exception>(() => cat.DefinirFavorito("nao-existe", true));
    }

    [Fact]
    public async Task Edicoes_simultaneas_no_mesmo_video_nao_se_perdem()
    {
        CriarVideo("v");
        var cat = Novo();
        cat.Varrer();
        // A ponte atende comandos em paralelo: favoritar e aplicar tags ao mesmo tempo não pode perder nada.
        var tarefas = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            var n = i;
            tarefas.Add(Task.Run(() => cat.AlterarTagsEmMassa(["v"], [$"t{n}"], [])));
            tarefas.Add(Task.Run(() => cat.DefinirFavorito("v", true)));
        }
        await Task.WhenAll(tarefas);
        var meta = JsonStore.Ler<VideoMeta>(Path.Combine(Videos, "v", "v.json"))!;
        Assert.True(meta.Favorito);
        Assert.Equal(20, meta.Tags.Count);
    }

    // ---------- Tags em massa ----------

    private List<string> TagsNoDisco(string slug) =>
        JsonStore.Ler<VideoMeta>(Path.Combine(Videos, slug, slug + ".json"))!.Tags;

    [Fact]
    public void Tags_em_massa_adiciona_sem_duplicar_e_mantem_as_existentes()
    {
        CriarVideo("a");
        CriarVideo("b");
        CriarVideo("c");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("a", "A", "", "", ["Python"]);

        Assert.Equal(2, cat.AlterarTagsEmMassa(["a", "b"], ["python", "iniciante"], []));
        Assert.Equal(["Python", "iniciante"], TagsNoDisco("a")); // "python" reaproveita a grafia "Python"
        Assert.Equal(["Python", "iniciante"], TagsNoDisco("b"));
        Assert.Empty(TagsNoDisco("c"));
        Assert.Equal(0, cat.AlterarTagsEmMassa(["a", "b"], ["iniciante"], [])); // nada muda
    }

    [Fact]
    public void Tags_em_massa_remove_sem_diferenciar_maiusculas()
    {
        CriarVideo("a");
        CriarVideo("b");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("a", "A", "", "", ["Python", "iniciante"]);
        cat.Editar("b", "B", "", "", ["iniciante"]);

        Assert.Equal(2, cat.AlterarTagsEmMassa(["a", "b"], [], ["INICIANTE"]));
        Assert.Equal(["Python"], TagsNoDisco("a"));
        Assert.Empty(TagsNoDisco("b"));
        Assert.Equal(["Python"], cat.Tags());
    }

    [Fact]
    public void Tags_em_massa_valida_tudo_antes_de_gravar()
    {
        CriarVideo("a");
        CriarVideo("cheio");
        var cat = Novo();
        cat.Varrer();
        cat.Editar("cheio", "Cheio", "", "", Enumerable.Range(1, CatalogService.MaxTags).Select(i => $"t{i}"));

        Assert.Throws<ArgumentException>(() => cat.AlterarTagsEmMassa(["a", "cheio"], ["nova"], []));
        Assert.Empty(TagsNoDisco("a")); // nenhum vídeo foi alterado
        Assert.Throws<ArgumentException>(() => cat.AlterarTagsEmMassa(["a"], [" "], []));
        Assert.Throws<ArgumentException>(() => cat.AlterarTagsEmMassa([], ["x"], []));
        Assert.ThrowsAny<Exception>(() => cat.AlterarTagsEmMassa([".."], ["x"], []));
    }

    // ---------- Renomear categoria ----------

    private string CategoriaNoDisco(string slug) =>
        JsonStore.Ler<VideoMeta>(Path.Combine(Videos, slug, slug + ".json"))!.Categoria;

    [Fact]
    public void RenomearCategoria_muda_todos_os_videos_dela()
    {
        CriarVideo("a", categoria: "Cursos");
        CriarVideo("b", categoria: "cursos");   // mesma categoria com outra grafia
        CriarVideo("c", categoria: "Receitas");
        var cat = Novo();
        cat.Varrer();

        Assert.Equal(2, cat.RenomearCategoria("Cursos", "  Aulas  "));
        Assert.Equal("Aulas", CategoriaNoDisco("a"));
        Assert.Equal("Aulas", CategoriaNoDisco("b"));
        Assert.Equal("Receitas", CategoriaNoDisco("c"));
        Assert.Equal(["Aulas", "Receitas"], cat.Categorias());
    }

    [Fact]
    public void RenomearCategoria_para_existente_une_com_a_grafia_dela()
    {
        CriarVideo("a", categoria: "Cursos");
        CriarVideo("b", categoria: "Aulas");
        var cat = Novo();
        cat.Varrer();
        Assert.Equal(1, cat.RenomearCategoria("Cursos", "AULAS"));
        Assert.Equal("Aulas", CategoriaNoDisco("a"));
        Assert.Equal(["Aulas"], cat.Categorias());
    }

    [Fact]
    public void RenomearCategoria_so_maiusculas_e_sem_categoria()
    {
        CriarVideo("a", categoria: "cursos");
        CriarVideo("b", categoria: "");
        var cat = Novo();
        cat.Varrer();

        Assert.Equal(1, cat.RenomearCategoria("cursos", "Cursos"));
        Assert.Equal("Cursos", CategoriaNoDisco("a"));

        // dar categoria aos vídeos sem categoria, e depois tirar de volta
        Assert.Equal(1, cat.RenomearCategoria(CatalogService.SemCategoria, "Diversos"));
        Assert.Equal("Diversos", CategoriaNoDisco("b"));
        Assert.Equal(1, cat.RenomearCategoria("Diversos", ""));
        Assert.Equal("", CategoriaNoDisco("b")); // grava vazio, não o texto "Sem categoria"
        Assert.Equal(0, cat.RenomearCategoria("Cursos", "Cursos"));
    }

    [Fact]
    public void RenomearCategoria_invalida()
    {
        CriarVideo("a", categoria: "Cursos");
        var cat = Novo();
        cat.Varrer();
        Assert.Throws<KeyNotFoundException>(() => cat.RenomearCategoria("Não existe", "X"));
        Assert.Throws<ArgumentException>(() => cat.RenomearCategoria("Cursos", new string('x', CatalogService.MaxCategoria + 1)));
        Assert.Equal("Cursos", CategoriaNoDisco("a"));
    }

    // ---------- Pasta configurável ----------

    [Fact]
    public void TrocarPasta_le_o_acervo_da_nova_pasta()
    {
        CriarVideo("aqui");
        var outra = Path.Combine(_raiz, "outra");
        Directory.CreateDirectory(Path.Combine(outra, "la"));
        File.WriteAllBytes(Path.Combine(outra, "la", "la.mp4"), [1]);
        JsonStore.Gravar(Path.Combine(outra, "la", "la.json"), new VideoMeta { Slug = "la", Titulo = "Lá" });

        var cat = Novo();
        cat.Varrer();
        Assert.Equal("aqui", Assert.Single(cat.Itens).Meta.Slug);

        cat.TrocarPasta(outra, criarSeFaltar: false);
        Assert.Equal("la", Assert.Single(cat.Itens).Meta.Slug);
        Assert.Equal(outra, cat.PastaVideos);
    }

    [Fact]
    public void Pasta_escolhida_ausente_nao_e_criada_e_catalogo_fica_vazio()
    {
        CriarVideo("v");
        var ausente = Path.Combine(_raiz, "hd-desconectado", "Filmes");
        var cat = new CatalogService(ausente, Cache, p => p, criarSeFaltar: false);
        cat.Varrer();
        Assert.Empty(cat.Itens);
        Assert.False(cat.PastaDisponivel);
        Assert.False(Directory.Exists(ausente));
    }

    [Fact]
    public void Limpeza_nao_apaga_arquivos_previa_fora_das_pastas_de_video()
    {
        var alheio = Path.Combine(Videos, "Minhas Fotos", "sub", "foto.previa.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(alheio)!);
        File.WriteAllBytes(alheio, [1]);
        Novo().LimparExclusoesPendentes();
        Assert.True(File.Exists(alheio));
    }
}
