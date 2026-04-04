using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace UniPlaySong.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void BrowseDefaultMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
            Title = "Select Default Music File"
        };

        if (dialog.ShowDialog() == true && DataContext is SettingsHandler handler)
        {
            handler.Settings.DefaultMusicPath = dialog.FileName;
        }
    }
}
