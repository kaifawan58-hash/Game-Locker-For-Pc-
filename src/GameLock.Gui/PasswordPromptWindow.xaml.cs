using System.Windows;

namespace GameLock.Gui
{
    public partial class PasswordPromptWindow : Window
    {
        public string Password1 => PasswordBox1.Password;
        public string Password2 => PasswordBox2.Password;
        public bool ConfirmMode { get; }

        /// <param name="title">Window title.</param>
        /// <param name="prompt1">Label above the first password box.</param>
        /// <param name="confirmMode">If true, shows a second box and requires both to match.</param>
        /// <param name="prompt2">Label above the second box (only used when confirmMode is true).</param>
        public PasswordPromptWindow(string title, string prompt1, bool confirmMode = false, string prompt2 = "Confirm password:")
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = prompt1;
            ConfirmMode = confirmMode;

            if (confirmMode)
            {
                Height = 230;
                PromptLabel2.Text = prompt2;
                PromptLabel2.Visibility = Visibility.Visible;
                PasswordBox2.Visibility = Visibility.Visible;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Password1))
            {
                MessageBox.Show(this, "Password cannot be empty.", "GameLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ConfirmMode && Password1 != Password2)
            {
                MessageBox.Show(this, "Passwords do not match.", "GameLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
