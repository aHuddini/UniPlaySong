using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
        }

        // Opens the hyperlink URI in the user's default browser.
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
                // Silent: if the OS can't open a browser we'd rather do nothing than throw into the settings host.
            }
        }
    }
}
