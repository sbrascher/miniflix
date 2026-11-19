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

    /// <summary>Identificador estável do arquivo: SHA-256 do primeiro 1 MB + tamanho.</summary>
    public static string CalcularId(string arquivo)
    {
        using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Math.Min(fs.Length, 1024 * 1024)];
        fs.ReadExactly(buffer);
        var hash = SHA256.HashData([.. buffer, .. BitConverter.GetBytes(fs.Length)]);
        return "local:" + Convert.ToHexStringLower(hash)[..16];
    }

    /// <param name="tags">Tags do vídeo (já validadas por <see cref="CatalogService.NormalizarTags"/>).</param>
    /// <param name="reduzirPara1080">Só para vídeos que precisam ser reconvertidos: limita a 1920×1080 (arquivo menor).</param>
    public FilaItem CriarItem(string arquivo, string titulo, string categoria, IReadOnlyList<string>? tags = null,
        bool reduzirPara1080 = false) => new()
    {
        Tipo = "local",
        Titulo = string.IsNullOrWhiteSpace(titulo) ? TituloSugerido(arquivo) : titulo.Trim(),
        Categoria = categoria.Trim(),
        Origem = Path.GetFileName(arquivo),
        Executor = (item, relator, ct) => ExecutarAsync(item, arquivo, tags ?? [], reduzirPara1080, relator, ct),
    };

    private async Task ExecutarAsync(FilaItem item, string arquivo, IReadOnlyList<string> tags, bool reduzirPara1080,
        Relator relator, CancellationToken ct)
    {
        // 1. Análise
        relator.Etapa("Analisando arquivo", 0, 0.03);
        if (!File.Exists(arquivo)) throw new FileNotFoundException("O arquivo de origem não foi encontrado (foi movido ou o pendrive foi removido?).");

        var id = CalcularId(arquivo);
        if (catalogo.ObterPorId(id) is { } existente)
            throw new InvalidOperationException($"Este vídeo já está no catálogo: “{existente.Meta.Titulo}”.");

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
