using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pauth40.Localization;
using Windows.System;

namespace Pauth40.Views;

/// <summary>
/// Gate shown before the authenticator. It creates the protected vault, unlocks
/// it with the master password, or resets the password through an e-mail code
/// or the device protected recovery copy.
/// </summary>
public sealed partial class SecurityView : UserControl
{
    private enum Mode
    {
        Setup,
        Unlock,
        Reset
    }

    private readonly SecurityService _security;
    private readonly EmailService _email;
    private readonly DispatcherTimer _cooldownTimer;

    private int _cooldown;
    private bool _useLocalReset;
    private bool _suppressLanguageChange;
    private Mode _mode;

    public SecurityView(SecurityService security, EmailService email)
    {
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _email = email ?? throw new ArgumentNullException(nameof(email));

        InitializeComponent();

        InitializeLanguageSelector();

        _cooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cooldownTimer.Tick += OnCooldownTick;

        Unloaded += OnUnloaded;

        ShowMode(_security.IsConfigured ? Mode.Unlock : Mode.Setup);
    }

    /// <summary>Raised when the vault was created, unlocked or recovered.</summary>
    public event EventHandler? Authenticated;

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cooldownTimer.Stop();
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

        ApplyLanguageTexts();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange || LanguageCombo.SelectedItem is not LanguageOption option)
            return;

        LocalizationManager.Instance.SetLanguage(option.Code);
    }

    private void ApplyLanguageTexts()
    {
        ModeTitleText.Text = GetModeTitle();

        if (_mode != Mode.Reset)
            return;

        if (_useLocalReset)
        {
            ResetSubtitleText.Text = Loc.T("security_reset_dpapi_subtitle");
            LocalNoteText.Text = Loc.T("security_reset_local_note");
        }
        else
        {
            ResetSubtitleText.Text = Loc.Format("security_reset_subtitle", _security.MaskedEmail());
        }
    }

    private string GetModeTitle() => _mode switch
    {
        Mode.Setup => Loc.T("security_title_setup"),
        Mode.Unlock => Loc.T("security_title_unlock"),
        _ => Loc.T("security_title_reset")
    };

    private void ShowMode(Mode mode)
    {
        _mode = mode;
        _useLocalReset = false;

        ModeTitleText.Text = GetModeTitle();

        SetupPanel.Visibility = mode == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
        UnlockPanel.Visibility = mode == Mode.Unlock ? Visibility.Visible : Visibility.Collapsed;
        ResetPanel.Visibility = mode == Mode.Reset ? Visibility.Visible : Visibility.Collapsed;

        ClearErrors();

        switch (mode)
        {
            case Mode.Setup:
                SetupEmailBox.Focus(FocusState.Programmatic);
                break;

            case Mode.Unlock:
                UnlockEmailText.Text = _security.MaskedEmail();
                UnlockPasswordBox.Password = string.Empty;
                UnlockPasswordBox.Focus(FocusState.Programmatic);
                break;

            case Mode.Reset:
                ResetSubtitleText.Text =
                    Loc.Format("security_reset_subtitle", _security.MaskedEmail());
                ResetEmailArea.Visibility = Visibility.Visible;
                ResetPasswordArea.Visibility = Visibility.Visible;
                LocalResetLink.Visibility =
                    _security.HasLocalRecovery ? Visibility.Visible : Visibility.Collapsed;
                LocalNoteText.Visibility = Visibility.Collapsed;
                ResetCodeBox.Text = string.Empty;
                ResetPasswordBox.Password = string.Empty;
                ResetConfirmBox.Password = string.Empty;
                ResetStatusText.Text = string.Empty;
                ResetStatusText.Visibility = Visibility.Collapsed;

                if (_security.HasSmtp)
                {
                    ResetSmtpPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PrefillResetSmtp();
                    ResetSmtpPanel.Visibility = Visibility.Visible;
                }

                ResetPasswordBox.Focus(FocusState.Programmatic);
                break;
        }
    }

    private void ClearErrors()
    {
        HideError(SetupErrorText);
        HideError(UnlockErrorText);
        HideError(ResetErrorText);
    }

    private void Complete() => Authenticated?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- setup

    private void SetupSmtp_Checked(object sender, RoutedEventArgs e)
    {
        SetupSmtpPanel.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(SetupSmtpHostBox.Text))
            PrefillSetupSmtp();
    }

    private void SetupSmtp_Unchecked(object sender, RoutedEventArgs e) =>
        SetupSmtpPanel.Visibility = Visibility.Collapsed;

    private void SetupEmail_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SetupSmtpPanel.Visibility == Visibility.Visible &&
            string.IsNullOrWhiteSpace(SetupSmtpHostBox.Text))
        {
            PrefillSetupSmtp();
        }
    }

    private void PrefillSetupSmtp()
    {
        SmtpOptions defaults = SmtpDefaults.ForEmail(SetupEmailBox.Text);

        if (string.IsNullOrWhiteSpace(SetupSmtpHostBox.Text))
            SetupSmtpHostBox.Text = defaults.Host;

        if (string.IsNullOrWhiteSpace(SetupSmtpPortBox.Text))
            SetupSmtpPortBox.Text = defaults.Port.ToString();

        if (string.IsNullOrWhiteSpace(SetupSmtpUserBox.Text))
            SetupSmtpUserBox.Text = SetupEmailBox.Text.Trim();
    }

    private void Setup_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        string email = SetupEmailBox.Text.Trim();
        string password = SetupPasswordBox.Password;

        if (!SecurityService.IsValidEmail(email))
        {
            ShowError(SetupErrorText, Loc.T("security_err_invalid_email"));
            SetupEmailBox.Focus(FocusState.Programmatic);
            return;
        }

        if (password.Length < 8)
        {
            ShowError(SetupErrorText, Loc.T("security_err_password_length"));
            SetupPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!string.Equals(password, SetupConfirmBox.Password, StringComparison.Ordinal))
        {
            ShowError(SetupErrorText, Loc.T("security_err_password_mismatch"));
            SetupConfirmBox.Focus(FocusState.Programmatic);
            return;
        }

        SmtpInput? smtp = null;

        if (SetupSmtpCheckBox.IsChecked == true)
        {
            if (!TryCreateSmtpInput(
                    SetupSmtpHostBox,
                    SetupSmtpPortBox,
                    SetupSmtpUserBox,
                    SetupSmtpPasswordBox,
                    SetupSmtpSslCheckBox,
                    email,
                    SetupErrorText,
                    out SmtpInput input))
            {
                return;
            }

            smtp = input;
        }

        try
        {
            _security.CreateAccount(email, password, smtp);
            Complete();
        }
        catch (Exception ex)
        {
            ShowError(SetupErrorText, ex.Message);
        }
    }

    // ---------------------------------------------------------------- unlock

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        HideError(UnlockErrorText);

        if (_security.Unlock(UnlockPasswordBox.Password))
        {
            Complete();
            return;
        }

        ShowError(UnlockErrorText, Loc.T("security_err_wrong_password"));
        UnlockPasswordBox.Focus(FocusState.Programmatic);
    }

    private void UnlockPassword_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        Unlock_Click(sender, e);
        e.Handled = true;
    }

    private void Forgot_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Reset);

    private void BackToUnlock_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Unlock);

    // ---------------------------------------------------------------- reset

    private async void SendCode_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();
        ResetStatusText.Text = string.Empty;
        ResetStatusText.Visibility = Visibility.Collapsed;

        if (!_security.IsConfigured)
        {
            ShowError(ResetErrorText, Loc.T("security_err_not_configured"));
            return;
        }

        if (!_security.TryGetSmtpOptions(out SmtpOptions options))
        {
            ResetSmtpPanel.Visibility = Visibility.Visible;

            if (string.IsNullOrWhiteSpace(ResetSmtpHostBox.Text))
                PrefillResetSmtp();

            if (!TryCreateSmtpInput(
                    ResetSmtpHostBox,
                    ResetSmtpPortBox,
                    ResetSmtpUserBox,
                    ResetSmtpPasswordBox,
                    ResetSmtpSslCheckBox,
                    _security.Email,
                    ResetErrorText,
                    out SmtpInput input))
            {
                return;
            }

            _security.UpdateSmtp(input);

            if (!_security.TryGetSmtpOptions(out options))
            {
                ShowError(ResetErrorText, Loc.T("security_err_email_incomplete"));
                return;
            }
        }

        string code = _security.GenerateResetCode();

        SendCodeButton.IsEnabled = false;
        SetStatus(ResetStatusText, Loc.T("security_status_sending"));

        try
        {
            await _email.SendPasswordResetAsync(options, _security.Email, code);
            StartCooldown();
            ResetCodeBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            SendCodeButton.IsEnabled = true;
            ResetStatusText.Text = string.Empty;
            ResetStatusText.Visibility = Visibility.Collapsed;
            ShowError(ResetErrorText, ex.Message);
            ResetSmtpPanel.Visibility = Visibility.Visible;
        }
    }

    private void StartCooldown()
    {
        _cooldown = 30;
        SendCodeButton.IsEnabled = false;
        SetStatus(
            ResetStatusText,
            Loc.Format("security_status_sent", _security.MaskedEmail(), _cooldown));
        _cooldownTimer.Start();
    }

    private void OnCooldownTick(object? sender, object e)
    {
        _cooldown--;

        if (_cooldown <= 0)
        {
            _cooldownTimer.Stop();
            SendCodeButton.IsEnabled = true;
            SetStatus(ResetStatusText, Loc.T("security_status_resend"));
            return;
        }

        SetStatus(
            ResetStatusText,
            Loc.Format("security_status_sent", _security.MaskedEmail(), _cooldown));
    }

    private void UseLocalRecovery_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        if (!_security.HasLocalRecovery)
        {
            ShowError(ResetErrorText, Loc.T("security_err_no_dpapi"));
            return;
        }

        _useLocalReset = true;
        ResetEmailArea.Visibility = Visibility.Collapsed;
        LocalResetLink.Visibility = Visibility.Collapsed;
        ResetSubtitleText.Text = Loc.T("security_reset_dpapi_subtitle");
        LocalNoteText.Text = Loc.T("security_reset_local_note");
        LocalNoteText.Visibility = Visibility.Visible;
        ResetPasswordBox.Focus(FocusState.Programmatic);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        string password = ResetPasswordBox.Password;

        if (password.Length < 8)
        {
            ShowError(ResetErrorText, Loc.T("security_err_new_password_length"));
            ResetPasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!string.Equals(password, ResetConfirmBox.Password, StringComparison.Ordinal))
        {
            ShowError(ResetErrorText, Loc.T("security_err_password_mismatch"));
            ResetConfirmBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!_useLocalReset && !_security.VerifyResetCode(ResetCodeBox.Text))
        {
            ShowError(ResetErrorText, Loc.T("security_err_reset_code"));
            ResetCodeBox.Focus(FocusState.Programmatic);
            return;
        }

        try
        {
            if (_useLocalReset)
                _security.ResetPasswordWithLocalRecovery(password);
            else
                _security.ResetPasswordWithDpapi(password);

            Complete();
        }
        catch (Exception ex)
        {
            ShowError(ResetErrorText, ex.Message);
        }
    }

    private void PrefillResetSmtp()
    {
        SmtpOptions defaults = SmtpDefaults.ForEmail(_security.Email);

        if (string.IsNullOrWhiteSpace(ResetSmtpHostBox.Text))
            ResetSmtpHostBox.Text = defaults.Host;

        if (string.IsNullOrWhiteSpace(ResetSmtpPortBox.Text))
            ResetSmtpPortBox.Text = defaults.Port.ToString();

        if (string.IsNullOrWhiteSpace(ResetSmtpUserBox.Text))
            ResetSmtpUserBox.Text = _security.Email;
    }

    // ---------------------------------------------------------------- helpers

    private static bool TryCreateSmtpInput(
        TextBox hostBox,
        TextBox portBox,
        TextBox userBox,
        PasswordBox passwordBox,
        CheckBox sslBox,
        string from,
        TextBlock errorText,
        out SmtpInput input)
    {
        input = default!;

        string host = hostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowError(errorText, Loc.T("security_err_smtp_host"));
            hostBox.Focus(FocusState.Programmatic);
            return false;
        }

        int port = 587;
        if (!string.IsNullOrWhiteSpace(portBox.Text) &&
            (!int.TryParse(portBox.Text.Trim(), out port) || port < 1 || port > 65535))
        {
            ShowError(errorText, Loc.T("security_err_smtp_port"));
            portBox.Focus(FocusState.Programmatic);
            return false;
        }

        string user = userBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(user))
            user = from.Trim();

        input = new SmtpInput(
            host,
            port,
            sslBox.IsChecked == true,
            user,
            from.Trim(),
            passwordBox.Password);

        return true;
    }

    private static void ShowError(TextBlock target, string message)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
    }

    private static void HideError(TextBlock target)
    {
        target.Text = string.Empty;
        target.Visibility = Visibility.Collapsed;
    }

    private static void SetStatus(TextBlock target, string message)
    {
        target.Text = message;
        target.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
