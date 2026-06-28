using System.Windows;
using System.Windows.Controls;

namespace MAC_1.Views
{
    public partial class DownloadCard : UserControl
    {
        private void DotsButton_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = true;
        }
        public DownloadCard()
        {
            InitializeComponent(); // Ab isko error nahi dena chahiye
        }
    }
}