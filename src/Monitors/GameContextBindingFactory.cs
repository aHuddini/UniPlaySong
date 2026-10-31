using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;

namespace UniPlaySong.Monitors
{
    // Creates priority bindings for game context that work with any Playnite theme Supports multiple data
    // context paths used by different themes
    internal static class GameContextBindingFactory
    {
        // Common data context paths used by various Playnite themes Ordered by priority (most common first)
        private static readonly IReadOnlyList<string> BindingPaths = new List<string>
        {
            "SelectedGameDetails.Game.Game",  // Common in many themes
            "SelectedGameDetails.Game",        // Alternative path
            "SelectedGameContext.Game",        // Some fullscreen themes
            "SelectedGame.Game",               // Another variation
            "SelectedGame",                    // Direct game property
            string.Empty                       // Fallback to DataContext itself
        };

        // Priority binding over the candidate paths, first that resolves wins; contextSource is usually window.DataContext.
        internal static BindingBase Create(object contextSource)
        {
            var priorityBinding = new PriorityBinding();

            foreach (var path in BindingPaths)
            {
                priorityBinding.Bindings.Add(CreateBinding(path, contextSource));
            }

            return priorityBinding;
        }

        // Creates a single binding for a specific path
        private static Binding CreateBinding(string path, object source)
        {
            var binding = new Binding
            {
                Mode = BindingMode.OneWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            if (!string.IsNullOrWhiteSpace(path))
            {
                binding.Path = new PropertyPath(path);
            }

            if (source != null)
            {
                binding.Source = source;
            }

            return binding;
        }
    }
}

