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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch
            {
                // Silent: if the OS cannot open a browser we would rather do nothing than throw
                // into the settings host.
            }
        }
    }
}
