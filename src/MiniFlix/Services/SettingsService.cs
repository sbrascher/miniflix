namespace MiniFlix.Services;

public sealed class Settings
{
    /// <summary>"escuro" | "claro"</summary>
    public string Tema { get; set; } = "escuro";
}

/// <summary>Configurações do usuário em data/settings.json.</summary>
public sealed class SettingsService(string arquivo)
{
    private static readonly string[] Temas = ["escuro", "claro"];

    public Settings Atual { get; private set; } = JsonStore.LerSeguro<Settings>(arquivo) ?? new Settings();

    public Settings Salvar(Settings novo)
    {
        if (!Temas.Contains(novo.Tema)) throw new ArgumentException($"Tema inválido: {novo.Tema}");
        JsonStore.Gravar(arquivo, novo);
        Atual = novo;
        return novo;
    }
}
