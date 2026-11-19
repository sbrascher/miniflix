using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MiniFlix.Models;

namespace MiniFlix.Services;

/// <summary>Importa um arquivo de vídeo do computador para o acervo (etapa 4).</summary>
public sealed partial class ImportacaoLocal(CatalogService catalogo)
{
    public static readonly string[] Extensoes =
        [".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".ts", ".3gp"];

    [GeneratedRegex(@"\s+")] private static partial Regex Espacos();

    /// <summary>"aula_01.intro-final.mp4" -> "aula 01 intro-final".</summary>
    public static string TituloSugerido(string arquivo)
    {
        var nome = Path.GetFileNameWithoutExtension(arquivo).Replace('_', ' ').Replace('.', ' ');
        nome = Espacos().Replace(nome, " ").Trim();
        return nome.Length == 0 ? "Vídeo" : nome;
    }

    /// <summary>Identificador do arquivo de origem (impressão digital atual; ver <see cref="ImpressaoDigital"/>).</summary>
    public static string CalcularId(string arquivo) => ImpressaoDigital.Calcular(arquivo).Atual;

    // ---------- Escolha e análise (antes de enfileirar) ----------

    public const int MaxArquivosPorVez = 2000;

    /// <summary>
    /// Lista os vídeos de uma pasta e de todas as subpastas (por extensão), em ordem de caminho.
    /// Pula pastas ocultas/do sistema, pastas sem permissão e a própria pasta de vídeos do MiniFlix.
    /// </summary>
    public IReadOnlyList<string> ListarVideosDaPasta(string pasta, params string[] ignorar)
    {
        var opcoes = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        var excluir = ignorar.Append(catalogo.PastaVideos).ToArray();
        var lista = Directory.EnumerateFiles(pasta, "*", opcoes)
            .Where(f => Extensoes.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !excluir.Any(e => PastaVideos.Dentro(f, e)))
            .Take(MaxArquivosPorVez + 1)
            .Order(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true))
            .ToList();
        if (lista.Count > MaxArquivosPorVez)
            throw new InvalidOperationException($"A pasta tem mais de {MaxArquivosPorVez} vídeos. Escolha uma subpasta.");
        return lista;
    }

    /// <summary>O que o modal precisa saber de cada arquivo escolhido.</summary>
    public sealed record ArquivoAnalisado(
        string Caminho,
        string? Subpasta,                 // nome da pasta do arquivo, se estiver numa subpasta da pasta escolhida
        string? CaminhoRelativo,          // para exibir no modo pasta
        long TamanhoBytes,
        ImpressaoDigital.Ids? Ids,
        InfoMidia? Info,
        VideoItem? Existente,             // já está no catálogo (mesmo arquivo)
        string? Erro);

