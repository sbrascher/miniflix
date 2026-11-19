namespace MiniFlix.Services;

public sealed class Settings
{
    /// <summary>"escuro" | "claro"</summary>
    public string Tema { get; set; } = "escuro";

    /// <summary>
    /// Pasta dos vídeos escolhida pelo usuário. null = pasta "videos" ao lado do MiniFlix.exe.
    /// Na mesma unidade do app fica relativa (ex.: "..\Filmes"), para continuar valendo quando o
    /// pendrive muda de letra; em outra unidade fica absoluta (ex.: "E:\Filmes").
    /// </summary>
    public string? PastaVideos { get; set; }
}

/// <summary>Configurações do usuário em data/settings.json.</summary>
public sealed class SettingsService(string arquivo)
{
    private static readonly string[] Temas = ["escuro", "claro"];

    public Settings Atual { get; private set; } = JsonStore.LerSeguro<Settings>(arquivo) ?? new Settings();

    /// <summary>Aplica uma alteração sobre uma cópia da configuração atual, valida e grava.</summary>
    public Settings Alterar(Action<Settings> alteracao)
    {
        var novo = new Settings { Tema = Atual.Tema, PastaVideos = Atual.PastaVideos };
        alteracao(novo);
        if (!Temas.Contains(novo.Tema)) throw new ArgumentException($"Tema inválido: {novo.Tema}");
        JsonStore.Gravar(arquivo, novo);
        Atual = novo;
        return novo;
    }
}
