using System;
using System.IO;
using System.Text.Json;

namespace BkLightDesk;

// Questa classe rappresenta i dati che vogliamo salvare
public class AppConfig
{
    public int SavedBrightness { get; set; } = 100; // Default 100%
    public bool UseTurboMode { get; set; } = true;  // Default True
}

public static class SettingsManager
{
    private static AppConfig _config = new AppConfig();
    
    // Percorso salvataggio: %AppData%/Local/BkLightDesk/user_settings.json
    private static readonly string _folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BkLightDesk");
    private static readonly string _filePath = Path.Combine(_folderPath, "user_settings.json");

    // Proprietà statiche: quando le modifichi, salvano automaticamente
    public static int Brightness
    {
        get => _config.SavedBrightness;
        set 
        { 
            _config.SavedBrightness = value; 
            Save(); 
        }
    }

    public static bool UseTurboMode
    {
        get => _config.UseTurboMode;
        set 
        { 
            _config.UseTurboMode = value; 
            Save(); 
        }
    }

    public static void Load()
    {
        try
        {
            if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);
            
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null) _config = loaded;
            }
        }
        catch 
        { 
            // Se fallisce il caricamento (file corrotto o primo avvio), usa i default
        }
    }

    private static void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_filePath, json);
        }
        catch { /* Ignora errori di scrittura */ }
    }
}