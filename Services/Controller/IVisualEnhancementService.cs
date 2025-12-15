using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    /// <summary>
    /// Service for applying and managing controller-specific visual enhancements
    /// </summary>
    public interface IVisualEnhancementService : IDisposable
    {
        /// <summary>
        /// Applies controller-friendly visual styles to a ListBox
        /// </summary>
        /// <param name="listBox">The ListBox to enhance</param>
        void ApplyControllerStyles(ListBox listBox);
        
        /// <summary>
        /// Removes controller styles and restores original appearance
        /// </summary>
        /// <param name="listBox">The ListBox to restore</param>
        void RemoveControllerStyles(ListBox listBox);
        
        /// <summary>
        /// Shows controller button hints panel
        /// </summary>
        /// <param name="hintsPanel">The panel containing button hints</param>
        void ShowControllerHints(Panel hintsPanel);
        
        /// <summary>
        /// Hides controller button hints panel
        /// </summary>
        /// <param name="hintsPanel">The panel containing button hints</param>
        void HideControllerHints(Panel hintsPanel);
        
        /// <summary>
        /// Hides search elements for controller mode
        /// </summary>
        /// <param name="searchContainer">The container holding search elements</param>
        void HideSearchForController(Panel searchContainer);
        
        /// <summary>
        /// Shows search elements for normal mode
        /// </summary>
        /// <param name="searchContainer">The container holding search elements</param>
        void ShowSearchForNormal(Panel searchContainer);
        
        /// <summary>
        /// Gets whether controller styles are currently applied to a ListBox
        /// </summary>
        /// <param name="listBox">The ListBox to check</param>
        /// <returns>True if controller styles are applied</returns>
        bool AreControllerStylesApplied(ListBox listBox);
        
        /// <summary>
        /// Shows quick filter buttons for controller mode
        /// </summary>
        /// <param name="filtersContainer">The container holding quick filter elements</param>
        void ShowQuickFilters(Panel filtersContainer);
        
        /// <summary>
        /// Hides quick filter buttons for normal mode
        /// </summary>
        /// <param name="filtersContainer">The container holding quick filter elements</param>
        void HideQuickFilters(Panel filtersContainer);
        
        /// <summary>
        /// Shows On-Screen Keyboard for controller text input
        /// </summary>
        /// <param name="oskContainer">The container that will hold the OSK</param>
        void ShowOnScreenKeyboard(Panel oskContainer);
        
        /// <summary>
        /// Hides On-Screen Keyboard
        /// </summary>
        /// <param name="oskContainer">The container holding the OSK</param>
        void HideOnScreenKeyboard(Panel oskContainer);
    }
}