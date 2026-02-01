using System;
using System.Windows;
using Playnite.SDK.Controls;
using UniPlaySong.Audio;
using UniPlaySong.DeskMediaControl;

namespace UniPlaySong.Controls
{
    /// <summary>
    /// PluginUserControl wrapper for the spectrum visualizer.
    /// Registered as "UPS_SpectrumVisualizer" for theme integration.
    /// Themes can place this anywhere via:
    ///   &lt;ContentControl x:Name="UPS_SpectrumVisualizer" /&gt;
    /// </summary>
    public class SpectrumVisualizerPluginControl : PluginUserControl
    {
        private readonly SpectrumVisualizerControl _visualizer;

        public SpectrumVisualizerPluginControl(Func<UniPlaySongSettings> getSettings = null)
        {
            _visualizer = new SpectrumVisualizerControl();
            if (getSettings != null)
                _visualizer.SetSettingsProvider(getSettings);
            Content = _visualizer;

            // Check if audio is already playing
            var provider = VisualizationDataProvider.Current;
            if (provider != null)
            {
                _visualizer.SetActive(true);
            }

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to static provider changes
            // The visualizer polls VisualizationDataProvider.Current each frame,
            // so no explicit subscription needed — just activate/deactivate
            var provider = VisualizationDataProvider.Current;
            _visualizer.SetActive(provider != null);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _visualizer.SetActive(false);
        }
    }
}
