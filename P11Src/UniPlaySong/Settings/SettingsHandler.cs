using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Playnite;

namespace UniPlaySong.Settings;

[INotifyPropertyChanged]
partial class SettingsHandler : PluginSettingsHandler
{
    private static readonly ILogger _logger = LogManager.GetLogger();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsFilePath;
    [ObservableProperty] private UniPlaySongSettings settings;
    private string? _editSnapshot;

    public SettingsHandler(string userDataDir)
    {
        // P11's UserDataDir is already plugin-scoped (ExtensionsData/{PluginId}/)
        Directory.CreateDirectory(userDataDir);
        _settingsFilePath = Path.Combine(userDataDir, "settings.json");
        settings = LoadFromDisk();
    }

    public override FrameworkElement GetEditView(GetSettingsViewArgs args)
    {
        return new SettingsView { DataContext = this };
    }

    public override async Task BeginEditAsync(BeginEditArgs args)
    {
        _logger.Info("Settings: BeginEdit — snapshotting current state");
        _editSnapshot = JsonSerializer.Serialize(Settings, _jsonOptions);
        _logger.Info($"Settings: snapshot = {_editSnapshot}");
        await Task.CompletedTask;
    }

    public override async Task EndEditAsync(EndEditArgs args)
    {
        _logger.Info($"Settings: EndEdit — saving (enabled={Settings.EnableMusic}, vol={Settings.MusicVolume}, fadeIn={Settings.FadeInDurationMs}ms, fadeOut={Settings.FadeOutDurationMs}ms, defaultMusic={Settings.EnableDefaultMusic}, radio={Settings.RadioModeEnabled})");
        SaveToDisk();
        _editSnapshot = null;
        await Task.CompletedTask;
    }

    public override async Task CancelEditAsync(CancelEditArgs args)
    {
        _logger.Info("Settings: CancelEdit — restoring snapshot");
        if (_editSnapshot != null)
        {
            var restored = JsonSerializer.Deserialize<UniPlaySongSettings>(_editSnapshot, _jsonOptions);
            if (restored != null)
                Settings = restored;
        }
        _editSnapshot = null;
        await Task.CompletedTask;
    }

    public override async Task<ICollection<string>> VerifySettingsAsync(VerifySettingsArgs args)
    {
        var errors = new List<string>();

        if (Settings.MusicVolume < 0 || Settings.MusicVolume > 100)
            errors.Add("Music volume must be between 0 and 100.");

        if (Settings.FadeInDurationMs < 0)
            errors.Add("Fade in duration cannot be negative.");

        if (Settings.FadeOutDurationMs < 0)
            errors.Add("Fade out duration cannot be negative.");

        if (Settings.EnableDefaultMusic && !string.IsNullOrEmpty(Settings.DefaultMusicPath)
            && !File.Exists(Settings.DefaultMusicPath))
            errors.Add("Default music file does not exist.");

        await Task.CompletedTask;
        return errors;
    }

    private UniPlaySongSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                _logger.Info($"Settings: loading from {_settingsFilePath}");
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<UniPlaySongSettings>(json, _jsonOptions) ?? new();
                _logger.Info($"Settings: loaded (enabled={loaded.EnableMusic}, vol={loaded.MusicVolume}, fadeIn={loaded.FadeInDurationMs}ms, fadeOut={loaded.FadeOutDurationMs}ms, defaultMusic={loaded.EnableDefaultMusic}, radio={loaded.RadioModeEnabled})");
                return loaded;
            }
            _logger.Info("Settings: no settings file found — using defaults");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Settings: failed to load — using defaults");
        }
        return new();
    }

    // Call on shutdown to ensure settings are persisted
    public void SaveIfNeeded()
    {
        _logger.Info("Settings: SaveIfNeeded — persisting current state");
        SaveToDisk();
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            _logger.Info($"Settings: writing to {_settingsFilePath}:\n{json}");
            File.WriteAllText(_settingsFilePath, json);
            _logger.Info("Settings: write complete");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Settings: failed to save");
        }
    }
}
