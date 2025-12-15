using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using UniPlaySong.Services.Controller;
using UniPlaySong.Common;
using Playnite.SDK;

namespace UniPlaySong.Views
{
    public partial class DownloadDialogView : UserControl
    {
        // NEW: Controller overlay (additive only - no impact on existing functionality)
        private IControllerOverlay _controllerOverlay;

        public DownloadDialogView()
        {
            InitializeComponent();
            Loaded += DownloadDialogView_Loaded;
        }

        private void DownloadDialogView_Loaded(object sender, RoutedEventArgs e)
        {
            // Update SelectedItems when selection changes
            ResultsListBox.SelectionChanged += (s, args) =>
            {
                if (DataContext is ViewModels.DownloadDialogViewModel viewModel)
                {
                    var selected = new List<ViewModels.DownloadItemViewModel>();
                    foreach (ViewModels.DownloadItemViewModel item in ResultsListBox.SelectedItems)
                    {
                        selected.Add(item);
                    }
                    viewModel.SelectedItems = selected;
                }
            };

            // Clean up preview files when dialog closes
            if (sender is System.Windows.Controls.UserControl control)
            {
                control.Unloaded += (s, args) =>
                {
                    if (DataContext is ViewModels.DownloadDialogViewModel viewModel)
                    {
                        viewModel.CleanupPreviewFiles();
                    }
                    
                    // NEW: Cleanup controller overlay (temporarily disabled)
                    // CleanupControllerSupport();
                };
            }

            // NEW: Initialize controller support (temporarily disabled for troubleshooting)
            // InitializeControllerSupport();
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.DownloadDialogViewModel viewModel)
            {
                // Get the item that was double-clicked
                var listBox = sender as ListBox;
                if (listBox != null)
                {
                    var hit = System.Windows.Media.VisualTreeHelper.HitTest(listBox, e.GetPosition(listBox));
                    var listBoxItem = FindAncestor<ListBoxItem>(hit.VisualHit);
                    if (listBoxItem != null && listBoxItem.Content is ViewModels.DownloadItemViewModel itemViewModel)
                    {
                        viewModel.HandleDoubleClick(itemViewModel);
                        e.Handled = true;
                    }
                }
            }
        }

