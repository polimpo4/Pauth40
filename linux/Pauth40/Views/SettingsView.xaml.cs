using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pauth40.Localization;
using Pauth40.Services;
using Pauth40.ViewModels;

namespace Pauth40.Views;

/// <summary>
/// Account security, e-mail recovery and encrypted database backup settings.
/// The view is embedded in the application shell, so the shell owns the
/// navigation back to the authenticator.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private readonly MainViewModel _viewModel;
    private readonly SecurityService _security;
    private readonly EmailService _email;
    private bool _suppressLanguageChange;

    public SettingsView(
        MainViewModel viewModel,
        SecurityService security,
        EmailService email)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _email = email ?? throw new ArgumentNullException(nameof(email));

        InitializeComponent();

        InitializeLanguageSelector();

        AccountEmailText.Text = _security.Email;
        LoadSmtp();

        Unloaded += OnUnloaded;
    }

    /// <summary>Raised after a backup import changed the account list.</summary>
    public event EventHandler? AccountsChanged;

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void InitializeLanguageSelector()
    {
        _suppressLanguageChange = true;

        LanguageCombo.ItemsSource = LocalizationManager.Instance.Languages;
        LanguageCombo.SelectedItem = LocalizationManager.Instance.CurrentLanguage;

        _suppressLanguageChange = false;

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
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

    private void LoadSmtp()
    {
        SmtpInput smtp = _security.GetSmtpInput();

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            SmtpOptions defaults = SmtpDefaults.ForEmail(_security.Email);
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
            NewPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!string.Equals(newPassword, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            SetStatus(PasswordStatusText, Loc.T("settings_err_password_mismatch"), true);
            ConfirmPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        try
        {
            _security.ChangePassword(CurrentPasswordBox.Password, newPassword);

            CurrentPasswordBox.Password = string.Empty;
            NewPasswordBox.Password = string.Empty;
            ConfirmPasswordBox.Password = string.Empty;

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
        if (!TryBuildSmtpInput(out SmtpInput input))
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
        if (!TryBuildSmtpInput(out SmtpInput input))
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

        if (!_security.TryGetSmtpOptions(out SmtpOptions options))
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
            SmtpHostBox.Focus(FocusState.Programmatic);
            return false;
        }

        if (!int.TryParse(SmtpPortBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            SetStatus(SmtpStatusText, Loc.T("settings_err_smtp_port"), true);
            SmtpPortBox.Focus(FocusState.Programmatic);
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

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(BackupStatusText, string.Empty, false);

        string password = BackupPasswordBox.Password;

        if (password.Length < 8)
        {
            SetStatus(BackupStatusText, Loc.T("settings_err_backup_password_length"), true);
            BackupPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        byte[] payload;

        try
        {
            payload = BackupService.Create(_viewModel.Accounts, password);
        }
        catch (Exception ex)
        {
            SetStatus(BackupStatusText, ex.Message, true);
            return;
        }

        string suggestedName = $"pauth40-backup-{DateTime.Now:yyyyMMdd-HHmm}";
        string location = await FilePickerService.SaveBackupAsync(suggestedName, payload);

        if (string.IsNullOrEmpty(location))
            return;

        SetStatus(
            BackupStatusText,
            Loc.Format("settings_status_backup_created", location),
            false);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(BackupStatusText, string.Empty, false);

        byte[]? payload = await FilePickerService.PickBackupAsync();
        if (payload is null)
            return;

        try
        {
            var imported = BackupService.Read(payload, BackupPasswordBox.Password);
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
                SetStatus(BackupStatusText, Loc.T("settings_import_none"), false);
            }
        }
        catch (Exception ex)
        {
            SetStatus(BackupStatusText, ex.Message, true);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static void SetStatus(TextBlock target, string message, bool error)
    {
        target.Text = message;
        target.Foreground = StatusBrush(error);
        target.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static Brush StatusBrush(bool error)
    {
        string key = error ? "DangerBrush" : "CyanBrush";

        try
        {
            if (Application.Current.Resources[key] is Brush brush)
                return brush;
        }
        catch
        {
            // Fall back to a literal color when the theme is not available.
        }

        return new SolidColorBrush(error
            ? Microsoft.UI.Colors.OrangeRed
            : Microsoft.UI.Colors.Cyan);
    }
}
