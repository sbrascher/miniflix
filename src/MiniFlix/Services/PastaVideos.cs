namespace MiniFlix.Services;

/// <summary>Regras da pasta de vídeos configurável (resolução, gravação na configuração e validação).</summary>
public static class PastaVideos
{
    private static string Normalizar(string caminho) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(caminho));

    /// <summary>Caminho absoluto da pasta a partir do valor da configuração (relativo à raiz do app).</summary>
    public static string Resolver(string? configurada, string raiz, string padrao) =>
        string.IsNullOrWhiteSpace(configurada) ? Normalizar(padrao) : Normalizar(Path.Combine(raiz, configurada));

    /// <summary>
    /// Valor a gravar na configuração: null se for a pasta padrão; relativo à raiz se estiver na mesma
    /// unidade (sobrevive à troca de letra do pendrive); absoluto se estiver em outra unidade.
    /// </summary>
    public static string? ParaConfig(string pasta, string raiz, string padrao)
    {
        pasta = Normalizar(pasta);
        if (MesmoCaminho(pasta, padrao)) return null;
        return string.Equals(Path.GetPathRoot(pasta), Path.GetPathRoot(Normalizar(raiz)), StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(raiz, pasta)
            : pasta;
    }

    public static bool MesmoCaminho(string a, string b) =>
        string.Equals(Normalizar(a), Normalizar(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>true se <paramref name="caminho"/> é <paramref name="pai"/> ou está dentro dele.</summary>
    public static bool Dentro(string caminho, string pai)
    {
        var c = Normalizar(caminho) + Path.DirectorySeparatorChar;
        var p = Normalizar(pai) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica se a pasta pode guardar o acervo. Retorna a mensagem de erro (em português) ou null se estiver ok.
    /// <paramref name="raiz"/> é a pasta do MiniFlix.exe; <paramref name="padrao"/> é a pasta "videos" padrão.
    /// </summary>
    public static string? Validar(string pasta, string raiz, string padrao)
    {
        pasta = Normalizar(pasta);
        if (MesmoCaminho(pasta, padrao)) return null;

        if (Dentro(raiz, pasta))
            return "Essa pasta contém o próprio MiniFlix. Escolha a pasta \"videos\" ou outra fora do programa.";
        if (Dentro(pasta, raiz))
            return "Dentro da pasta do MiniFlix só a pasta \"videos\" pode guardar o acervo.";
        if (!Directory.Exists(pasta))
            return "A pasta não existe (o disco pode ter sido desconectado).";

        // Precisa ser gravável: importação, download, edição e capas escrevem lá.
        var teste = Path.Combine(pasta, $"_tmp-teste-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(teste, "");
            File.Delete(teste);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "O MiniFlix não tem permissão para gravar nessa pasta. Escolha outra.";
        }
        return null;
    }
}
