using System;
using System.Windows.Controls;

namespace UniPlaySong.Services.Controller
{
    // Service for managing focus behavior for controller navigation
    public interface IFocusManagementService : IDisposable
    {
        void EnableControllerFocus(ListBox listBox);

        // Restores the ListBox's original focus behaviour.
        void DisableControllerFocus(ListBox listBox);

        // Useful after dialog operations.
        void RestoreFocus(ListBox listBox);

        bool IsControllerFocusEnabled(ListBox listBox);

        void EnsureSelectedItemVisible(ListBox listBox);
    }
}
