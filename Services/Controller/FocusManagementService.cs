using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Stores original focus state for restoration
    /// </summary>
    internal class FocusState
    {
        public int OriginalTabIndex { get; set; }
        public bool OriginalFocusable { get; set; }
        public int OriginalSelectedIndex { get; set; }
    }

    /// <summary>
    /// Service for managing focus behavior optimized for controller navigation
    /// Preserves original state for safe restoration
    /// </summary>
    public class FocusManagementService : IFocusManagementService
    {
        private readonly ILogger _logger;
        private readonly Dictionary<ListBox, FocusState> _originalStates = new Dictionary<ListBox, FocusState>();
        private readonly Dictionary<ListBox, bool> _controllerFocusEnabled = new Dictionary<ListBox, bool>();

        public FocusManagementService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void EnableControllerFocus(ListBox listBox)
        {
            if (listBox == null)
            {
                _logger?.Warn("Cannot enable controller focus on null ListBox");
                return;
            }

            try
            {
                // Don't re-enable if already enabled
                if (_controllerFocusEnabled.ContainsKey(listBox) && _controllerFocusEnabled[listBox])
                {
                    _logger?.Debug($"Controller focus already enabled for {listBox.Name ?? "ListBox"}");
                    return;
                }

                // Save original state for restoration
                _originalStates[listBox] = new FocusState
                {
                    OriginalTabIndex = listBox.TabIndex,
                    OriginalFocusable = listBox.Focusable,
                    OriginalSelectedIndex = listBox.SelectedIndex
                };

                // Apply controller-friendly focus settings
                listBox.Focusable = true;
                listBox.TabIndex = 0; // Make it first in tab order
                
                // Focus the ListBox immediately
                FocusListBox(listBox);
                
                // Select first item if nothing is selected
                if (listBox.Items.Count > 0 && listBox.SelectedIndex < 0)
                {
                    listBox.SelectedIndex = 0;
                }

                _controllerFocusEnabled[listBox] = true;
                _logger?.Debug($"Controller focus enabled for {listBox.Name ?? "ListBox"}");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to enable controller focus for {listBox.Name ?? "ListBox"} - normal focus behavior will continue");
            }
        }

        public void DisableControllerFocus(ListBox listBox)
        {
            if (listBox == null) return;

            try
            {
                if (_originalStates.TryGetValue(listBox, out var state))
                {
                    // Restore original state
                    listBox.TabIndex = state.OriginalTabIndex;
                    listBox.Focusable = state.OriginalFocusable;
                    // Note: Don't restore SelectedIndex as user may have made selections
                    
                    _originalStates.Remove(listBox);
                    _controllerFocusEnabled.Remove(listBox);
                    
                    _logger?.Debug($"Controller focus disabled and state restored for {listBox.Name ?? "ListBox"}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error disabling controller focus for {listBox.Name ?? "ListBox"} - no functional impact");
            }
        }

        public void RestoreFocus(ListBox listBox)
        {
            if (listBox == null) return;

            try
            {
                if (_controllerFocusEnabled.ContainsKey(listBox) && _controllerFocusEnabled[listBox])
                {
                    FocusListBox(listBox);
                    _logger?.Debug($"Focus restored to {listBox.Name ?? "ListBox"}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error restoring focus to {listBox.Name ?? "ListBox"} - no functional impact");
            }
        }

        public bool IsControllerFocusEnabled(ListBox listBox)
        {
            return _controllerFocusEnabled.ContainsKey(listBox) && _controllerFocusEnabled[listBox];
        }

        public void EnsureSelectedItemVisible(ListBox listBox)
        {
            if (listBox == null) return;

            try
            {
                if (listBox.SelectedItem != null)
                {
                    // Use Dispatcher to ensure this happens after layout updates
                    listBox.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            listBox.ScrollIntoView(listBox.SelectedItem);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Error scrolling selected item into view");
                        }
                    }, DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error ensuring selected item is visible");
            }
        }

        /// <summary>
        /// Safely focuses a ListBox with retry logic
        /// </summary>
        private void FocusListBox(ListBox listBox)
        {
            try
            {
                // Try immediate focus first
                bool focused = listBox.Focus();
                if (!focused)
                {
                    // If immediate focus failed, try using Keyboard.Focus
                    Keyboard.Focus(listBox);
                }

                // If still not focused, try with dispatcher (for timing issues)
                if (!listBox.IsFocused && !listBox.IsKeyboardFocused)
                {
                    listBox.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            listBox.Focus();
                            Keyboard.Focus(listBox);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Delayed focus attempt failed");
                        }
                    }, DispatcherPriority.Input);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error focusing ListBox");
            }
        }

        public void Dispose()
        {
            try
            {
                // Restore all ListBoxes to original state
                var listBoxesToRestore = new List<ListBox>(_originalStates.Keys);
                foreach (var listBox in listBoxesToRestore)
                {
                    DisableControllerFocus(listBox);
                }
                
                _originalStates.Clear();
                _controllerFocusEnabled.Clear();
                
                _logger?.Debug("FocusManagementService disposed");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disposing FocusManagementService");
            }
        }
    }
}