    /// <summary>
    /// Analisa os arquivos (impressão digital + ffprobe) em paralelo, informando o progresso.
    /// <paramref name="pastaBase"/>: pasta escolhida no modo "Importar pasta" (para subpasta/caminho relativo).
    /// </summary>
    public async Task<IReadOnlyList<ArquivoAnalisado>> AnalisarAsync(IReadOnlyList<string> arquivos, string? pastaBase,
        Action<int, int> progresso, CancellationToken ct)
    {
        int feitos = 0;
        using var limite = new SemaphoreSlim(4);
        var tarefas = arquivos.Select(async caminho =>
        {
            await limite.WaitAsync(ct);
            try
            {
                string? subpasta = null, relativo = null;
                if (pastaBase is not null)
                {
                    relativo = Path.GetRelativePath(pastaBase, caminho);
                    var dir = Path.GetDirectoryName(caminho);
                    if (dir is not null && !PastaVideos.MesmoCaminho(dir, pastaBase)) subpasta = Path.GetFileName(dir);
                }
                // 1) Impressão digital: o "já está no catálogo" vale mesmo que a análise do vídeo falhe.
                long tamanho;
                ImpressaoDigital.Ids ids;
                try
                {
                    tamanho = new FileInfo(caminho).Length;
                    ids = await Task.Run(() => ImpressaoDigital.Calcular(caminho), ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Aviso($"Não foi possível ler {caminho}: {ex.Message}");
                    return new ArquivoAnalisado(caminho, subpasta, relativo, 0, null, null, null, "Não foi possível ler o arquivo.");
                }
                var existente = catalogo.ObterPorImpressao(ids);

                // 2) O que será preciso fazer (copiar ou converter).
                InfoMidia? info = null;
                string? erro = null;
                try
                {
                    info = await Ffmpeg.AnalisarAsync(caminho, ct);
                    if (!info.TemVideo) erro = "O arquivo não contém vídeo.";
                }
                catch (InvalidOperationException ex) { erro = ex.Message; }
                return new ArquivoAnalisado(caminho, subpasta, relativo, tamanho, ids, info, existente, erro);
            }
            finally
            {
                limite.Release();
                progresso(Interlocked.Increment(ref feitos), arquivos.Count);
            }
        });
        return await Task.WhenAll(tarefas);
    }

    // ---------- Fila ----------

    /// <param name="tags">Tags do vídeo (já validadas por <see cref="CatalogService.NormalizarTags"/>).</param>
    /// <param name="reduzirPara1080">Só para vídeos que precisam ser reconvertidos: limita a 1920×1080 (arquivo menor).</param>
    public FilaItem CriarItem(string arquivo, string titulo, string categoria, IReadOnlyList<string>? tags = null,
        bool reduzirPara1080 = false) => new()
    {
        Tipo = "local",
        Titulo = string.IsNullOrWhiteSpace(titulo) ? TituloSugerido(arquivo) : titulo.Trim(),
        Categoria = categoria.Trim(),
        Origem = Path.GetFileName(arquivo),
        Executor = (item, relator, ct) => ExecutarAsync(item, arquivo, tags ?? [], reduzirPara1080, null, relator, ct),
    };

    /// <summary>
    /// Reimporta <paramref name="arquivo"/> por cima do vídeo <paramref name="slug"/>: troca só o arquivo de
    /// vídeo e mantém título, categoria, tags, favorito, descrição, capa e o "continuar assistindo".
    /// </summary>
    public FilaItem CriarItemReimportacao(string arquivo, string slug, bool reduzirPara1080 = false)
    {
        var existente = catalogo.Obter(slug) ?? throw new KeyNotFoundException("Vídeo não encontrado no catálogo.");
        return new FilaItem
        {
            Tipo = "local",
            Titulo = existente.Meta.Titulo,
            Categoria = existente.Meta.Categoria,
            Origem = Path.GetFileName(arquivo),
            Executor = (item, relator, ct) => ExecutarAsync(item, arquivo, [], reduzirPara1080, slug, relator, ct),
        };
    }

    private async Task ExecutarAsync(FilaItem item, string arquivo, IReadOnlyList<string> tags, bool reduzirPara1080,
        string? reimportarSlug, Relator relator, CancellationToken ct)
    {
        // 1. Análise
        relator.Etapa("Analisando arquivo", 0, 0.03);
        if (!File.Exists(arquivo)) throw new FileNotFoundException("O arquivo de origem não foi encontrado (foi movido ou o pendrive foi removido?).");

        var ids = ImpressaoDigital.Calcular(arquivo);
        var id = ids.Atual;
        if (reimportarSlug is null && catalogo.ObterPorImpressao(ids) is { } existente)
            throw new InvalidOperationException($"Este vídeo já está no catálogo: “{existente.Meta.Titulo}”.");
        if (reimportarSlug is not null && catalogo.Obter(reimportarSlug) is null)
            throw new InvalidOperationException("O vídeo a ser reimportado não está mais no catálogo.");

        var info = await Ffmpeg.AnalisarAsync(arquivo, ct);
        if (!info.TemVideo) throw new InvalidOperationException("O arquivo não contém uma trilha de vídeo.");

        var tratamento = info.Tratamento();
        VerificarEspaco(new FileInfo(arquivo).Length, tratamento);

        var pasta = catalogo.CriarPastaTemporaria();
        try
        {
            // 2. Cópia (remux) ou conversão
            relator.Etapa(tratamento switch
            {
                Tratamento.ConverterVideo => $"Convertendo {info.NomeCodecVideo} em alta qualidade",
                Tratamento.ConverterSoAudio => "Copiando vídeo e convertendo o áudio",
                _ => "Copiando vídeo",
            }, 0.03, 0.95);
            var mp4 = Path.Combine(pasta, "video.mp4");
            var reduziu = await Ffmpeg.ConverterParaMp4Async(arquivo, mp4, info, reduzirPara1080, relator.Progresso, ct);

            // 3. Capa (falha aqui não impede a importação)
            relator.Etapa("Gerando capa", 0.95, 0.98);
            try { await Ffmpeg.GerarCapaAsync(mp4, Path.Combine(pasta, "capa.jpg"), info.DuracaoSegundos, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Aviso($"Capa não gerada para {item.Titulo}: {ex.Message}"); }

            // 4. Metadados + publicação
            relator.Etapa("Salvando no catálogo", 0.98, 1);
            ct.ThrowIfCancellationRequested();
            if (reimportarSlug is not null)
            {
                catalogo.SubstituirVideo(reimportarSlug, pasta, info.DuracaoSegundos);
                CatalogService.DescartarPastaTemporaria(pasta);
                item.Slug = reimportarSlug;
                item.Mensagem = "Reimportado (título, tags e capa mantidos). " + Mensagem(info, tratamento, reduziu);
                return;
            }
            item.Slug = catalogo.Publicar(pasta, new VideoMeta
            {
                Id = id,
                Titulo = item.Titulo,
                Categoria = item.Categoria,
                // Normaliza de novo na publicação: reaproveita a grafia de tags criadas enquanto o item esperava na fila.
                Tags = catalogo.NormalizarTags(tags),
                Origem = "local",
                UrlOrigem = null,
                DuracaoSegundos = Math.Round(info.DuracaoSegundos, 2),
                DataPublicacao = File.GetLastWriteTime(arquivo).ToString("yyyy-MM-dd"),
                DataAdicionado = DateTime.Now,
            });
            item.Mensagem = Mensagem(info, tratamento, reduziu);
        }
        catch
        {
            CatalogService.DescartarPastaTemporaria(pasta);
            throw;
        }
    }

    /// <summary>Explica no item da fila o que foi feito com o arquivo (e se houve alguma perda).</summary>
    public static string Mensagem(InfoMidia info, Tratamento tratamento, bool reduziu) => tratamento switch
    {
        Tratamento.CopiarTudo => "Copiado sem perda de qualidade.",
        Tratamento.ConverterSoAudio => "Vídeo copiado sem perda; áudio convertido para FLAC (sem perda).",
        _ => $"Convertido de {info.NomeCodecVideo} para H.264 em alta qualidade" +
             (reduziu ? ", reduzido para 1080p." : ", na resolução original."),
    };

    private void VerificarEspaco(long tamanhoOrigem, Tratamento tratamento)
    {
        bool converter = tratamento == Tratamento.ConverterVideo;
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(catalogo.PastaVideos))!);
        // Conversão costuma gerar arquivo menor ou parecido; 1,2x dá margem. Remux ≈ tamanho original.
        // Reconverter em alta qualidade (ex.: HEVC → H.264) pode até dobrar o tamanho; FLAC ocupa mais que AC3/AAC.
        var fator = tratamento switch { Tratamento.ConverterVideo => 2.0, Tratamento.ConverterSoAudio => 1.3, _ => 1.02 };
        var necessario = (long)(tamanhoOrigem * fator) + 50 * 1024 * 1024;
        if (drive.AvailableFreeSpace < necessario)
            throw new InvalidOperationException(
                $"Espaço insuficiente: são necessários cerca de {necessario / 1024 / 1024:N0} MB e há {drive.AvailableFreeSpace / 1024 / 1024:N0} MB livres.");

        if (!converter && tamanhoOrigem >= 4L * 1024 * 1024 * 1024 &&
            drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O vídeo tem mais de 4 GB e a unidade está em FAT32. Use um pendrive em exFAT ou NTFS.");
    }
}
