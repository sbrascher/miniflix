using MiniFlix.Services;

namespace MiniFlix.Tests;

/// <summary>
/// Regra "copiar ou converter" da importação. Reflete o que o WebView2 do MiniFlix toca de
/// verdade (testado no app, decodificando quadros e áudio): H.264 em 4:2:0/4:2:2/4:4:4 de 8 e
/// 10 bits, VP9/AV1 4:2:0, áudio AAC/MP3/Opus/FLAC. HEVC só toca o áudio.
/// </summary>
public class CompatibilidadeCodecsTests
{
    private static InfoMidia Info(string? video, string? pix, string? audio, string formato = "mov,mp4,m4a,3gp,3g2,mj2") =>
        new(60, formato, video, pix, audio, 1920, 1080);

    [Theory]
    [InlineData("h264", "yuv420p", "aac")]
    [InlineData("h264", "yuvj420p", "aac")]
    [InlineData("h264", "yuv420p10le", "aac")]
    [InlineData("h264", "yuv422p", "aac")]      // câmeras/gravações profissionais: tocam, não precisa converter
    [InlineData("h264", "yuv422p10le", "aac")]
    [InlineData("h264", "yuv444p", "aac")]      // gravações de tela
    [InlineData("h264", "yuv444p10le", "aac")]
    [InlineData("h264", "yuv420p", "mp3")]
    [InlineData("h264", "yuv420p", "opus")]
    [InlineData("h264", "yuv420p", "flac")]
    [InlineData("vp9", "yuv420p", "opus")]   // típico de downloads do YouTube
    [InlineData("av1", "yuv420p", "opus")]   // típico de downloads do YouTube
    [InlineData("av1", "yuv420p10le", "opus")]
    [InlineData("h264", "yuv420p", null)]    // vídeo sem áudio
    public void Copia_sem_converter_o_que_o_player_toca(string video, string pix, string? audio)
    {
        Assert.True(Info(video, pix, audio).CompativelSemConverter);
        Assert.Equal(Tratamento.CopiarTudo, Info(video, pix, audio).Tratamento());
    }

    [Theory]
    [InlineData("hevc", "yuv420p", "aac")]      // WebView2 toca só o som
    [InlineData("mpeg4", "yuv420p", "mp3")]     // AVI antigo (DivX/Xvid)
    [InlineData("wmv3", "yuv420p", "wmav2")]    // WMV
    [InlineData("prores", "yuv422p10le", "pcm_s16le")]
    [InlineData("vp9", "yuv444p", "opus")]      // VP9 4:4:4 não foi testado: converte por segurança
    public void Video_incompativel_e_reconvertido(string video, string pix, string audio) =>
        Assert.Equal(Tratamento.ConverterVideo, Info(video, pix, audio).Tratamento());

    [Theory]
    [InlineData("ac3")]        // tocou neste PC, mas depende de componentes do Windows
    [InlineData("eac3")]
    [InlineData("dts")]
    [InlineData("pcm_s16le")]
    [InlineData("wmav2")]
    public void So_o_audio_e_convertido_quando_o_video_e_compativel(string audio) =>
        Assert.Equal(Tratamento.ConverterSoAudio, Info("h264", "yuv420p", audio).Tratamento());

    [Theory]
    [InlineData("yuv420p", "yuv420p")]
    [InlineData("yuv420p10le", "yuv420p10le")]   // HDR/10 bits continua 10 bits
    [InlineData("yuv422p10le", "yuv422p10le")]   // ProRes 422 mantém a cor 4:2:2
    [InlineData("yuv444p", "yuv444p")]
    [InlineData("yuvj420p", "yuv420p")]
    public void Reconversao_preserva_cor_e_bits(string origem, string saida) =>
        Assert.Equal(saida, Ffmpeg.PixelDeSaida(Info("hevc", origem, "aac")));

    [Theory]
    [InlineData("matroska,webm")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2")]
    public void Contêiner_de_origem_nao_importa(string formato) =>
        Assert.True(Info("vp9", "yuv420p", "opus", formato).CompativelSemConverter);
}
