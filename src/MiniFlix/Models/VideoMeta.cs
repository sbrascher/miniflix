namespace MiniFlix.Models;

/// <summary>Conteúdo de videos/&lt;slug&gt;/&lt;slug&gt;.json — a fonte da verdade de cada vídeo.</summary>
public sealed class VideoMeta
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Categoria { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public bool Favorito { get; set; }
    public string Origem { get; set; } = "";          // "youtube" | "local"
    public string? UrlOrigem { get; set; }
    public double DuracaoSegundos { get; set; }
    public string? DataPublicacao { get; set; }        // "yyyy-MM-dd"
    public DateTime DataAdicionado { get; set; }
    public int Versao { get; set; } = 1;
}

/// <summary>Vídeo como a UI o recebe: metadados + URLs prontas do host virtual.</summary>
public sealed record VideoItem(
    VideoMeta Meta,
    string VideoUrl,
    string? CapaUrl,
    long TamanhoBytes);
