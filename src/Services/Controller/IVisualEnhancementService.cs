using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    // Service for applying and managing controller-specific visual enhancements
    public interface IVisualEnhancementService : IDisposable
    {
        void ApplyControllerStyles(ListBox listBox);

        // Restores the ListBox's original appearance.
        void RemoveControllerStyles(ListBox listBox);

        void ShowControllerHints(Panel hintsPanel);

        void HideControllerHints(Panel hintsPanel);

        void HideSearchForController(Panel searchContainer);

        void ShowSearchForNormal(Panel searchContainer);

        bool AreControllerStylesApplied(ListBox listBox);

        void ShowQuickFilters(Panel filtersContainer);

        void HideQuickFilters(Panel filtersContainer);

        void ShowOnScreenKeyboard(Panel oskContainer);

        void HideOnScreenKeyboard(Panel oskContainer);
    }
}
