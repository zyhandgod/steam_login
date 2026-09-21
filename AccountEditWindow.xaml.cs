using System.Windows;
using System.Windows.Input;
using SteamLoginLite.Models;

namespace SteamLoginLite
{
    public partial class AccountEditWindow : Window
    {
        public string Username => UsernameBox.Text.Trim();
        public string Password => PasswordBox.Password;
        public string Email => EmailBox.Text.Trim().Replace("\\@", "@");
        public string EmailPassword => EmailPasswordBox.Password;
        public string GameId => GameIdBox.Text.Trim();
        public string Note => NoteBox.Text.Trim();

        public AccountEditWindow(AccountRecord account)
        {
            InitializeComponent();
            UsernameBox.Text = account.Username;
            EmailBox.Text = account.Email;
            GameIdBox.Text = account.GameId;
            NoteBox.Text = account.Note;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (Username.Length == 0)
            {
                MessageBox.Show("Steam 账号不能为空。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameBox.Focus();
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
