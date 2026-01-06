using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
// OnScreenKeyboard is now in the root UniPlaySong namespace

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Stores original visual state for restoration
    /// </summary>
    internal class VisualState
    {
        public Style OriginalItemContainerStyle { get; set; }
        public Visibility OriginalVisibility { get; set; }
    }

    /// <summary>
    /// Service for applying and managing controller-specific visual enhancements
    /// Preserves original appearance for safe restoration
    /// </summary>
    public class VisualEnhancementService : IVisualEnhancementService
    {
        private readonly ILogger _logger;
        private readonly Dictionary<ListBox, VisualState> _originalListBoxStyles = new Dictionary<ListBox, VisualState>();
        private readonly Dictionary<Panel, VisualState> _originalPanelStates = new Dictionary<Panel, VisualState>();

        public VisualEnhancementService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ApplyControllerStyles(ListBox listBox)
        {
            if (listBox == null)
            {
                _logger?.Warn("Cannot apply controller styles to null ListBox");
                return;
            }

            try
            {
                // Don't re-apply if already applied
                if (_originalListBoxStyles.ContainsKey(listBox))
                {
                    _logger?.Debug($"Controller styles already applied to {listBox.Name ?? "ListBox"}");
                    return;
                }

                // Save original state
                _originalListBoxStyles[listBox] = new VisualState
                {
                    OriginalItemContainerStyle = listBox.ItemContainerStyle
                };

                // Create and apply controller-enhanced style
                var controllerStyle = CreateControllerEnhancedStyle(listBox);
                if (controllerStyle != null)
                {
                    listBox.ItemContainerStyle = controllerStyle;
                    _logger?.Debug($"Controller styles applied to {listBox.Name ?? "ListBox"}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to apply controller styles to {listBox.Name ?? "ListBox"} - appearance unchanged");
            }
        }

        public void RemoveControllerStyles(ListBox listBox)
        {
            if (listBox == null) return;

            try
            {
                if (_originalListBoxStyles.TryGetValue(listBox, out var state))
                {
                    // Restore original style
                    listBox.ItemContainerStyle = state.OriginalItemContainerStyle;
                    _originalListBoxStyles.Remove(listBox);
                    
                    _logger?.Debug($"Controller styles removed from {listBox.Name ?? "ListBox"}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error removing controller styles from {listBox.Name ?? "ListBox"} - no functional impact");
            }
        }

        public void ShowControllerHints(Panel hintsPanel)
        {
            if (hintsPanel == null) return;

            try
            {
                hintsPanel.Visibility = Visibility.Visible;
                _logger?.Debug("Controller hints panel shown");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error showing controller hints panel");
            }
        }

        public void HideControllerHints(Panel hintsPanel)
        {
            if (hintsPanel == null) return;

            try
            {
                hintsPanel.Visibility = Visibility.Collapsed;
                _logger?.Debug("Controller hints panel hidden");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error hiding controller hints panel");
            }
        }

        public void HideSearchForController(Panel searchContainer)
        {
            if (searchContainer == null) return;

            try
            {
                // Save original visibility if not already saved
                if (!_originalPanelStates.ContainsKey(searchContainer))
                {
                    _originalPanelStates[searchContainer] = new VisualState
                    {
                        OriginalVisibility = searchContainer.Visibility
                    };
                }

                searchContainer.Visibility = Visibility.Collapsed;
                _logger?.Debug("Search container hidden for controller mode");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error hiding search container for controller mode");
            }
        }

        public void ShowSearchForNormal(Panel searchContainer)
        {
            if (searchContainer == null) return;

            try
            {
                if (_originalPanelStates.TryGetValue(searchContainer, out var state))
                {
                    searchContainer.Visibility = state.OriginalVisibility;
                    _originalPanelStates.Remove(searchContainer);
                }
                else
                {
                    // Default to visible if no original state saved
                    searchContainer.Visibility = Visibility.Visible;
                }
                
                _logger?.Debug("Search container shown for normal mode");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error showing search container for normal mode");
            }
        }

        public bool AreControllerStylesApplied(ListBox listBox)
        {
            return _originalListBoxStyles.ContainsKey(listBox);
        }

        public void ShowQuickFilters(Panel filtersContainer)
        {
            if (filtersContainer == null) return;

            try
            {
                // Save original visibility if not already saved
                if (!_originalPanelStates.ContainsKey(filtersContainer))
                {
                    _originalPanelStates[filtersContainer] = new VisualState
                    {
                        OriginalVisibility = filtersContainer.Visibility
                    };
                }

                filtersContainer.Visibility = Visibility.Visible;
                _logger?.Debug("Quick filters shown for controller mode");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error showing quick filters for controller mode");
            }
        }

        public void HideQuickFilters(Panel filtersContainer)
        {
            if (filtersContainer == null) return;

            try
            {
                if (_originalPanelStates.TryGetValue(filtersContainer, out var state))
                {
                    filtersContainer.Visibility = state.OriginalVisibility;
                    _originalPanelStates.Remove(filtersContainer);
                }
                else
                {
                    // Default to hidden if no original state saved
                    filtersContainer.Visibility = Visibility.Collapsed;
                }
                
                _logger?.Debug("Quick filters hidden for normal mode");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error hiding quick filters for normal mode");
            }
        }

        // TODO: OnScreenKeyboard feature not yet implemented
        // private object _activeOSK = null;

        public void ShowOnScreenKeyboard(Panel oskContainer)
        {
            // OnScreenKeyboard not yet implemented - stub method
            _logger?.Debug("On-Screen Keyboard feature not yet implemented");
        }

        public void HideOnScreenKeyboard(Panel oskContainer)
        {
            if (oskContainer == null) return;

            try
            {
                if (_originalPanelStates.TryGetValue(oskContainer, out var state))
                {
                    oskContainer.Visibility = state.OriginalVisibility;
                    _originalPanelStates.Remove(oskContainer);
                }
                else
                {
                    // Default to hidden if no original state saved
                    oskContainer.Visibility = Visibility.Collapsed;
                }

                // Clear container but keep OSK instance for reuse
                oskContainer.Children.Clear();
                
                _logger?.Debug("On-Screen Keyboard hidden for normal mode");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error hiding On-Screen Keyboard for normal mode");
            }
        }

        /// <summary>
        /// Creates a controller-enhanced style based on the existing Material Design style
        /// </summary>
        private Style CreateControllerEnhancedStyle(ListBox listBox)
        {
            try
            {
                var style = new Style(typeof(ListBoxItem));
                
                // Base on existing Material Design style if available
                if (Application.Current.Resources.Contains("MaterialDesignListBoxItem"))
                {
                    style.BasedOn = (Style)Application.Current.Resources["MaterialDesignListBoxItem"];
                }
                else if (listBox.ItemContainerStyle != null)
                {
                    // Base on current style if Material Design not available
                    style.BasedOn = listBox.ItemContainerStyle;
                }

                // Add controller-specific focus visual
                var focusStyle = CreateControllerFocusStyle();
                if (focusStyle != null)
                {
                    style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, focusStyle));
                }

                // Enhanced selection feedback for controller
                style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(8)));
                style.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(2)));

                return style;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to create controller-enhanced style - using original style");
                return null;
            }
        }

        /// <summary>
        /// Creates a focus visual style optimized for controller navigation
        /// </summary>
        private Style CreateControllerFocusStyle()
        {
            try
            {
                var focusStyle = new Style(typeof(Control));
                var template = new ControlTemplate(typeof(Control));
                
                // Create focus border with glow effect
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(33, 150, 243))); // Material Blue
                border.SetValue(Border.BorderThicknessProperty, new Thickness(3));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                border.SetValue(Border.MarginProperty, new Thickness(-3));
                border.SetValue(Border.OpacityProperty, 0.9);
                
                // Add subtle glow effect
                var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(33, 150, 243),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };
                border.SetValue(Border.EffectProperty, dropShadow);
                
                template.VisualTree = border;
                focusStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
                
                return focusStyle;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to create controller focus style - using default");
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                // Restore all ListBoxes to original state
                var listBoxesToRestore = new List<ListBox>(_originalListBoxStyles.Keys);
                foreach (var listBox in listBoxesToRestore)
                {
                    RemoveControllerStyles(listBox);
                }

                // Restore all panels to original state
                var panelsToRestore = new List<Panel>(_originalPanelStates.Keys);
                foreach (var panel in panelsToRestore)
                {
                    ShowSearchForNormal(panel);
                }

                _originalListBoxStyles.Clear();
                _originalPanelStates.Clear();
                
                _logger?.Debug("VisualEnhancementService disposed");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disposing VisualEnhancementService");
            }
        }
    }
}