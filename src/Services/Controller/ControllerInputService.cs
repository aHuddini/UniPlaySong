using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Service for handling controller input and mapping to actions
    /// Provides safe input handling with graceful degradation
    /// </summary>
    public class ControllerInputService : IControllerInputService
    {
        private readonly ILogger _logger;
        private readonly Dictionary<Control, bool> _attachedControls = new Dictionary<Control, bool>();
        
        public event EventHandler<ControllerAction> ActionRequested;
        
        public bool IsAttached => _attachedControls.Count > 0;

        public ControllerInputService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void AttachToControl(Control control)
        {
            if (control == null)
            {
                _logger?.Warn("Cannot attach to null control");
                return;
            }

            try
            {
                if (_attachedControls.ContainsKey(control))
                {
                    _logger?.Debug($"Already attached to {control.Name ?? control.GetType().Name}");
                    return;
                }

                control.PreviewKeyDown += OnControllerInput;
                _attachedControls[control] = true;
                
                _logger?.Debug($"Controller input attached to {control.Name ?? control.GetType().Name}");
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed to attach controller input to {control.Name ?? control.GetType().Name} - normal input will work");
            }
        }

        public void DetachFromControl(Control control)
        {
            if (control == null) return;

            try
            {
                if (_attachedControls.ContainsKey(control))
                {
                    control.PreviewKeyDown -= OnControllerInput;
                    _attachedControls.Remove(control);
                    
                    _logger?.Debug($"Controller input detached from {control.Name ?? control.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error detaching controller input from {control.Name ?? control.GetType().Name} - no functional impact");
            }
        }

        private void OnControllerInput(object sender, KeyEventArgs e)
        {
            try
            {
                var action = MapKeyToAction(e.Key);
                if (action != ControllerAction.None)
                {
                    _logger?.Debug($"Controller action mapped: {e.Key} -> {action}");
                    
                    // Fire the action event
                    ActionRequested?.Invoke(this, action);
                    
                    // Mark as handled only if we successfully mapped and fired the action
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Controller input mapping failed - letting normal input handling proceed");
                // Don't set e.Handled = true to allow normal input processing
            }
        }

        private ControllerAction MapKeyToAction(Key key)
        {
            try
            {
                // Use C# 7.3 compatible syntax instead of switch expressions
                switch (key)
                {
                    // A Button / Enter - Primary confirm action
                    case Key.Enter:
                    case Key.Space:
                        return ControllerAction.Confirm;
                    
                    // B Button / Escape - Cancel/Back action  
                    case Key.Escape:
                        return ControllerAction.Cancel;
                    
                    // Y Button - Preview action
                    case Key.Y:
                        return ControllerAction.Preview;
                    
                    // X Button - Multi-select toggle
                    case Key.X:
                        return ControllerAction.MultiSelect;
                    
                    // Page navigation (shoulder buttons)
                    case Key.PageUp:
                        return ControllerAction.PageUp;
                    case Key.PageDown:
                        return ControllerAction.PageDown;
                    
                    // Jump navigation (triggers)
                    case Key.Home:
                        return ControllerAction.JumpToTop;
                    case Key.End:
                        return ControllerAction.JumpToBottom;
                    
                    // F1 or F3 could be mapped to search (since controllers often map special functions to F keys)
                    case Key.F1:
                        return ControllerAction.Search;
                    case Key.F3:
                        return ControllerAction.ShowOSK;
                    
                    // D-pad and arrow keys are handled by ListBox naturally, so we don't intercept them
                    default:
                        return ControllerAction.None;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Error mapping key {key} to action");
                return ControllerAction.None;
            }
        }

        /// <summary>
        /// Gets a user-friendly description of the controller mapping
        /// </summary>
        /// <param name="action">The controller action</param>
        /// <returns>Description string for UI hints</returns>
        public static string GetActionDescription(ControllerAction action)
        {
            switch (action)
            {
                case ControllerAction.Confirm:
                    return "Confirm";
                case ControllerAction.Cancel:
                    return "Cancel";
                case ControllerAction.Preview:
                    return "Preview";
                case ControllerAction.MultiSelect:
                    return "Select All";
                case ControllerAction.PageUp:
                    return "Page Up";
                case ControllerAction.PageDown:
                    return "Page Down";
                case ControllerAction.JumpToTop:
                    return "Jump to Top";
                case ControllerAction.JumpToBottom:
                    return "Jump to Bottom";
                case ControllerAction.Search:
                    return "Search";
                case ControllerAction.ShowOSK:
                    return "Keyboard";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Gets the controller button name for UI hints
        /// </summary>
        /// <param name="action">The controller action</param>
        /// <returns>Button name (A, B, X, Y, etc.)</returns>
        public static string GetButtonName(ControllerAction action)
        {
            switch (action)
            {
                case ControllerAction.Confirm:
                    return "A";
                case ControllerAction.Cancel:
                    return "B";
                case ControllerAction.Preview:
                    return "Y";
                case ControllerAction.MultiSelect:
                    return "X";
                case ControllerAction.PageUp:
                    return "LB";
                case ControllerAction.PageDown:
                    return "RB";
                case ControllerAction.JumpToTop:
                    return "LT";
                case ControllerAction.JumpToBottom:
                    return "RT";
                case ControllerAction.Search:
                    return "F1";
                case ControllerAction.ShowOSK:
                    return "F3";
                default:
                    return "?";
            }
        }

        public void Dispose()
        {
            try
            {
                // Detach from all controls
                var controlsToDetach = new List<Control>(_attachedControls.Keys);
                foreach (var control in controlsToDetach)
                {
                    DetachFromControl(control);
                }
                
                _attachedControls.Clear();
                _logger?.Debug("ControllerInputService disposed");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Error disposing ControllerInputService - no functional impact");
            }
        }
    }
}