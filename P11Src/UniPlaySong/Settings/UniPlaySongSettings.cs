using CommunityToolkit.Mvvm.ComponentModel;
using UniPlaySong.Common;

namespace UniPlaySong.Settings;

partial class UniPlaySongSettings : ObservableObject
{
    [ObservableProperty] private bool enableMusic = true;
    [ObservableProperty] private int musicVolume = Constants.DefaultMusicVolume;
    [ObservableProperty] private int fadeInDurationMs = Constants.DefaultFadeInDurationMs;
    [ObservableProperty] private int fadeOutDurationMs = Constants.DefaultFadeOutDurationMs;
    [ObservableProperty] private bool enableDefaultMusic;
    [ObservableProperty] private string? defaultMusicPath;
    [ObservableProperty] private bool radioModeEnabled;
}
