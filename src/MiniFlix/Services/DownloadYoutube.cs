using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiniFlix.Models;

namespace MiniFlix.Services;

/// <summary>Baixa um vídeo do YouTube com yt-dlp e o adiciona ao acervo (etapa 5).</summary>
public sealed partial class DownloadYoutube(CatalogService catalogo)
{
    /// <summary>Altura máxima baixada: mantém arquivos de tamanho razoável (e abaixo de 4 GB em FAT32).</summary>
    public const int AlturaMaxima = 1080;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")] private static partial Regex IdValido();

    /// <summary>Extrai o ID de 11 caracteres de URLs do YouTube (watch, youtu.be, shorts, embed, live, m., music.).</summary>
    public static string? ExtrairId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        string? id = null;

        if (host == "youtu.be")
            id = uri.AbsolutePath.Trim('/').Split('/')[0];
        else if (host is "youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com")
        {
            var partes = uri.AbsolutePath.Trim('/').Split('/');
            if (partes[0] == "watch")
                id = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
            else if (partes.Length >= 2 && partes[0] is "shorts" or "embed" or "live" or "v")
                id = partes[1];
        }
        return id is not null && IdValido().IsMatch(id) ? id : null;
    }

    public static string UrlCanonica(string id) => $"https://www.youtube.com/watch?v={id}";

    // ---------- Vários links de uma vez ----------

    public const int MaxLinksPorVez = 500;

    // Qualquer coisa com cara de endereço: http(s)://… ou youtube.com/… / youtu.be/… sem o "https://".
    [GeneratedRegex(@"(?:https?://|(?:www\.|m\.|music\.)?youtube(?:-nocookie)?\.com/|youtu\.be/)[^\s<>""'，、]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex PareceLink();

    /// <summary>Um link encontrado no texto colado: o ID do vídeo, ou o motivo de não servir.</summary>
    public sealed record LinkColado(string Texto, string? Id, string? Motivo);

    /// <summary>
    /// Encontra os links num texto livre (um por linha, separados por espaço/vírgula ou no meio de uma
    /// mensagem) e extrai o ID de cada um, na ordem em que aparecem.
    /// </summary>
    public static IReadOnlyList<LinkColado> ExtrairLinks(string? texto)
    {
        var lista = new List<LinkColado>();
        foreach (Match m in PareceLink().Matches(texto ?? ""))
        {
            var link = m.Value.TrimEnd('.', ',', ';', ')', ']', '!', '?');
            var id = ExtrairId(link);
            string? motivo = null;
            if (id is null)
            {
                var minusculo = link.ToLowerInvariant();
                motivo = !minusculo.Contains("youtu") ? "Não é um link do YouTube."
                    : minusculo.Contains("list=") || minusculo.Contains("/playlist") ? "Playlist sem um vídeo específico (ainda não suportado)."
                    : minusculo.Contains("/@") || minusculo.Contains("/channel/") || minusculo.Contains("/c/") ? "Link de canal, não de um vídeo."
                    : "Não foi possível identificar o vídeo neste link.";
            }
            lista.Add(new LinkColado(link, id, motivo));
            if (lista.Count > MaxLinksPorVez)
                throw new ArgumentException($"São mais de {MaxLinksPorVez} links. Divida em partes menores.");
        }
        return lista;
    }

    /// <summary>Valida a URL e cria o item da fila. Lança ArgumentException com mensagem amigável.</summary>
    public FilaItem CriarItem(string url, string categoria, IReadOnlyList<string>? tags = null)
    {
        var id = ExtrairId(url) ?? throw new ArgumentException(
            "Link do YouTube inválido. Use o endereço de um vídeo, por exemplo https://www.youtube.com/watch?v=…");
        if (catalogo.ObterPorId("yt:" + id) is { } existente)
            throw new ArgumentException($"Este vídeo já está no catálogo: “{existente.Meta.Titulo}”.");

        return new FilaItem
        {
            Tipo = "youtube",
            Titulo = "Vídeo do YouTube",
            Categoria = categoria.Trim(),
            Origem = UrlCanonica(id),
            Executor = (item, relator, ct) => ExecutarAsync(item, id, tags ?? [], relator, ct),
        };
    }

    private async Task ExecutarAsync(FilaItem item, string id, IReadOnlyList<string> tags, Relator relator, CancellationToken ct)
    {
        var ytDlp = Ferramentas.Exigir(Ferramentas.YtDlp, "yt-dlp");
        var ffmpeg = Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
        var url = UrlCanonica(id);

        if (catalogo.ObterPorId("yt:" + id) is { } existente) // pode ter sido adicionado enquanto esperava na fila
            throw new InvalidOperationException($"Este vídeo já está no catálogo: “{existente.Meta.Titulo}”.");

        // 1. Metadados
        relator.Etapa("Obtendo informações do vídeo", 0, 0.05);
        var json = new StringBuilder();
        var r = await Processo.ExecutarAsync(ytDlp, [.. ArgumentosBase(ffmpeg), "--format", Formato, "--dump-single-json", "--skip-download", url],
            ct, aoLerSaida: l => json.AppendLine(l), ambiente: Ambiente());
        if (r.CodigoSaida != 0) throw new InvalidOperationException(TraduzirErro(r.UltimasLinhasErro));

        var meta = LerMetadados(json.ToString(), id);
        item.Titulo = meta.Titulo;
        relator.Progresso(1);

        var pasta = catalogo.CriarPastaTemporaria();
        try
        {
            // 2. Download (vídeo e áudio separados quando necessário; o yt-dlp junta com o ffmpeg)
            relator.Etapa("Baixando vídeo", 0.05, 0.85);
            int partes = Math.Max(1, meta.PartesDownload), parteAtual = 0;
            r = await Processo.ExecutarAsync(ytDlp,
                [.. ArgumentosBase(ffmpeg), "--format", Formato, "--merge-output-format", "mp4",
                 "--write-thumbnail", "--convert-thumbnails", "jpg",
                 "--newline", "--progress", "--progress-template",
                 "download:MFPROG %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s %(progress.speed)s %(progress.eta)s",
                 "--output", Path.Combine(pasta, "download.%(ext)s"), url],
                ct, ambiente: Ambiente(), aoLerSaida: linha =>
                {
                    if (linha.StartsWith("[download] Destination:") && !linha.EndsWith(".jpg") && !linha.EndsWith(".webp"))
                        parteAtual = Math.Min(parteAtual + 1, partes);
                    else if (linha.StartsWith("MFPROG ") && LerProgresso(linha) is { } p)
                    {
                        relator.Progresso((Math.Max(parteAtual, 1) - 1 + p.Fracao) / partes);
                        relator.Detalhe(p.Descricao(parteAtual, partes));
                    }
                    else if (linha.StartsWith("[Merger]"))
                        relator.Detalhe("Juntando vídeo e áudio…");
                });
            if (r.CodigoSaida != 0) throw new InvalidOperationException(TraduzirErro(r.UltimasLinhasErro));

            var baixado = Directory.GetFiles(pasta, "download.*")
                .FirstOrDefault(f => !f.EndsWith(".jpg") && !f.EndsWith(".webp") && !f.EndsWith(".part") && !f.EndsWith(".ytdl"))
                ?? throw new InvalidOperationException("O download terminou, mas o arquivo de vídeo não foi encontrado.");

            // 3. Garante um MP4 que o player toca, com faststart (normalmente só copia as trilhas)
            relator.Etapa("Finalizando MP4", 0.85, 0.96);
            var info = await Ffmpeg.AnalisarAsync(baixado, ct);
            var mp4 = Path.Combine(pasta, "video.mp4");
            // O download já vem em até 1080p; se precisar reconverter, mantém esse limite.
            await Ffmpeg.ConverterParaMp4Async(baixado, mp4, info, reduzirPara1080: true, relator.Progresso, ct);
            File.Delete(baixado);

            // 4. Capa: miniatura do YouTube ou, na falta dela, um quadro do vídeo
            relator.Etapa("Preparando capa", 0.96, 0.98);
            var capa = Path.Combine(pasta, "capa.jpg");
            var thumb = Path.Combine(pasta, "download.jpg");
            if (File.Exists(thumb)) File.Move(thumb, capa, overwrite: true);
            else await Ffmpeg.GerarCapaAsync(mp4, capa, info.DuracaoSegundos, ct);
            foreach (var resto in Directory.GetFiles(pasta, "download.*")) File.Delete(resto);

            // 5. Publicação
            relator.Etapa("Salvando no catálogo", 0.98, 1);
            ct.ThrowIfCancellationRequested();
            if (meta.Duracao <= 0) meta.Duracao = info.DuracaoSegundos;
            item.Slug = catalogo.Publicar(pasta, new VideoMeta
            {
                Id = "yt:" + id,
                Titulo = meta.Titulo,
                Descricao = meta.Descricao,
                Categoria = item.Categoria,
                Tags = catalogo.NormalizarTags(tags),
                Origem = "youtube",
                UrlOrigem = url,
                DuracaoSegundos = Math.Round(meta.Duracao, 2),
                DataPublicacao = meta.DataPublicacao,
                DataAdicionado = DateTime.Now,
            });
            item.Mensagem = info.Altura > 0 ? $"Baixado em {info.Altura}p." : "Baixado.";
        }
        catch
        {
            CatalogService.DescartarPastaTemporaria(pasta);
            throw;
        }
    }

    // Preferência: H.264 + AAC (sem reconversão) até 1080p; depois qualquer formato até 1080p (reconvertido).
    private static readonly string Formato =
        $"bv*[vcodec^=avc1][height<={AlturaMaxima}]+ba[acodec^=mp4a]/" +
        $"b[ext=mp4][vcodec^=avc1][height<={AlturaMaxima}]/" +
        $"bv*[height<={AlturaMaxima}]+ba/b[height<={AlturaMaxima}]/b";

    private static List<string> ArgumentosBase(string ffmpeg)
    {
        var args = new List<string>
        {
            "--no-playlist", "--no-warnings", "--no-mtime", "--no-cache-dir", "--ignore-config",
            "--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!,
        };
        if (Ferramentas.Deno is { } deno) args.AddRange(["--js-runtimes", $"deno:{deno}"]);
        return args;
    }

    // yt-dlp é Python empacotado: força UTF-8 para títulos com acentos/emoji chegarem intactos.
    private static Dictionary<string, string> Ambiente() => new() { ["PYTHONUTF8"] = "1", ["PYTHONIOENCODING"] = "utf-8" };

    internal sealed class Metadados
    {
        public string Titulo = "";
        public string Descricao = "";
        public double Duracao;
        public string? DataPublicacao;
        public int PartesDownload = 1;
    }

    internal static Metadados LerMetadados(string json, string id)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string? Texto(string p) => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var m = new Metadados
        {
            Titulo = (Texto("title") ?? "").Trim(),
            Descricao = (Texto("description") ?? "").Trim(),
            Duracao = r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
            PartesDownload = r.TryGetProperty("requested_formats", out var rf) && rf.ValueKind == JsonValueKind.Array ? rf.GetArrayLength() : 1,
        };
        if (m.Titulo.Length == 0) m.Titulo = "Vídeo do YouTube " + id;
        if (m.Titulo.Length > CatalogService.MaxTitulo) m.Titulo = m.Titulo[..CatalogService.MaxTitulo].Trim();
        if (m.Descricao.Length > CatalogService.MaxDescricao) m.Descricao = m.Descricao[..CatalogService.MaxDescricao];

        var data = Texto("upload_date"); // "20240131"
        if (data is { Length: 8 } && DateTime.TryParseExact(data, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            m.DataPublicacao = dt.ToString("yyyy-MM-dd");
        return m;
    }

    internal sealed record Progresso(double Fracao, double? Velocidade, double? Eta)
    {
        public string Descricao(int parte, int partes)
        {
            var sb = new StringBuilder();
            if (partes > 1) sb.Append(parte <= 1 ? "Vídeo" : "Áudio").Append(" · ");
            if (Velocidade is > 0) sb.Append(FormatarVelocidade(Velocidade.Value));
            if (Eta is > 0) sb.Append(" · ").Append(FormatarEta(Eta.Value)).Append(" restantes");
            return sb.ToString().Trim(' ', '·');
        }
    }

    /// <summary>Lê "MFPROG baixados total estimado velocidade eta" (campos podem vir como "NA").</summary>
    internal static Progresso? LerProgresso(string linha)
    {
        var p = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 6) return null;
        static double? N(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        var baixados = N(p[1]);
        var total = N(p[2]) ?? N(p[3]);
        if (baixados is null || total is not > 0) return null;
        return new Progresso(Math.Clamp(baixados.Value / total.Value, 0, 1), N(p[4]), N(p[5]));
    }

    private static string FormatarVelocidade(double bps) => bps >= 1024 * 1024
        ? $"{bps / 1024 / 1024:0.0} MB/s".Replace('.', ',')
        : $"{bps / 1024:0} KB/s";

    private static string FormatarEta(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Round(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Converte erros técnicos do yt-dlp em mensagens que o usuário entende.</summary>
    internal static string TraduzirErro(string saidaErro)
    {
        var e = saidaErro;
        bool Tem(params string[] trechos) => trechos.Any(t => e.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (Tem("getaddrinfo", "Failed to resolve", "Unable to download webpage", "No route to host",
                "Network is unreachable", "timed out", "Connection refused", "Errno 11001"))
            return "Sem conexão com a internet (ou o YouTube não respondeu). Verifique a rede e tente novamente.";
        if (Tem("Private video")) return "Este vídeo é privado.";
        if (Tem("confirm your age", "age-restricted", "inappropriate for some users"))
            return "Este vídeo tem restrição de idade e exige login no YouTube; não é possível baixá-lo.";
        if (Tem("not a bot", "Sign in to confirm"))
            return "O YouTube pediu uma verificação. Atualize o yt-dlp (botão na administração) e tente mais tarde.";
        if (Tem("Video unavailable", "This video is unavailable", "has been removed", "is not available"))
            return "Vídeo indisponível (removido, bloqueado na sua região ou inexistente).";
        if (Tem("live event will begin", "is live", "Premieres in"))
            return "Transmissões ao vivo e estreias ainda não iniciadas não podem ser baixadas.";
        if (Tem("Requested format is not available"))
            return "Nenhum formato de vídeo compatível foi encontrado para este vídeo.";
        if (Tem("No supported JavaScript runtime", "js-runtimes", "JavaScript runtime"))
            return "O yt-dlp precisa do deno.exe na pasta bin do MiniFlix para baixar do YouTube.";
        if (Tem("No space left")) return "Espaço insuficiente no disco.";

        Log.Erro($"yt-dlp: {e}");
        var ultima = e.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.Contains("ERROR")) ?? "";
        return "Falha no download. " + (ultima.Length > 0 ? ultima.Replace("ERROR:", "").Trim() : "Detalhes em data/logs/miniflix.log.");
    }

    /// <summary>Atualiza o yt-dlp.exe da pasta bin (yt-dlp -U).</summary>
    public static async Task<string> AtualizarAsync(CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.YtDlp, "yt-dlp");
        var saida = new StringBuilder();
        var r = await Processo.ExecutarAsync(exe, ["-U"], ct, aoLerSaida: l => saida.AppendLine(l), aoLerErro: l => saida.AppendLine(l), ambiente: Ambiente());
        var texto = saida.ToString();
        if (texto.Contains("is up to date", StringComparison.OrdinalIgnoreCase)) return "O yt-dlp já está na versão mais recente.";
        if (texto.Contains("Updated yt-dlp to", StringComparison.OrdinalIgnoreCase))
            return "yt-dlp atualizado: " + texto.Split('\n').First(l => l.Contains("Updated yt-dlp to", StringComparison.OrdinalIgnoreCase)).Trim();
        if (r.CodigoSaida != 0 || texto.Contains("ERROR"))
            throw new InvalidOperationException("Não foi possível atualizar o yt-dlp (sem internet, ou ele não está na pasta bin do MiniFlix).");
        return texto.Trim();
    }

    public static async Task<string?> VersaoAsync(CancellationToken ct)
    {
        if (Ferramentas.YtDlp is not { } exe) return null;
        var v = "";
        await Processo.ExecutarAsync(exe, ["--version"], ct, aoLerSaida: l => v = l.Trim(), ambiente: Ambiente());
        return v;
    }
}
