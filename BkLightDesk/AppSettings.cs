namespace BkLightDesk;

/// <summary>
/// A global proxy class for application settings. 
/// Provides a simplified interface to access persistent data stored in SettingsManager.
/// </summary>
public static class AppSettings
{
    /// <summary>
    /// Acts as a bridge to the persistent SettingsManager.
    /// When read or written, it automatically triggers disk I/O through the manager.
    /// </summary>
    public static bool UseTurboMode 
    { 
        get => SettingsManager.TurboMode; 
        set => SettingsManager.TurboMode = value; 
    }
}