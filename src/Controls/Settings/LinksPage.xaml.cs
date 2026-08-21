using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class LinksPage : UserControl
    {
        public LinksPage()
        {
            InitializeComponent();
        }

        // Opens the URI in the user's default browser.
        private void AboutHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Open(e.Uri.AbsoluteUri);
            e.Handled = true;
        }

        // For link controls that are not Hyperlinks — the Ko-fi pill is a Button, because a
        // Hyperlink is an Inline and cannot host the Border the pill is made of. URL on Tag.
        private void OpenLink_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var url = (sender as System.Windows.FrameworkElement)?.Tag as string;
            if (!string.IsNullOrWhiteSpace(url)) Open(url);
        }

        private static void Open(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Silent: if the OS cannot open a browser we would rather do nothing than throw
                // into the settings host.
            }
        }
    }
}
