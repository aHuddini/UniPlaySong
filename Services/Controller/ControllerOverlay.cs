using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Main coordination service that manages all controller-related functionality
    /// Acts as a safe overlay that doesn't break existing dialog behavior
    /// </summary>
    public class ControllerOverlay : IControllerOverlay
    {
        private readonly IControllerDetectionService _detectionService;
        private readonly IControllerInputService _inputService;
        private readonly IFocusManagementService _focusService;
        private readonly IVisualEnhancementService _visualService;
        private readonly ILogger _logger;

        private Control _targetControl;
        private ListBox _targetListBox;
        private Panel _searchContainer;
        private Panel _hintsPanel;
        private Panel _quickFiltersContainer;
        private Panel _oskContainer;
        private bool _isAttached = false;
        private bool _oskVisible = false;
        private bool _isControllerMode = false;
        private bool _forceControllerMode = false;
        private bool _automaticDetection = true;

        public event EventHandler<bool> ControllerModeChanged;
        public event EventHandler<ControllerAction> ActionRequested;

        public bool IsAttached => _isAttached;
        public bool IsControllerMode => _isControllerMode;

        public ControllerOverlay(
            IControllerDetectionService detectionService,
            IControllerInputService inputService,
            IFocusManagementService focusService,
            IVisualEnhancementService visualService,
            ILogger logger)
        {
            _detectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
            _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
            _focusService = focusService ?? throw new ArgumentNullException(nameof(focusService));
            _visualService = visualService ?? throw new ArgumentNullException(nameof(visualService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void AttachTo(Control targetControl)
        {
            if (targetControl == null)
            {
                _logger?.Warn("Cannot attach to null control");
                return;
            }

            if (_isAttached)
            {
                _logger?.Warn("Controller overlay already attached, detaching first");
                Detach();
            }

            try
            {
                _targetControl = targetControl;
                
                // Find key UI elements safely
                _targetListBox = FindListBox(targetControl);
                _searchContainer = FindSearchContainer(targetControl);
                _quickFiltersContainer = FindQuickFiltersContainer(targetControl);
                _hintsPanel = FindControllerHintsContainer(targetControl);
                _oskContainer = FindOSKContainer(targetControl);

                if (_targetListBox == null)
                {
                    _logger?.Warn("No ListBox found in target control - controller overlay may not work properly");
                }

                // Subscribe to events
                _detectionService.ControllerModeChanged += OnDetectionServiceModeChanged;
                _inputService.ActionRequested += OnInputServiceActionRequested;

                // Start detection and apply initial state
                _detectionService.StartMonitoring();
                
                // Apply initial controller mode state
                UpdateControllerMode(_detectionService.IsControllerMode);

                _isAttached = true;
                _logger?.Info($"Controller overlay attached to {targetControl.Name ?? targetControl.GetType().Name}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to attach controller overlay to {targetControl.Name ?? targetControl.GetType().Name} - dialog will work normally");
                
                // Ensure clean state if attachment failed
                Detach();
            }
        }

        public void Detach()
        {
            if (!_isAttached) return;

            try
            {
                // Disable controller mode first
                if (_isControllerMode)
                {
                    DisableControllerMode();
                }

                // Unsubscribe from events
                if (_detectionService != null)
                {
                    _detectionService.ControllerModeChanged -= OnDetectionServiceModeChanged;
                    _detectionService.StopMonitoring();
                }

                if (_inputService != null)
                {
                    _inputService.ActionRequested -= OnInputServiceActionRequested;
                    if (_targetListBox != null)
                    {
                        _inputService.DetachFromControl(_targetListBox);
                    }
                }

                // Clear references
                _targetControl = null;
                _targetListBox = null;
                _searchContainer = null;
                _hintsPanel = null;
                _quickFiltersContainer = null;
                _oskContainer = null;
                _oskVisible = false;
                
                _isAttached = false;
                _logger?.Info("Controller overlay detached");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error detaching controller overlay - no functional impact");
            }
        }

        public void ForceControllerMode(bool enabled)
        {
            try
            {
                _automaticDetection = false;
                _forceControllerMode = enabled;
                
                UpdateControllerMode(enabled);
                
                _logger?.Info($"Controller mode forced to: {enabled}");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error forcing controller mode");
            }
        }

        public void RestoreAutomaticDetection()
        {
            try
            {
                _automaticDetection = true;
                _forceControllerMode = false;
                
                // Update to current detected state
                if (_detectionService != null)
                {
                    UpdateControllerMode(_detectionService.IsControllerMode);
                }
                
                _logger?.Info("Automatic controller detection restored");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error restoring automatic detection");
            }
        }

        private void OnDetectionServiceModeChanged(object sender, bool isControllerMode)
        {
            try
            {
                // Only respond to automatic detection if not in forced mode
                if (_automaticDetection)
                {
                    UpdateControllerMode(isControllerMode);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error handling controller mode change from detection service");
            }
        }

        private void OnInputServiceActionRequested(object sender, ControllerAction action)
        {
            try
            {
                // Handle OSK action internally
                if (action == ControllerAction.ShowOSK)
                {
                    ToggleOnScreenKeyboard();
                }
                else
                {
                    // Forward other actions to subscribers
                    ActionRequested?.Invoke(this, action);
                }
                
                _logger?.Debug($"Controller action processed: {action}");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error processing controller action: {action}");
            }
        }

        private void UpdateControllerMode(bool enableControllerMode)
        {
            if (_isControllerMode == enableControllerMode) return;

            try
            {
                _isControllerMode = enableControllerMode;

                if (enableControllerMode)
                {
                    EnableControllerMode();
                }
                else
                {
                    DisableControllerMode();
                }

                // Notify subscribers
                ControllerModeChanged?.Invoke(this, enableControllerMode);
                
                _logger?.Info($"Controller mode updated to: {enableControllerMode}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Error updating controller mode to {enableControllerMode} - attempting fallback to normal mode");
                
                // Fallback: try to disable controller mode for safety
                try
                {
                    DisableControllerMode();
                    _isControllerMode = false;
                }
                catch (Exception fallbackEx)
                {
                    _logger?.Error(fallbackEx, "Fallback to normal mode also failed - dialog may need restart");
                }
            }
        }

        private void EnableControllerMode()
        {
            try
            {
                if (_targetListBox != null)
                {
                    // Apply services in order
                    _inputService.AttachToControl(_targetListBox);
                    _focusService.EnableControllerFocus(_targetListBox);
                    _visualService.ApplyControllerStyles(_targetListBox);
                }

                // Phase 2: Show quick filters instead of hiding search
                if (_quickFiltersContainer != null)
                {
                    _visualService.ShowQuickFilters(_quickFiltersContainer);
                }

                // Hide search when quick filters are available
                if (_searchContainer != null && _quickFiltersContainer != null)
                {
                    _visualService.HideSearchForController(_searchContainer);
                }

                // Show hints if panel exists
                if (_hintsPanel != null)
                {
                    _visualService.ShowControllerHints(_hintsPanel);
                }

                _logger?.Debug("Controller mode enabled successfully");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error enabling controller mode - some features may not work");
                throw; // Re-throw to trigger fallback in UpdateControllerMode
            }
        }

        private void DisableControllerMode()
        {
            try
            {
                // Restore services in reverse order
                if (_hintsPanel != null)
                {
                    _visualService.HideControllerHints(_hintsPanel);
                }

                // Hide quick filters in normal mode
                if (_quickFiltersContainer != null)
                {
                    _visualService.HideQuickFilters(_quickFiltersContainer);
                }

                if (_searchContainer != null)
                {
                    _visualService.ShowSearchForNormal(_searchContainer);
                }

                if (_targetListBox != null)
                {
                    _visualService.RemoveControllerStyles(_targetListBox);
                    _focusService.DisableControllerFocus(_targetListBox);
                    _inputService.DetachFromControl(_targetListBox);
                }

                _logger?.Debug("Controller mode disabled successfully");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disabling controller mode - original functionality preserved");
            }
        }

        /// <summary>
        /// Safely finds the main ListBox in the target control
        /// </summary>
        private ListBox FindListBox(Control control)
        {
            try
            {
                return FindChildOfType<ListBox>(control);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error finding ListBox in target control");
                return null;
            }
        }

        /// <summary>
        /// Safely finds the search container (Grid containing search elements)
        /// </summary>
        private Panel FindSearchContainer(Control control)
        {
            try
            {
                // Look for a Grid that contains a TextBox (likely the search container)
                var grids = FindChildrenOfType<Grid>(control);
                foreach (var grid in grids)
                {
                    var hasTextBox = FindChildOfType<TextBox>(grid) != null;
                    var hasButton = FindChildOfType<Button>(grid) != null;
                    
                    if (hasTextBox && hasButton)
                    {
                        return grid; // Found search container
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error finding search container");
                return null;
            }
        }

        /// <summary>
        /// Safely finds the quick filters container by name
        /// </summary>
        private Panel FindQuickFiltersContainer(Control control)
        {
            try
            {
                return FindChildByName<Panel>(control, "QuickFiltersContainer");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error finding quick filters container");
                return null;
            }
        }

        /// <summary>
        /// Safely finds the controller hints container by name
        /// </summary>
        private Panel FindControllerHintsContainer(Control control)
        {
            try
            {
                return FindChildByName<Panel>(control, "ControllerHintsContainer");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error finding controller hints container");
                return null;
            }
        }

        /// <summary>
        /// Safely finds a child control by name
        /// </summary>
        private T FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            try
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    
                    if (child is T element && element.Name == name)
                        return element;

                    var childResult = FindChildByName<T>(child, name);
                    if (childResult != null)
                        return childResult;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error finding child by name: {name}");
            }

            return null;
        }

        /// <summary>
        /// Safely traverses visual tree to find a child of specific type
        /// </summary>
        private T FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            try
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    
                    if (child is T result)
                        return result;

                    var childResult = FindChildOfType<T>(child);
                    if (childResult != null)
                        return childResult;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error traversing visual tree for {typeof(T).Name}");
            }

            return null;
        }

        /// <summary>
        /// Safely finds all children of a specific type
        /// </summary>
        private List<T> FindChildrenOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            var results = new List<T>();
            if (parent == null) return results;

            try
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    
                    if (child is T match)
                        results.Add(match);

                    results.AddRange(FindChildrenOfType<T>(child));
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error finding children of type {typeof(T).Name}");
            }

            return results;
        }

        /// <summary>
        /// Toggle the On-Screen Keyboard visibility
        /// </summary>
        private void ToggleOnScreenKeyboard()
        {
            try
            {
                if (_oskContainer == null)
                {
                    _logger?.Debug("OSK container not found - keyboard unavailable");
                    return;
                }

                if (_oskVisible)
                {
                    _visualService.HideOnScreenKeyboard(_oskContainer);
                    _oskVisible = false;
                    _logger?.Debug("On-Screen Keyboard hidden");
                }
                else
                {
                    _visualService.ShowOnScreenKeyboard(_oskContainer);
                    _oskVisible = true;
                    _logger?.Debug("On-Screen Keyboard shown");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Error toggling On-Screen Keyboard");
            }
        }

        /// <summary>
        /// Safely finds the OSK container by name
        /// </summary>
        private Panel FindOSKContainer(Control control)
        {
            try
            {
                return FindChildByName<Panel>(control, "OSKContainer");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error finding OSK container");
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                Detach();
                
                _detectionService?.Dispose();
                _inputService?.Dispose();
                _focusService?.Dispose();
                _visualService?.Dispose();
                
                _logger?.Debug("ControllerOverlay disposed");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disposing ControllerOverlay");
            }
        }
    }
}