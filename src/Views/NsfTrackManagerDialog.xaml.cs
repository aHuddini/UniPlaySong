using System.Windows.Controls;
using UniPlaySong.ViewModels;

namespace UniPlaySong.Views
{
    public partial class NsfTrackManagerDialog : UserControl
    {
        public NsfTrackManagerDialog()
        {
            InitializeComponent();
        }

        public void Initialize(NsfTrackManagerViewModel vm)
        {
            DataContext = vm;
        }
    }
}
