using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniFlix.Services;

public static partial class Slug
{
    public const int TamanhoMaximo = 60;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Valido();

    /// <summary>"Aula #01: Introdução!" → "aula-01-introducao".</summary>
    public static string Gerar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "video";

        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var l = char.ToLowerInvariant(c);
            if (l is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(l);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > TamanhoMaximo)
        {
            slug = slug[..TamanhoMaximo];
            var corte = slug.LastIndexOf('-');
            if (corte >= TamanhoMaximo / 2) slug = slug[..corte]; // corta numa fronteira de palavra
            slug = slug.Trim('-');
        }
        return slug.Length == 0 ? "video" : slug;
    }

    /// <summary>Acrescenta -2, -3... até encontrar um slug livre.</summary>
    public static string Unico(string slug, Func<string, bool> existe)
    {
        if (!existe(slug)) return slug;
        for (int n = 2; ; n++)
        {
            var candidato = $"{slug}-{n}";
            if (!existe(candidato)) return candidato;
        }
    }

    public static bool EhValido(string? slug) => slug is not null && slug.Length <= TamanhoMaximo + 8 && Valido().IsMatch(slug);
}
