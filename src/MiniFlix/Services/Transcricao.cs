using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniFlix.Services;

/// <summary>
/// Transcrição de vídeos do YouTube a partir das legendas (sem IA): escolhe a melhor legenda,
/// lê o arquivo .vtt e gera um .md legível, com o horário de cada parágrafo.
/// </summary>
public static partial class Transcricao
{
    public const string SufixoMarkdown = ".transcricao.md";
    public const string SufixoLegenda = ".legenda.vtt";

    /// <summary>Legenda escolhida: a chave do yt-dlp (ex.: "pt-BR", "pt-orig", "en") e se é automática.</summary>
    public sealed record Escolha(string Chave, bool Automatica)
    {
        public bool Portugues => Chave.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        public string NomeIdioma => Chave.ToLowerInvariant() switch
        {
            var c when c.StartsWith("pt-br") => "português (Brasil)",
            var c when c.StartsWith("pt-pt") => "português (Portugal)",
            var c when c.StartsWith("pt") => "português",
            var c when c.StartsWith("en") => "inglês",
            var c => c,
        };

        public string NomeOrigem => Automatica
            ? "legenda automática do YouTube (gerada a partir da fala do vídeo)"
            : "legenda do autor do vídeo (YouTube)";
    }

    /// <summary>
    /// Escolhe a legenda pelo JSON do yt-dlp (--dump-single-json), nesta ordem:
    /// 1) do autor em português (pt-BR, pt, pt-…); 2) automática em português, só a ORIGINAL do vídeo
    /// (as traduções automáticas têm "tlang=" no endereço); 3) do autor em inglês; 4) automática original em inglês.
    /// Retorna null se não houver nenhuma dessas.
    /// </summary>
    public static Escolha? Escolher(JsonElement video)
    {
        static IEnumerable<string> Chaves(JsonElement v, string campo, bool soOriginais)
        {
            if (!v.TryGetProperty(campo, out var mapa) || mapa.ValueKind != JsonValueKind.Object) yield break;
            foreach (var p in mapa.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Array) continue;
                var vtt = p.Value.EnumerateArray().FirstOrDefault(f =>
                    f.TryGetProperty("ext", out var e) && e.GetString() == "vtt");
                if (vtt.ValueKind != JsonValueKind.Object) continue;
                if (soOriginais && vtt.TryGetProperty("url", out var u) && (u.GetString() ?? "").Contains("tlang=")) continue;
                yield return p.Name;
            }
        }

        static string? Melhor(IEnumerable<string> chaves, string idioma, params string[] preferidas)
        {
            var lista = chaves.Where(c => c.StartsWith(idioma, StringComparison.OrdinalIgnoreCase)).ToList();
            return preferidas.FirstOrDefault(p => lista.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? lista.FirstOrDefault();
        }

        var manuais = Chaves(video, "subtitles", soOriginais: false).Where(c => c != "live_chat").ToList();
        var automaticas = Chaves(video, "automatic_captions", soOriginais: true).ToList();

        if (Melhor(manuais, "pt", "pt-BR", "pt") is { } a) return new Escolha(a, false);
        if (Melhor(automaticas, "pt", "pt-BR-orig", "pt-orig", "pt-BR", "pt") is { } b) return new Escolha(b, true);
        if (Melhor(manuais, "en", "en", "en-US", "en-GB") is { } c) return new Escolha(c, false);
        if (Melhor(automaticas, "en", "en-orig", "en") is { } d) return new Escolha(d, true);
        return null;
    }

    public sealed record Trecho(double Segundos, string Texto);

    [GeneratedRegex(@"^(?:(\d+):)?(\d{2}):(\d{2})[.,](\d{3})\s+-->")] private static partial Regex LinhaTempo();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex Marcacao();
    [GeneratedRegex(@"\s+")] private static partial Regex Espacos();

    /// <summary>
    /// Lê um .vtt e devolve o texto falado, em ordem, sem repetições. As legendas automáticas do YouTube
    /// repetem cada linha no bloco seguinte (efeito "karaokê") e trazem marcações de tempo por palavra:
    /// tudo isso é limpo.
    /// </summary>
    public static List<Trecho> LerVtt(string vtt)
    {
        var trechos = new List<Trecho>();
        var recentes = new Queue<string>(); // últimas linhas emitidas (para não repetir)
        double? inicio = null;
        foreach (var bruta in vtt.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = bruta.Trim();
            var m = LinhaTempo().Match(linha);
            if (m.Success)
            {
                inicio = (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 3600 : 0)
                         + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value)
                         + int.Parse(m.Groups[4].Value) / 1000.0;
                continue;
            }
            if (inicio is null || linha.Length == 0) continue; // cabeçalho (WEBVTT, Kind, Language) ou separador
            if (linha.All(char.IsAsciiDigit) || linha.StartsWith("NOTE")) continue; // número do bloco / comentário
            var texto = System.Net.WebUtility.HtmlDecode(Espacos().Replace(Marcacao().Replace(linha, ""), " ").Trim());
            if (texto.Length == 0 || recentes.Contains(texto)) continue;
            trechos.Add(new Trecho(inicio.Value, texto));
            recentes.Enqueue(texto);
            if (recentes.Count > 3) recentes.Dequeue();
        }
        return trechos;
    }

    /// <summary>Agrupa os trechos em parágrafos de ~30–60 s (quebra de preferência no fim de uma frase).</summary>
    public static List<Trecho> Paragrafos(IReadOnlyList<Trecho> trechos)
    {
        var paragrafos = new List<Trecho>();
        var atual = new StringBuilder();
        double inicio = 0;
        foreach (var t in trechos)
        {
            if (atual.Length > 0)
            {
                var decorrido = t.Segundos - inicio;
                var fimDeFrase = atual[^1] is '.' or '?' or '!';
                if (decorrido >= 60 || (decorrido >= 30 && fimDeFrase))
                {
                    paragrafos.Add(new Trecho(inicio, atual.ToString()));
                    atual.Clear();
                }
            }
            if (atual.Length == 0) inicio = t.Segundos;
            else atual.Append(' ');
            atual.Append(t.Texto);
        }
        if (atual.Length > 0) paragrafos.Add(new Trecho(inicio, atual.ToString()));
        return paragrafos;
    }

    /// <summary>1834 → "30:34"; 4000 → "1:06:40".</summary>
    public static string Horario(double segundos)
    {
        var s = (int)Math.Floor(segundos);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:D2}:{s % 60:D2}" : $"{s / 60:D2}:{s % 60:D2}";
    }

    /// <summary>Monta o .md da transcrição (cabeçalho com origem e idioma + parágrafos com horário).</summary>
    public static string GerarMarkdown(string titulo, string url, Escolha escolha, IReadOnlyList<Trecho> trechos, DateTime quando)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(titulo).AppendLine();
        sb.Append("- **Origem:** ").AppendLine(escolha.NomeOrigem);
        sb.Append("- **Idioma:** ").Append(escolha.NomeIdioma)
          .AppendLine(escolha.Portugues ? "" : " (o vídeo não tem legenda em português)");
        sb.Append("- **Vídeo:** ").AppendLine(url);
        sb.Append("- **Gerada pelo MiniFlix em:** ").AppendLine(quando.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        if (escolha.Automatica)
            sb.AppendLine().AppendLine("> Legendas automáticas podem ter erros de reconhecimento e não têm pontuação.");
        sb.AppendLine().AppendLine("---").AppendLine();
        foreach (var p in Paragrafos(trechos))
            sb.Append("**[").Append(Horario(p.Segundos)).Append("]** ").AppendLine(p.Texto).AppendLine();
        return sb.ToString();
    }
}
