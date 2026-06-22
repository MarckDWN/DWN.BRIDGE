using AIBridge.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AIBridge.Services;

/// <summary>
/// Persists and loads application settings from %LocalAppData%\AIBridge\settings.json.
/// </summary>
public class SettingsService
{
    private readonly string _settingsFile;
    private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge");
        Directory.CreateDirectory(dir);
        _settingsFile = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, _jsonOpts) ?? new AppSettings();
            }
        }
        catch { /* ignore corrupted file */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOpts);
            File.WriteAllText(_settingsFile, json);
        }
        catch { }

        // Avvia la sincronizzazione cloud in background se possibile
        _ = SyncToCloudAsync(settings);
    }

    /// <summary>
    /// Sincronizza le impostazioni dal Cloud Brain se connesso.
    /// Viene tipicamente chiamata dopo il login.
    /// </summary>
    public async Task SyncFromCloudAsync()
    {
        var client = CloudServiceLocator.Client;
        if (client == null || !client.IsConnected) return;

        var cloudSettings = await client.GetSettingsAsync();
        if (cloudSettings.Count > 0)
        {
            var local = Load();
            bool changed = false;

            if (cloudSettings.TryGetValue("Language", out var lang) && local.Language != lang)
            {
                local.Language = lang;
                changed = true;
            }

            if (changed)
            {
                SaveLocal(local); // Salva solo in locale per non causare loop
            }
        }
    }

    private void SaveLocal(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOpts);
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }

    private async Task SyncToCloudAsync(AppSettings settings)
    {
        var client = CloudServiceLocator.Client;
        if (client == null || !client.IsConnected) return;

        var dict = new Dictionary<string, string>
        {
            { "Language", settings.Language }
        };

        await client.UpdateSettingsAsync(dict);
    }
}
