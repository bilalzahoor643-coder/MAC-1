using System.Windows.Controls;
using MAC_1.ViewModels;

namespace MAC_1.Views
{
    public partial class DownloadingSection : UserControl
    {
        public DownloadingSection()
        {
            InitializeComponent();
            DataContext = MainViewModel.Instance;
        }
    }
}
