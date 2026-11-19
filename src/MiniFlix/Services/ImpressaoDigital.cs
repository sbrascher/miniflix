using System.Security.Cryptography;

namespace MiniFlix.Services;

/// <summary>
/// Impressão digital de um arquivo de vídeo para reconhecer reimportações, mesmo com outro nome ou em
/// outra pasta. Ler o arquivo inteiro levaria minutos num pendrive; em vez disso usa o tamanho exato
/// + amostras do início, do meio e do fim. Dois vídeos diferentes com o mesmo tamanho e os mesmos
/// trechos nesses três pontos não acontecem na prática.
/// </summary>
public static class ImpressaoDigital
{
    public const int TamanhoInicio = 1024 * 1024;  // 1 MB (o mesmo trecho da versão antiga)
    public const int TamanhoAmostra = 64 * 1024;   // 64 KB do meio e 64 KB do fim

    /// <param name="Atual">Formato atual ("local2:…"), gravado nos vídeos importados a partir de agora.</param>
    /// <param name="Antiga">Formato antigo ("local:…", só o primeiro 1 MB + tamanho), dos vídeos já importados.
    /// Sai do mesmo trecho lido, então comparar com os dois não custa nada a mais.</param>
    public sealed record Ids(string Atual, string Antiga)
    {
        public bool Contem(string? id) => id == Atual || id == Antiga;
    }

    public static Ids Calcular(string arquivo)
    {
        using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 1, FileOptions.RandomAccess);
        long tamanho = fs.Length;
        var tamanhoBytes = BitConverter.GetBytes(tamanho);

        var inicio = new byte[Math.Min(tamanho, TamanhoInicio)];
        fs.ReadExactly(inicio);

        // Formato antigo: idêntico ao que ImportacaoLocal sempre gravou.
        var antiga = "local:" + Convert.ToHexStringLower(SHA256.HashData([.. inicio, .. tamanhoBytes]))[..16];

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(tamanhoBytes);
        sha.AppendData(inicio);
        if (tamanho > TamanhoInicio)
        {
            foreach (var posicao in new[] { tamanho / 2 - TamanhoAmostra / 2, tamanho - TamanhoAmostra })
            {
                var p = Math.Max(posicao, TamanhoInicio); // arquivos pequenos: as amostras não repetem o início
                var trecho = new byte[Math.Min(TamanhoAmostra, tamanho - p)];
                if (trecho.Length == 0) continue;
                fs.Position = p;
                fs.ReadExactly(trecho);
                sha.AppendData(trecho);
            }
        }
        var atual = "local2:" + Convert.ToHexStringLower(sha.GetHashAndReset())[..32];
        return new Ids(atual, antiga);
    }
}
