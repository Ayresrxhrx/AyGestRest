using System.Windows;

namespace AyGestRest.Views
{
    public partial class TextInputDialog : Window
    {
        public string Value => InputBox.Text.Trim();

        public TextInputDialog(string title, string placeholder = "")
        {
            InitializeComponent();
            TitleText.Text = title;
            InputBox.Text = placeholder;
            Loaded += (_, _) => InputBox.Focus();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Value)) return;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
