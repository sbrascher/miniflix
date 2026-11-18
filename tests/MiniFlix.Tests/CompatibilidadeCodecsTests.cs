using MiniFlix.Services;

namespace MiniFlix.Tests;

/// <summary>
/// Regra "copiar ou converter" da importação. Reflete o que o WebView2 do MiniFlix toca de
/// verdade (testado no app): H.264/VP9/AV1 com AAC/MP3/Opus tocam; HEVC só toca o áudio.
/// </summary>
public class CompatibilidadeCodecsTests
{
    private static InfoMidia Info(string? video, string? pix, string? audio, string formato = "mov,mp4,m4a,3gp,3g2,mj2") =>
        new(60, formato, video, pix, audio, 1920, 1080);

    [Theory]
    [InlineData("h264", "yuv420p", "aac")]
    [InlineData("h264", "yuvj420p", "aac")]
    [InlineData("h264", "yuv420p10le", "aac")]
    [InlineData("h264", "yuv420p", "mp3")]
    [InlineData("h264", "yuv420p", "opus")]
    [InlineData("vp9", "yuv420p", "opus")]   // típico de downloads do YouTube
    [InlineData("av1", "yuv420p", "opus")]   // típico de downloads do YouTube
    [InlineData("av1", "yuv420p10le", "opus")]
    [InlineData("h264", "yuv420p", null)]    // vídeo sem áudio
    public void Copia_sem_converter_o_que_o_player_toca(string video, string pix, string? audio) =>
        Assert.True(Info(video, pix, audio).CompativelSemConverter);

    [Theory]
    [InlineData("hevc", "yuv420p", "aac")]      // WebView2 toca só o som
    [InlineData("mpeg4", "yuv420p", "mp3")]     // AVI antigo (DivX/Xvid)
    [InlineData("wmv3", "yuv420p", "wmav2")]    // WMV
    [InlineData("h264", "yuv422p", "aac")]      // amostragem de cor não suportada
    [InlineData("h264", "yuv420p", "ac3")]      // áudio Dolby
    [InlineData("h264", "yuv420p", "flac")]
    public void Converte_o_resto(string video, string pix, string audio) =>
        Assert.False(Info(video, pix, audio).CompativelSemConverter);

    [Theory]
    [InlineData("matroska,webm")]
    [InlineData("mov,mp4,m4a,3gp,3g2,mj2")]
    public void Contêiner_de_origem_nao_importa(string formato) =>
        Assert.True(Info("vp9", "yuv420p", "opus", formato).CompativelSemConverter);
}
