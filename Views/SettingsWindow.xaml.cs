using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Pauth40.Localization;
using Pauth40.ViewModels;

namespace Pauth40.Views;

/// <summary>
/// Account security, e-mail recovery and encrypted database backup settings.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SecurityService _security;
    private readonly EmailService _email;
    private bool _suppressLanguageChange;

    public SettingsWindow(
        MainViewModel viewModel,
        SecurityService security,
        EmailService email)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _email = email ?? throw new ArgumentNullException(nameof(email));

        InitializeComponent();

        VersionText.Text = AppVersion.Display;

        Height = Math.Min(820, Math.Max(560, SystemParameters.WorkArea.Height - 48));

        InitializeLanguageSelector();

        AccountEmailText.Text = _security.Email;
        LoadSmtp();
    }

    private void InitializeLanguageSelector()
    {
        _suppressLanguageChange = true;

        LanguageCombo.ItemsSource = LocalizationManager.Instance.Languages;
        LanguageCombo.SelectedItem = LocalizationManager.Instance.CurrentLanguage;

        _suppressLanguageChange = false;

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _suppressLanguageChange = true;
        LanguageCombo.SelectedItem = LocalizationManager.Instance.CurrentLanguage;
        _suppressLanguageChange = false;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange || LanguageCombo.SelectedItem is not LanguageOption option)
            return;

        LocalizationManager.Instance.SetLanguage(option.Code);
    }

    /// <summary>Raised after a backup import changed the account list.</summary>
    public event EventHandler? AccountsChanged;

    private void LoadSmtp()
    {
        SmtpInput smtp = _security.GetSmtpInput();

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            var defaults = SmtpDefaults.ForEmail(_security.Email);
            smtp = new SmtpInput(
                defaults.Host,
                defaults.Port,
                true,
                defaults.UserName,
                _security.Email,
                string.Empty);
        }

        SmtpHostBox.Text = smtp.Host;
        SmtpPortBox.Text = smtp.Port <= 0 ? "587" : smtp.Port.ToString();
        SmtpUserBox.Text = smtp.UserName;
        SmtpFromBox.Text = string.IsNullOrWhiteSpace(smtp.From) ? _security.Email : smtp.From;
        SmtpPasswordBox.Password = smtp.Password ?? string.Empty;
        SmtpSslCheckBox.IsChecked = smtp.UseSsl;
    }

    // ---------------------------------------------------------------- password

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(PasswordStatusText, string.Empty, false);

        string newPassword = NewPasswordBox.Password;

        if (newPassword.Length < 8)
        {
            SetStatus(PasswordStatusText, Loc.T("settings_err_new_password_length"), true);
            NewPasswordBox.Focus();
            return;
        }

        if (!string.Equals(newPassword, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            SetStatus(PasswordStatusText, Loc.T("settings_err_password_mismatch"), true);
            ConfirmPasswordBox.SelectAll();
            ConfirmPasswordBox.Focus();
            return;
        }

        try
        {
            _security.ChangePassword(CurrentPasswordBox.Password, newPassword);

            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();

            SetStatus(PasswordStatusText, Loc.T("settings_status_password_changed"), false);
        }
        catch (Exception ex)
        {
            SetStatus(PasswordStatusText, ex.Message, true);
        }
    }

    // ---------------------------------------------------------------- e-mail

    private void SaveSmtp_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSmtpInput(out var input))
            return;

        try
        {
            _security.UpdateSmtp(input);
            SetStatus(SmtpStatusText, Loc.T("settings_status_email_saved"), false);
        }
        catch (Exception ex)
        {
            SetStatus(SmtpStatusText, ex.Message, true);
        }
    }

    private async void TestSmtp_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSmtpInput(out var input))
            return;

        if (!SecurityService.IsValidEmail(_security.Email))
        {
            SetStatus(SmtpStatusText, Loc.T("settings_err_account_email"), true);
            return;
        }

        try
        {
            _security.UpdateSmtp(input);
        }
        catch (Exception ex)
        {
            SetStatus(SmtpStatusText, ex.Message, true);
            return;
        }

        if (!_security.TryGetSmtpOptions(out var options))
        {
            SetStatus(SmtpStatusText, Loc.T("settings_err_email_incomplete"), true);
            return;
        }

        TestSmtpButton.IsEnabled = false;
        SetStatus(SmtpStatusText, Loc.T("settings_status_test_sending"), false);

        try
        {
            await _email.SendTestAsync(options, _security.Email);
            SetStatus(SmtpStatusText, Loc.T("settings_status_test_sent"), false);
        }
        catch (Exception ex)
        {
            SetStatus(SmtpStatusText, ex.Message, true);
        }
        finally
        {
            TestSmtpButton.IsEnabled = true;
        }
    }

    private bool TryBuildSmtpInput(out SmtpInput input)
    {
        input = default!;

        string host = SmtpHostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            SetStatus(SmtpStatusText, Loc.T("settings_err_smtp_host"), true);
            SmtpHostBox.Focus();
            return false;
        }

        if (!int.TryParse(SmtpPortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            SetStatus(SmtpStatusText, Loc.T("settings_err_smtp_port"), true);
            SmtpPortBox.Focus();
            return false;
        }

        string from = SmtpFromBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(from))
            from = _security.Email;

        string user = SmtpUserBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(user))
            user = from;

        input = new SmtpInput(
            host,
            port,
            SmtpSslCheckBox.IsChecked == true,
            user,
            from,
            SmtpPasswordBox.Password);

        return true;
    }

    // ---------------------------------------------------------------- backup

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(BackupStatusText, string.Empty, false);

        string password = BackupPasswordBox.Password;

        if (password.Length < 8)
        {
            SetStatus(BackupStatusText, Loc.T("settings_err_backup_password_length"), true);
            BackupPasswordBox.Focus();
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("settings_dialog_export_title"),
            Filter = Loc.T("settings_dialog_filter"),
            DefaultExt = ".pauth40",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"pauth40-backup-{DateTime.Now:yyyyMMdd-HHmm}.pauth40"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            BackupService.Export(dialog.FileName, _viewModel.Accounts, password);
            SetStatus(BackupStatusText, Loc.Format("settings_status_backup_created", dialog.FileName), false);
        }
        catch (Exception ex)
        {
            SetStatus(BackupStatusText, ex.Message, true);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(BackupStatusText, string.Empty, false);

        var dialog = new OpenFileDialog
        {
            Title = Loc.T("settings_dialog_import_title"),
            Filter = Loc.T("settings_dialog_filter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var imported = BackupService.Import(dialog.FileName, BackupPasswordBox.Password);
            int added = _viewModel.MergeAccounts(imported);

            if (added > 0)
            {
                AccountsChanged?.Invoke(this, EventArgs.Empty);
                SetStatus(
                    BackupStatusText,
                    added == 1
                        ? Loc.T("settings_import_one")
                        : Loc.Format("settings_import_many", added),
                    false);
            }
            else
            {
                SetStatus(
                    BackupStatusText,
                    Loc.T("settings_import_none"),
                    false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(BackupStatusText, ex.Message, true);
        }
    }

    // ---------------------------------------------------------------- helpers

    private void SetStatus(TextBlock target, string message, bool error)
    {
        target.Text = message;
        target.Foreground = (Brush)FindResource(error ? "DangerBrush" : "CyanBrush");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse was released between the event and DragMove.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
