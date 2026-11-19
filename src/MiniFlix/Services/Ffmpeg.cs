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
    int Altura,
    int Canais = 0)
{
    public bool TemVideo => CodecVideo is not null;

    // O que o WebView2 do MiniFlix toca de verdade (testado no app, decodificando quadros e áudio):
    //   H.264 em 4:2:0, 4:2:2 e 4:4:4, 8 e 10 bits; VP9 e AV1 em 4:2:0 8/10 bits; áudio AAC, MP3, Opus e FLAC.
    //   HEVC/H.265 toca só o áudio, sem imagem — por isso precisa de conversão.
    //   AC3/E-AC3/PCM tocaram neste PC, mas dependem de componentes do Windows: são convertidos para FLAC (sem perda).
    private static readonly HashSet<string> PixelH264 =
        ["yuv420p", "yuvj420p", "yuv420p10le", "yuv422p", "yuvj422p", "yuv422p10le", "yuv444p", "yuvj444p", "yuv444p10le"];
    private static readonly HashSet<string> Pixel420 = ["yuv420p", "yuvj420p", "yuv420p10le"];
    private static readonly HashSet<string> AudioSuportado = ["aac", "mp3", "opus", "flac"];

    /// <summary>A trilha de vídeo pode ser copiada como está (sem reconverter, sem perda).</summary>
    public bool VideoCompativel => CodecVideo switch
    {
        "h264" => PixFmt is not null && PixelH264.Contains(PixFmt),
        "vp9" or "av1" => PixFmt is not null && Pixel420.Contains(PixFmt),
        _ => false,
    };

    /// <summary>A trilha de áudio (se houver) pode ser copiada como está.</summary>
    public bool AudioCompativel => CodecAudio is null || AudioSuportado.Contains(CodecAudio);

    /// <summary>
    /// O player já toca estas trilhas: basta copiá-las para um MP4 (remux), sem reconverter.
    /// Vale para qualquer contêiner de origem (MP4, MOV, MKV, WebM…): leva segundos e não perde qualidade.
    /// </summary>
    public bool CompativelSemConverter => VideoCompativel && AudioCompativel;

    public bool Bits10 => PixFmt?.Contains("10") == true || PixFmt?.Contains("12") == true;

    /// <summary>Nome amigável do codec de vídeo ("HEVC/H.265", "WMV"…), para explicar por que precisa converter.</summary>
    public string NomeCodecVideo => CodecVideo switch
    {
        null => "sem vídeo",
        "hevc" => "HEVC/H.265",
        "h264" => "H.264",
        "mpeg4" => "MPEG-4 (DivX/Xvid)",
        "msmpeg4v2" or "msmpeg4v3" or "wmv1" or "wmv2" or "wmv3" or "vc1" => "WMV/VC-1",
        "prores" => "ProRes",
        "mpeg2video" => "MPEG-2",
        "mpeg1video" => "MPEG-1",
        "vp8" => "VP8",
        "dnxhd" => "DNxHD",
        "mjpeg" => "Motion JPEG",
        var c => c.ToUpperInvariant(),
    };
}

/// <summary>Como a importação vai tratar um arquivo — mostrado antes de confirmar.</summary>
public enum Tratamento { CopiarTudo, ConverterSoAudio, ConverterVideo }

public static class TratamentoExt
{
    public static Tratamento Tratamento(this InfoMidia i) =>
        !i.VideoCompativel ? Services.Tratamento.ConverterVideo
        : !i.AudioCompativel ? Services.Tratamento.ConverterSoAudio
        : Services.Tratamento.CopiarTudo;
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
            video is { } v4 ? Inteiro(v4, "height") : 0,
            audio is { } a2 ? Inteiro(a2, "channels") : 0);
    }

    /// <summary>Qualidade da reconversão (só usada quando o vídeo não pode ser copiado). 18 ≈ visualmente sem perda.</summary>
    public const int CrfAltaQualidade = 18;

    /// <summary>Formato de pixel H.264 que preserva a amostragem de cor e os 10 bits da origem (o player toca todos).</summary>
    public static string PixelDeSaida(InfoMidia info)
    {
        var p = info.PixFmt ?? "";
        var croma = p.Contains("444") ? "yuv444p" : p.Contains("422") ? "yuv422p" : "yuv420p";
        return info.Bits10 ? croma + "10le" : croma;
    }

    /// <summary>
    /// Gera o MP4 que o player toca, perdendo o mínimo possível:
    ///   • vídeo compatível → copiado como está (sem perda);
    ///   • áudio incompatível → FLAC (sem perda), mantendo os canais;
    ///   • vídeo incompatível (HEVC, WMV, ProRes…) → H.264 de alta qualidade (CRF 18), na resolução original,
    ///     mantendo 10 bits e a amostragem de cor. <paramref name="reduzirPara1080"/> limita a 1920×1080.
    /// Retorna true se o vídeo foi reduzido. <paramref name="progresso"/> recebe valores de 0 a 1.
    /// </summary>
    public static async Task<bool> ConverterParaMp4Async(string origem, string destino, InfoMidia info, bool reduzirPara1080,
        Action<double> progresso, CancellationToken ct)
    {
        var exe = Ferramentas.Exigir(Ferramentas.Ffmpeg, "ffmpeg");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y", "-i", origem,
                                      "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn" };
        bool reduziu = false;
        if (info.VideoCompativel)
            args.AddRange(["-c:v", "copy"]);
        else
        {
            if (reduzirPara1080 && (info.Largura > 1920 || info.Altura > 1080))
            {
                // Só reduz, nunca amplia; dimensões pares.
                args.AddRange(["-vf", "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:flags=lanczos,scale=trunc(iw/2)*2:trunc(ih/2)*2"]);
                reduziu = true;
            }
            else
                args.AddRange(["-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2"]); // H.264 exige dimensões pares
            args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", CrfAltaQualidade.ToString(CultureInfo.InvariantCulture),
                           "-pix_fmt", PixelDeSaida(info)]);
        }
        args.AddRange(info.AudioCompativel ? ["-c:a", "copy"] : ["-c:a", "flac"]);
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
        return reduziu;
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