        private static T FindAncestor<T>(System.Windows.DependencyObject current) where T : System.Windows.DependencyObject
        {
            while (current != null)
            {
                if (current is T ancestor)
                    return ancestor;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // NEW: Controller support methods (additive only - safe to fail)
        
        /// <summary>
        /// Initializes controller support safely without affecting existing functionality
        /// </summary>
        private void InitializeControllerSupport()
        {
            try
            {
                // Get logger from UniPlaySong plugin (if available)
                var logger = GetPluginLogger();
                if (logger == null)
                {
                    // Silent fail - existing functionality unaffected
                    return;
                }

                // Create services with dependency injection pattern
                var detectionService = new ControllerDetectionService(logger);
                var inputService = new ControllerInputService(logger);
                var focusService = new FocusManagementService(logger);
                var visualService = new VisualEnhancementService(logger);
                
                // Create overlay coordinator
                _controllerOverlay = new ControllerOverlay(
                    detectionService, inputService, focusService, visualService, logger);
                
                // Subscribe to controller actions
                _controllerOverlay.ActionRequested += OnControllerAction;
                
                // Attach to this dialog
                _controllerOverlay.AttachTo(this);
                
                logger.Debug("Controller support initialized successfully");
            }
            catch (System.Exception ex)
            {
                // Silent fail - existing dialog functionality continues normally
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Controller support initialization failed - dialog works normally");
            }
        }

        /// <summary>
        /// Handles controller actions mapped to dialog operations
        /// </summary>
        private void OnControllerAction(object sender, ControllerAction action)
        {
            try
            {
                var viewModel = DataContext as ViewModels.DownloadDialogViewModel;
                if (viewModel == null) return;

                switch (action)
                {
                    case ControllerAction.Confirm:
                        // Trigger confirm/download action
                        if (viewModel.ConfirmCommand?.CanExecute(null) == true)
                            viewModel.ConfirmCommand.Execute(null);
                        break;
                        
                    case ControllerAction.Cancel:
                        // Trigger cancel action
                        if (viewModel.CancelCommand?.CanExecute(null) == true)
                            viewModel.CancelCommand.Execute(null);
                        break;
                        
                    case ControllerAction.Preview:
                        // Trigger preview action
                        if (viewModel.PreviewCommand?.CanExecute(null) == true)
                            viewModel.PreviewCommand.Execute(null);
                        break;
                        
                    case ControllerAction.MultiSelect:
                        // Toggle select all
                        ToggleSelectAll(viewModel);
                        break;
                        
                    case ControllerAction.PageUp:
                        // Move selection up by 10 items
                        MoveSelectionByPages(-1);
                        break;
                        
                    case ControllerAction.PageDown:
                        // Move selection down by 10 items
                        MoveSelectionByPages(1);
                        break;
                        
                    case ControllerAction.JumpToTop:
                        // Jump to first item
                        JumpToItem(0);
                        break;
                        
                    case ControllerAction.JumpToBottom:
                        // Jump to last item
                        JumpToItem(-1);
                        break;
                        
                    case ControllerAction.ShowOSK:
                        // OSK is handled internally by ControllerOverlay
                        // This case is here for completeness but shouldn't be reached
                        break;
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, $"Error handling controller action {action} - no functional impact");
            }
        }

        /// <summary>
        /// Toggle select all/deselect all functionality
        /// </summary>
        private void ToggleSelectAll(ViewModels.DownloadDialogViewModel viewModel)
        {
            try
            {
                if (ResultsListBox == null) return;

                // Check if all items are selected
                bool allSelected = true;
                foreach (var item in ResultsListBox.Items)
                {
                    var listBoxItem = ResultsListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (listBoxItem != null && !listBoxItem.IsSelected)
                    {
                        allSelected = false;
                        break;
                    }
                }

                // If all selected, deselect all. Otherwise, select all.
                foreach (var item in ResultsListBox.Items)
                {
                    var listBoxItem = ResultsListBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                    if (listBoxItem != null)
                    {
                        listBoxItem.IsSelected = !allSelected;
                    }
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Error toggling select all");
            }
        }

        /// <summary>
        /// Move selection by pages (10 items at a time)
        /// </summary>
        private void MoveSelectionByPages(int direction)
        {
            try
            {
                if (ResultsListBox?.Items.Count > 0)
                {
                    int currentIndex = ResultsListBox.SelectedIndex;
                    int newIndex = System.Math.Max(0, System.Math.Min(ResultsListBox.Items.Count - 1, 
                        currentIndex + (direction * 10)));
                    
                    ResultsListBox.SelectedIndex = newIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Error moving selection by pages");
            }
        }

        /// <summary>
        /// Jump to specific item (0 = first, -1 = last)
        /// </summary>
        private void JumpToItem(int index)
        {
            try
            {
                if (ResultsListBox?.Items.Count > 0)
                {
                    if (index < 0)
                        index = ResultsListBox.Items.Count - 1;
                    
                    index = System.Math.Max(0, System.Math.Min(ResultsListBox.Items.Count - 1, index));
                    
                    ResultsListBox.SelectedIndex = index;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Error jumping to item");
            }
        }

        /// <summary>
        /// Safely gets the plugin logger
        /// </summary>
        private ILogger GetPluginLogger()
        {
            try
            {
                // Use LogManager to get logger (safer than accessing plugin instance)
                return LogManager.GetLogger();
            }
            catch
            {
                // Silent fail - controller features just won't work
                return null;
            }
        }

        /// <summary>
        /// Cleans up controller support safely
        /// </summary>
        private void CleanupControllerSupport()
        {
            try
            {
                if (_controllerOverlay != null)
                {
                    _controllerOverlay.ActionRequested -= OnControllerAction;
                    _controllerOverlay.Dispose();
                    _controllerOverlay = null;
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Error cleaning up controller support - no functional impact");
            }
        }
        
        /// <summary>
        /// Called when OSK text is confirmed - updates search term and performs search
        /// </summary>
        private void OnOSKTextConfirmed(string text)
        {
            try
            {
                if (DataContext is ViewModels.DownloadDialogViewModel viewModel && !string.IsNullOrWhiteSpace(text))
                {
                    // Update search term
                    viewModel.SearchTerm = text;
                    
                    // Perform the search
                    if (viewModel.SearchCommand?.CanExecute(null) == true)
                    {
                        viewModel.SearchCommand.Execute(null);
                    }
                    
                    var logger = GetPluginLogger();
                    logger?.Debug($"OSK search confirmed: {text}");
                }
            }
            catch (System.Exception ex)
            {
                var logger = GetPluginLogger();
                logger?.Debug(ex, "Error handling OSK text confirmation");
            }
        }
    }
}

