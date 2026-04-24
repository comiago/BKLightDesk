using System;
using System.IO;
using System.Text.Json;

namespace BkLightDesk;

/// <summary>
/// Data model for user preferences.
/// </summary>
public class UserConfiguration
{
    public int Brightness { get; set; } = 100;
    public bool TurboMode { get; set; } = true;
}

/// <summary>
/// Handles loading and saving application settings to a local JSON file.
/// </summary>
public static class SettingsManager
{
    private static UserConfiguration _config = new();
    private static readonly object _fileLock = new();
    
    // Path: %AppData%/Local/BkLightDesk/settings.json
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "BkLightDesk"
    );
    
    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    /// <summary>
    /// Gets or sets the matrix brightness (1-100). Automatically persists to disk.
    /// </summary>
    public static int Brightness
    {
        get => _config.Brightness;
        set 
        { 
            if (_config.Brightness == value) return;
            _config.Brightness = value; 
            Save(); 
        }
    }

    /// <summary>
    /// Gets or sets whether Turbo Mode is enabled. Automatically persists to disk.
    /// </summary>
    public static bool TurboMode
    {
        get => _config.TurboMode;
        set 
        { 
            if (_config.TurboMode == value) return;
            _config.TurboMode = value; 
            Save(); 
        }
    }

    /// <summary>
    /// Loads settings from the JSON file. If the file is missing or corrupt, defaults are used.
    /// </summary>
    public static void Load()
    {
        lock (_fileLock)
        {
            try
            {
                if (!Directory.Exists(FolderPath)) 
                    Directory.CreateDirectory(FolderPath);
                
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<UserConfiguration>(json);
                    if (loaded != null) _config = loaded;
                }
            }
            catch (Exception)
            {
                // On failure (e.g., first run or corrupted file), we fall back to defaults
                _config = new UserConfiguration();
            }
        }
    }

    /// <summary>
    /// Serializes the current configuration to a JSON file.
    /// </summary>
    private static void Save()
    {
        lock (_fileLock)
        {
            try
            {
                // Ensure directory exists before saving
                if (!Directory.Exists(FolderPath)) 
                    Directory.CreateDirectory(FolderPath);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception)
            {
                // Fail silently to prevent app crashes during IO issues
            }
        }
    }

    /// <summary>
    /// Resets all settings to their original factory values.
    /// </summary>
    public static void ResetToDefaults()
    {
        _config = new UserConfiguration();
        Save();
    }
}