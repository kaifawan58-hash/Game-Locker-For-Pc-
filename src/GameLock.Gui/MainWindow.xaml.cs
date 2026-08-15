using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using GameLock.Common;

namespace GameLock.Gui
{
    public partial class MainWindow : Window
    {
        private GameLockConfig _config = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureFirstRunPassword();
            LoadConfigIntoUi();
            await RefreshStatusAsync();
        }

        // ---------- First-run password setup ----------

        private void EnsureFirstRunPassword()
        {
            if (ConfigStore.ConfigExists())
                return;

            MessageBox.Show(this,
                "No parent password is configured yet. Please create one now.\n" +
                "This password will be required every time you unlock games.",
                "GameLock - First-time setup", MessageBoxButton.OK, MessageBoxImage.Information);

            while (true)
            {
                var dlg = new PasswordPromptWindow("Create Parent Password", "New password:", confirmMode: true) { Owner = this };
                if (dlg.ShowDialog() != true)
                {
                    // Refuse to run unconfigured/unprotected - close the app.
                    MessageBox.Show(this, "A password is required to use GameLock. Exiting.", "GameLock",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Environment.Exit(1);
                    return;
                }

                if (dlg.Password1.Length < 4)
                {
                    MessageBox.Show(this, "Please choose a password at least 4 characters long.", "GameLock",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                var (hash, salt, iterations) = PasswordHasher.CreateHash(dlg.Password1);
                var cfg = new GameLockConfig
                {
                    PasswordHashBase64 = hash,
                    PasswordSaltBase64 = salt,
                    PasswordIterations = iterations
                };
                ConfigStore.Save(cfg);
                break;
            }
        }

        // ---------- Config <-> UI ----------

        private void LoadConfigIntoUi()
        {
            _config = ConfigStore.Load();
            GameListBox.ItemsSource = null;
            GameListBox.ItemsSource = _config.GamePaths;
        }

        private void SaveConfigAndNotifyService()
        {
            ConfigStore.Save(_config);
            _ = PipeClient.SendAsync(new PipeRequest { Command = PipeCommandType.ReloadConfig });
        }

        // ---------- Game list management ----------

        private void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select game executable(s)",
                Filter = "Executable files (*.exe)|*.exe",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != true) return;

            bool added = false;
            foreach (var path in dlg.FileNames)
            {
                if (!_config.GamePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                {
                    _config.GamePaths.Add(path);
                    added = true;
                }
            }

            if (added)
            {
                SaveConfigAndNotifyService();
                LoadConfigIntoUi();
            }
        }

        private void RemoveGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (GameListBox.SelectedItem is not string selected) return;

            _config.GamePaths.RemoveAll(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
            SaveConfigAndNotifyService();
            LoadConfigIntoUi();
        }

        private void ClearGamesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_config.GamePaths.Count == 0) return;

            var result = MessageBox.Show(this, "Remove ALL games from the protected list?", "GameLock",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _config.GamePaths.Clear();
            SaveConfigAndNotifyService();
            LoadConfigIntoUi();
        }

        // ---------- Lock / Unlock ----------

        private async void LockNowButton_Click(object sender, RoutedEventArgs e)
        {
            var response = await PipeClient.SendAsync(new PipeRequest { Command = PipeCommandType.LockNow });
            ShowResponse(response);
            await RefreshStatusAsync();
        }

        private async void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PasswordPromptWindow("Unlock Games", "Parent password:") { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var response = await PipeClient.SendAsync(new PipeRequest
            {
                Command = PipeCommandType.Unlock,
                Payload = dlg.Password1
            });

            ShowResponse(response);
            await RefreshStatusAsync();
        }

        // ---------- Password management ----------

        private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            _config = ConfigStore.Load();

            var currentDlg = new PasswordPromptWindow("Change Password", "Current password:") { Owner = this };
            if (currentDlg.ShowDialog() != true) return;

            bool ok = PasswordHasher.Verify(currentDlg.Password1, _config.PasswordHashBase64, _config.PasswordSaltBase64, _config.PasswordIterations);
            if (!ok)
            {
                MessageBox.Show(this, "Current password is incorrect.", "GameLock", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var newDlg = new PasswordPromptWindow("Change Password", "New password:", confirmMode: true) { Owner = this };
            if (newDlg.ShowDialog() != true) return;

            if (newDlg.Password1.Length < 4)
            {
                MessageBox.Show(this, "Please choose a password at least 4 characters long.", "GameLock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (hash, salt, iterations) = PasswordHasher.CreateHash(newDlg.Password1);
            _config.PasswordHashBase64 = hash;
            _config.PasswordSaltBase64 = salt;
            _config.PasswordIterations = iterations;
            SaveConfigAndNotifyService();

            MessageBox.Show(this, "Password changed.", "GameLock", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---------- Protection service install/uninstall ----------

        private void InstallProtectionButton_Click(object sender, RoutedEventArgs e)
        {
            var (ok, message) = InstallManager.InstallService();
            MessageBox.Show(this, message, "GameLock - Install Protection", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            _ = RefreshStatusAsync();
        }

        private void UninstallProtectionButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this,
                "This will stop enforcement entirely - games will no longer be blocked. Continue?",
                "GameLock - Uninstall Protection", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var (ok, message) = InstallManager.UninstallService();
            MessageBox.Show(this, message, "GameLock - Uninstall Protection", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Error);
            _ = RefreshStatusAsync();
        }

        // ---------- Status ----------

        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatusAsync();
        }

        private async System.Threading.Tasks.Task RefreshStatusAsync()
        {
            var response = await PipeClient.SendAsync(new PipeRequest { Command = PipeCommandType.GetStatus });

            if (!response.Success && response.Message.StartsWith("SERVICE_UNREACHABLE", StringComparison.Ordinal))
            {
                StatusText.Text = "PROTECTION SERVICE NOT INSTALLED/RUNNING";
                StatusText.Foreground = Brushes.DarkOrange;
                return;
            }

            StatusText.Text = response.Locked ? "LOCKED" : "UNLOCKED (until next restart)";
            StatusText.Foreground = response.Locked ? Brushes.DarkGreen : Brushes.DarkRed;
        }

        private void ShowResponse(PipeResponse response)
        {
            string displayMessage = response.Message.StartsWith("SERVICE_UNREACHABLE: ", StringComparison.Ordinal)
                ? response.Message["SERVICE_UNREACHABLE: ".Length..]
                : response.Message;

            MessageBox.Show(this, displayMessage, "GameLock", MessageBoxButton.OK,
                response.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
    }
}
