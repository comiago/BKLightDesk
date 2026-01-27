namespace BkLightDesk;

public static class AppSettings
{
    // Ora questa proprietà fa da ponte verso il SettingsManager.
    // Quando la leggi o la scrivi, in realtà stai leggendo/scrivendo su file.
    public static bool UseTurboMode 
    { 
        get => SettingsManager.UseTurboMode; 
        set => SettingsManager.UseTurboMode = value; 
    }
}