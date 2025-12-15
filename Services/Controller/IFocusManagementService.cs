using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Service for managing focus behavior for controller navigation
    /// </summary>
    public interface IFocusManagementService : IDisposable
    {
        /// <summary>
        /// Enables controller-friendly focus behavior for a ListBox
        /// </summary>
        /// <param name="listBox">The ListBox to enhance</param>
        void EnableControllerFocus(ListBox listBox);
        
        /// <summary>
        /// Disables controller focus and restores original behavior
        /// </summary>
        /// <param name="listBox">The ListBox to restore</param>
        void DisableControllerFocus(ListBox listBox);
        
        /// <summary>
        /// Restores focus to the ListBox (useful after dialog operations)
        /// </summary>
        /// <param name="listBox">The ListBox to focus</param>
        void RestoreFocus(ListBox listBox);
        
        /// <summary>
        /// Gets whether controller focus is currently enabled for a ListBox
        /// </summary>
        /// <param name="listBox">The ListBox to check</param>
        /// <returns>True if controller focus is enabled</returns>
        bool IsControllerFocusEnabled(ListBox listBox);
        
        /// <summary>
        /// Ensures the selected item is visible in the ListBox
        /// </summary>
        /// <param name="listBox">The ListBox to scroll</param>
        void EnsureSelectedItemVisible(ListBox listBox);
    }
}