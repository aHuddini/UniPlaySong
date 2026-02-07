using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Controls;
using UniPlaySong.Services;

namespace UniPlaySong.Monitors
{
    /// <summary>
    /// Monitors Playnite windows and attaches music controls for universal compatibility
    /// Works with both desktop and fullscreen modes, any theme
    /// </summary>
    public class WindowMonitor
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static IMusicPlaybackService _playbackService;
        private static ErrorHandlerService _errorHandler;

        /// <summary>
        /// Attaches the window monitor to Playnite's window system
        /// </summary>
        public static void Attach(IMusicPlaybackService playbackService, ErrorHandlerService errorHandler = null)
        {
            _playbackService = playbackService;
            _errorHandler = errorHandler;
            
            // Register handler for all window loaded events
            EventManager.RegisterClassHandler(
                typeof(Window), 
                Window.LoadedEvent, 
                new RoutedEventHandler(Window_Loaded));
            
            Logger.Debug("WindowMonitor attached - ready for universal theme support");
        }

        /// <summary>
        /// Handles window loaded events to attach music controls
        /// </summary>
        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window))
            {
                return;
            }

            if (_errorHandler != null)
            {
                _errorHandler.Try(
                    () =>
                    {
                        // Look for a ContentControl where we can attach our control
                        // Themes can provide a ContentControl named "UniPlaySong_MusicControl" for integration
                        var musicControlPlaceholder = FindVisualChild<ContentControl>(
                            window, 
                            name: "UniPlaySong_MusicControl");

                        if (musicControlPlaceholder != null)
                        {
                            // Create a simple control that binds to game context
                            var control = new PluginUserControl();
                            var contextSource = window.DataContext;
                            var binding = GameContextBindingFactory.Create(contextSource);
                            
                            // Bind to GameContext property for theme integration
                            BindingOperations.SetBinding(
                                control, 
                                PluginUserControl.GameContextProperty, 
                                binding);

                            musicControlPlaceholder.Focusable = false;
                            musicControlPlaceholder.Content = control;
                            
                            Logger.Debug("Attached music control to window");
                        }
                    },
                    context: "attaching music control to window",
                    showUserMessage: false
                );
            }
            else
            {
                // Fallback to original error handling
                try
                {
                    // Look for a ContentControl where we can attach our control
                    // Themes can provide a ContentControl named "UniPlaySong_MusicControl" for integration
                    var musicControlPlaceholder = FindVisualChild<ContentControl>(
                        window, 
                        name: "UniPlaySong_MusicControl");

                    if (musicControlPlaceholder != null)
                    {
                        // Create a simple control that binds to game context
                        var control = new PluginUserControl();
                        var contextSource = window.DataContext;
                        var binding = GameContextBindingFactory.Create(contextSource);
                        
                        // Bind to GameContext property for theme integration
                        BindingOperations.SetBinding(
                            control, 
                            PluginUserControl.GameContextProperty, 
                            binding);

                        musicControlPlaceholder.Focusable = false;
                        musicControlPlaceholder.Content = control;
                        
                        Logger.Debug("Attached music control to window");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error attaching to window: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Recursively searches the visual tree for a child element
        /// </summary>
        private static OutType FindVisualChild<OutType>(
            DependencyObject parent, 
            string typeName = null, 
            string name = null) 
            where OutType : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is OutType outType)
                {
                    var frameworkElement = child as FrameworkElement;
                    bool typeMatches = typeName == null || child.GetType().Name == typeName;
                    bool nameMatches = name == null || frameworkElement?.Name == name;
                    
                    if (typeMatches && nameMatches)
                    {
                        return outType;
                    }
                }

                // Recursively search children
                var childResult = FindVisualChild<OutType>(child, typeName, name);
                if (childResult != null)
                {
                    return childResult;
                }
            }

            return null;
        }
    }
}

