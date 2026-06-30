using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MAC_1.Views
{
    public partial class NewCategoryPopup : Window
    {
        private const string PlaceholderText = "Enter category name...";

        public string CategoryName { get; private set; } = string.Empty;
        public string DownloadLocation { get; private set; } = string.Empty;
        public bool UseAsDefaultLocation { get; private set; } = true;

        public NewCategoryPopup()
        {
            InitializeComponent();

            CloseButton.Click += (s, e) => { DialogResult = false; Close(); };
            CancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            BrowseButton.Click += BrowseButton_Click;
            CreateCategoryButton.Click += CreateCategoryButton_Click;
            AddNewCategoryButton.Click += AddNewCategoryButton_Click;

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            foreach (var child in FindDeleteButtons(CategoriesPanel))
            {
                child.Click += DeleteCategoryButton_Click;
            }
        }

        private static System.Collections.Generic.IEnumerable<Button> FindDeleteButtons(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn && btn.Tag != null)
                    yield return btn;
                foreach (var nested in FindDeleteButtons(child))
                    yield return nested;
            }
        }

        private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var name = btn.Tag?.ToString() ?? "this category";
                var result = MessageBox.Show($"Delete category \"{name}\"?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    if (btn.Parent is Grid grid && grid.Parent is Border row && row.Parent == CategoriesPanel)
                        CategoriesPanel.Children.Remove(row);
                }
            }
        }

        private void AddNewCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            CategoryNameTextBox.Focus();
            CategoryNameTextBox.SelectAll();
        }

        private void CategoryNameTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (CategoryNameTextBox.Text == PlaceholderText)
            {
                CategoryNameTextBox.Text = string.Empty;
                CategoryNameTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
            }
        }

        private void CategoryNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CategoryNameTextBox.Text))
            {
                CategoryNameTextBox.Text = PlaceholderText;
                CategoryNameTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Download Location",
                InitialDirectory = DownloadLocationTextBox.Text
            };
            if (dialog.ShowDialog() == true)
                DownloadLocationTextBox.Text = dialog.FolderName;
        }

        private void CreateCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            var name = CategoryNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(name) || name == PlaceholderText)
            {
                MessageBox.Show("Please enter a category name.", "Category Name Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CategoryNameTextBox.Focus();
                return;
            }
            CategoryName = name.Trim();
            DownloadLocation = DownloadLocationTextBox.Text.Trim();
            UseAsDefaultLocation = UseAsDefaultCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}
