using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UniPlaySong.Models;
using UniPlaySong.ViewModels;

namespace UniPlaySong.Views
{
    /// <summary>
    /// Code-behind for BatchManualDownloadDialog.xaml
    /// </summary>
    public partial class BatchManualDownloadDialog : UserControl
    {
        public BatchManualDownloadDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handle game item click to trigger selection command
        /// </summary>
        private void GameItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is GameDownloadItem gameItem)
            {
                if (!gameItem.IsClickable) return;

                var viewModel = DataContext as BatchManualDownloadViewModel;
                if (viewModel?.SelectGameCommand?.CanExecute(gameItem) == true)
                {
                    viewModel.SelectGameCommand.Execute(gameItem);
                }
            }
        }

        /// <summary>
        /// Handle album item click to trigger selection command
        /// </summary>
        private void AlbumItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is Album album)
            {
                var viewModel = DataContext as BatchManualDownloadViewModel;
                if (viewModel?.SelectAlbumCommand?.CanExecute(album) == true)
                {
                    viewModel.SelectAlbumCommand.Execute(album);
                }
            }
        }
    }
}
