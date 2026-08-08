using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Views
{
    // Interaction logic for DownloadFromUrlDialog.xaml
    public partial class DownloadFromUrlDialog : UserControl
    {
        public DownloadFromUrlDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Focus the URL text box when the dialog loads
            UrlTextBox?.Focus();
        }
    }
}
