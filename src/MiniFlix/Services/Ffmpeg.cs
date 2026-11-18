using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MiniFlix.Services;

public sealed record InfoMidia(
    double DuracaoSegundos,
    string Formato,
    string? CodecVideo,
    string? PixFmt,
    string? CodecAudio,
    int Largura,
    int Altura)
{
    public bool TemVideo => CodecVideo is not null;

    /// <summary>Já toca em qualquer navegador: basta remuxar (copiar sem reconverter).</summary>
    public bool CompativelSemConverter =>
        (Formato.Contains("mp4") || Formato.Contains("mov")) &&
        CodecVideo == "h264" &&
        PixFmt is "yuv420p" or "yuvj420p" &&
        (CodecAudio is null or "aac");
}

/// <summary>Operações com ffprobe/ffmpeg usadas pela importação e pelo download.</summary>
public static class Ffmpeg
{
    public static async Task<InfoMidia> AnalisarAsync(string arquivo, CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.Ffprobe, "ffprobe");
        var saida = new StringBuilder();
        var r = await Processo.ExecutarAsync(exe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", arquivo],
            ct, aoLerSaida: l => saida.AppendLine(l));
        if (r.CodigoSaida != 0)
            throw new InvalidOperationException("Não foi possível ler o arquivo. Ele pode não ser um vídeo ou estar corrompido.");

        using var doc = JsonDocument.Parse(saida.ToString());
        var raiz = doc.RootElement;
        string? Texto(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Inteiro(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        JsonElement? video = null, audio = null;
        if (raiz.TryGetProperty("streams", out var streams))
            foreach (var s in streams.EnumerateArray())
            {
                var tipo = Texto(s, "codec_type");
                // Ignora capas embutidas (attached_pic), que aparecem como "vídeo" de 1 quadro.
                bool capa = s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                if (tipo == "video" && video is null && !capa) video = s;
                if (tipo == "audio" && audio is null) audio = s;
            }

        var formato = raiz.TryGetProperty("format", out var f) ? f : default;
        double.TryParse(formato.ValueKind == JsonValueKind.Object ? Texto(formato, "duration") : null,
            NumberStyles.Float, CultureInfo.InvariantCulture, out var duracao);

        return new InfoMidia(
            duracao,
            formato.ValueKind == JsonValueKind.Object ? Texto(formato, "format_name") ?? "" : "",
            video is { } v ? Texto(v, "codec_name") : null,
            video is { } v2 ? Texto(v2, "pix_fmt") : null,
            audio is { } a ? Texto(a, "codec_name") : null,
            video is { } v3 ? Inteiro(v3, "width") : 0,
            video is { } v4 ? Inteiro(v4, "height") : 0);
    }

    /// <summary>
    /// Gera um MP4 H.264/AAC com faststart. Se a origem já for compatível, só copia as trilhas (rápido).
    /// <paramref name="progresso"/> recebe valores de 0 a 1.
    /// </summary>
    public static async Task ConverterParaMp4Async(string origem, string destino, InfoMidia info, bool converter,
        Action<double> progresso, CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y", "-i", origem,
                                      "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn" };
        if (converter)
            args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", "22", "-pix_fmt", "yuv420p",
                           "-c:a", "aac", "-b:a", "160k", "-ac", "2"]);
        else
            args.AddRange(["-c", "copy"]);
        args.AddRange(["-movflags", "+faststart", destino]);

        var r = await Processo.ExecutarAsync(exe, args, ct, aoLerSaida: linha =>
        {
            // -progress escreve "out_time_us=123456" periodicamente
            if (info.DuracaoSegundos > 0 && linha.StartsWith("out_time_us=") &&
                long.TryParse(linha.AsSpan(12), out var us) && us > 0)
                progresso(Math.Clamp(us / 1_000_000.0 / info.DuracaoSegundos, 0, 1));
        });

        if (r.CodigoSaida != 0)
        {
            var detalhe = r.UltimasLinhasErro;
            if (detalhe.Contains("No space left", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Espaço insuficiente no disco.");
            if (detalhe.Contains("File too large", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O arquivo final passa de 4 GB e a unidade está em FAT32. Use um pendrive em exFAT ou NTFS.");
            Log.Erro($"ffmpeg falhou ({r.CodigoSaida}): {detalhe}");
            throw new InvalidOperationException("Falha ao converter o vídeo (detalhes em data/logs/miniflix.log).");
        }
        progresso(1);
    }

    /// <summary>Largura padrão das capas (todas iguais: catálogo uniforme e leve no pendrive).</summary>
    public const int LarguraCapa = 640;

    /// <summary>Extrai um quadro (em 10% da duração, no máximo 60 s) como capa padrão.</summary>
    public static Task<bool> GerarCapaAsync(string video, string destinoJpg, double duracao, CancellationToken ct) =>
        GerarCapaNoInstanteAsync(video, destinoJpg, duracao > 0 ? Math.Min(duracao * 0.1, 60) : 1, ct);

    /// <summary>Extrai o quadro do instante indicado (segundos) como capa JPG de 640 px de largura.</summary>
    public static async Task<bool> GerarCapaNoInstanteAsync(string video, string destinoJpg, double segundos, CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
        var r = await Processo.ExecutarAsync(exe,
            ["-hide_banner", "-loglevel", "error", "-y", "-ss", Math.Max(segundos, 0).ToString("0.###", CultureInfo.InvariantCulture),
             "-i", video, "-frames:v", "1", "-vf", $"scale={LarguraCapa}:-2", "-q:v", "3", destinoJpg], ct);
        return r.CodigoSaida == 0 && File.Exists(destinoJpg);
    }

    /// <summary>
    /// Converte uma imagem qualquer (JPG, PNG, WebP, BMP, GIF…) em capa no padrão do app:
    /// JPG com até 640 px de largura (imagens menores não são ampliadas).
    /// </summary>
    public static async Task ConverterImagemParaCapaAsync(string imagem, string destinoJpg, CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
        var r = await Processo.ExecutarAsync(exe,
            ["-hide_banner", "-loglevel", "error", "-y", "-i", imagem, "-frames:v", "1",
             "-vf", $"scale=w=min({LarguraCapa}\\,iw):h=-2:flags=lanczos,format=yuvj420p", "-q:v", "3", destinoJpg], ct);
        if (r.CodigoSaida != 0 || !File.Exists(destinoJpg))
        {
            Log.Erro($"Falha ao converter imagem de capa ({r.CodigoSaida}): {r.UltimasLinhasErro}");
            throw new InvalidOperationException("Não foi possível usar essa imagem. Tente um arquivo JPG ou PNG.");
        }
    }
}
