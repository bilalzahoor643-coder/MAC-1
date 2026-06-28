using System.Windows.Controls;
using FontAwesome.WPF;

namespace MAC_1.Views
{
    public partial class EmptyStateSection : UserControl
    {
        public EmptyStateSection()
        {
            InitializeComponent();
        }

        public void SetPage(string title, string emptyTitle, string emptySubtitle, FontAwesomeIcon icon)
        {
            PageTitle.Text = title;
            EmptyTitle.Text = emptyTitle;
            EmptySubtitle.Text = emptySubtitle;
            EmptyIcon.Icon = icon;
        }
    }
}
