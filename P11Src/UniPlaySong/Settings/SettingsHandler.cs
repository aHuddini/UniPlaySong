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
        var pluginDataDir = Path.Combine(userDataDir, "Extensions", UniPlaySongPlugin.Id);
        Directory.CreateDirectory(pluginDataDir);
        _settingsFilePath = Path.Combine(pluginDataDir, "settings.json");
        settings = LoadFromDisk();
    }

    public override FrameworkElement GetEditView(GetSettingsViewArgs args)
    {
        return new SettingsView { DataContext = this };
    }

    public override async Task BeginEditAsync(BeginEditArgs args)
    {
        _editSnapshot = JsonSerializer.Serialize(Settings, _jsonOptions);
        await Task.CompletedTask;
    }

    public override async Task EndEditAsync(EndEditArgs args)
    {
        SaveToDisk();
        _editSnapshot = null;
        await Task.CompletedTask;
    }

    public override async Task CancelEditAsync(CancelEditArgs args)
    {
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
                var json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<UniPlaySongSettings>(json, _jsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings — using defaults");
        }
        return new();
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
        }
    }
}
