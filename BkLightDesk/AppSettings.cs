namespace BkLightDesk;

public static class AppSettings
{
    // Ora legge e scrive direttamente sul file di salvataggio
    public static bool UseTurboMode 
    { 
        get => SettingsManager.UseTurboMode; 
        set => SettingsManager.UseTurboMode = value; 
    }
}