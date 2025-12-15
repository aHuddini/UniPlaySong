using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;

namespace UniPlaySong.Monitors
{
    /// <summary>
    /// Creates priority bindings for game context that work with any Playnite theme
    /// Supports multiple data context paths used by different themes
    /// </summary>
    internal static class GameContextBindingFactory
    {
        /// <summary>
        /// Common data context paths used by various Playnite themes
        /// Ordered by priority (most common first)
        /// </summary>
        private static readonly IReadOnlyList<string> BindingPaths = new List<string>
        {
            "SelectedGameDetails.Game.Game",  // Common in many themes
            "SelectedGameDetails.Game",        // Alternative path
            "SelectedGameContext.Game",        // Some fullscreen themes
            "SelectedGame.Game",               // Another variation
            "SelectedGame",                    // Direct game property
            string.Empty                       // Fallback to DataContext itself
        };

        /// <summary>
        /// Creates a priority binding that tries multiple paths until one works
        /// </summary>
        /// <param name="contextSource">The source object (usually window.DataContext)</param>
        /// <returns>A PriorityBinding that will use the first available path</returns>
        internal static BindingBase Create(object contextSource)
        {
            var priorityBinding = new PriorityBinding();

            foreach (var path in BindingPaths)
            {
                priorityBinding.Bindings.Add(CreateBinding(path, contextSource));
            }

            return priorityBinding;
        }

        /// <summary>
        /// Creates a single binding for a specific path
        /// </summary>
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

