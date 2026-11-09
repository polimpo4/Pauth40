using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Pauth40.Views;

/// <summary>
/// Gate shown before the main window. It either creates the protected vault,
/// unlocks it with the master password, or resets the password through an
/// e-mail code / the local Windows recovery copy.
/// </summary>
public partial class SecurityWindow : Window
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
    private readonly double _maxHeight;

    private int _cooldown;
    private bool _useDpapiReset;

    public SecurityWindow(SecurityService security, EmailService email)
    {
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _email = email ?? throw new ArgumentNullException(nameof(email));

        InitializeComponent();

        _maxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 48);
        _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cooldownTimer.Tick += OnCooldownTick;
        Closed += (_, _) => _cooldownTimer.Stop();

        ShowMode(_security.IsConfigured ? Mode.Unlock : Mode.Setup);
    }

    private void ShowMode(Mode mode)
    {
        _useDpapiReset = false;

        SetupPanel.Visibility = mode == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
        UnlockPanel.Visibility = mode == Mode.Unlock ? Visibility.Visible : Visibility.Collapsed;
        ResetPanel.Visibility = mode == Mode.Reset ? Visibility.Visible : Visibility.Collapsed;

        ClearErrors();

        switch (mode)
        {
            case Mode.Setup:
                WindowTitleText.Text = "Pauth40 - Primeiro acesso";
                Height = Math.Min(760, _maxHeight);
                Dispatcher.BeginInvoke(() => SetupEmailBox.Focus());
                break;

            case Mode.Unlock:
                WindowTitleText.Text = "Pauth40 - Desbloquear";
                Height = Math.Min(650, _maxHeight);
                UnlockEmailText.Text = _security.MaskedEmail();
                UnlockPasswordBox.Clear();
                Dispatcher.BeginInvoke(() => UnlockPasswordBox.Focus());
                break;

            case Mode.Reset:
                WindowTitleText.Text = "Pauth40 - Recuperar acesso";
                Height = Math.Min(810, _maxHeight);
                ResetSubtitleText.Text =
                    $"Envie um código de 6 dígitos para {_security.MaskedEmail()}. " +
                    "O código expira em 10 minutos.";
                ResetEmailArea.Visibility = Visibility.Visible;
                ResetPasswordArea.Visibility = Visibility.Visible;
                DpapiResetLink.Visibility = Visibility.Visible;
                DpapiNoteText.Visibility = Visibility.Collapsed;
                ResetCodeBox.Clear();
                ResetPasswordBox.Clear();
                ResetConfirmBox.Clear();
                ResetStatusText.Text = string.Empty;

                if (_security.HasSmtp)
                {
                    ResetSmtpPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PrefillResetSmtp();
                    ResetSmtpPanel.Visibility = Visibility.Visible;
                }

                Dispatcher.BeginInvoke(() => ResetPasswordBox.Focus());
                break;
        }
    }

    private void ClearErrors()
    {
        SetupErrorText.Text = string.Empty;
        UnlockErrorText.Text = string.Empty;
        ResetErrorText.Text = string.Empty;
    }

    private void Complete()
    {
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

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
        // Only auto-fill the provider defaults while the user has not typed a
        // server of their own.
        if (SetupSmtpPanel.Visibility == Visibility.Visible &&
            string.IsNullOrWhiteSpace(SetupSmtpHostBox.Text))
        {
            PrefillSetupSmtp();
        }
    }

    private void PrefillSetupSmtp()
    {
        var defaults = SmtpDefaults.ForEmail(SetupEmailBox.Text);

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
            SetupErrorText.Text = "Informe um endereço de e-mail válido.";
            SetupEmailBox.Focus();
            return;
        }

        if (password.Length < 8)
        {
            SetupErrorText.Text = "A senha precisa ter pelo menos 8 caracteres.";
            SetupPasswordBox.Focus();
            return;
        }

        if (!string.Equals(password, SetupConfirmBox.Password, StringComparison.Ordinal))
        {
            SetupErrorText.Text = "As senhas não são iguais.";
            SetupConfirmBox.SelectAll();
            SetupConfirmBox.Focus();
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
                    out var input))
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
            SetupErrorText.Text = ex.Message;
        }
    }

    // ---------------------------------------------------------------- unlock

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        UnlockErrorText.Text = string.Empty;

        if (_security.Unlock(UnlockPasswordBox.Password))
        {
            Complete();
            return;
        }

        UnlockErrorText.Text = "Senha incorreta. Tente novamente.";
        UnlockPasswordBox.SelectAll();
        UnlockPasswordBox.Focus();
    }

    private void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Unlock_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Forgot_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Reset);

    private void BackToUnlock_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Unlock);

    // ---------------------------------------------------------------- reset

    private async void SendCode_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();
        ResetStatusText.Text = string.Empty;

        if (!_security.IsConfigured)
        {
            ResetErrorText.Text = "A senha ainda não foi configurada.";
            return;
        }

        if (!_security.TryGetSmtpOptions(out var options))
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
                    out var input))
            {
                return;
            }

            _security.UpdateSmtp(input);

            if (!_security.TryGetSmtpOptions(out options))
            {
                ResetErrorText.Text = "As configurações de e-mail não estão completas.";
                return;
            }
        }

        string code = _security.GenerateResetCode();

        SendCodeButton.IsEnabled = false;
        ResetStatusText.Text = "Enviando código...";

        try
        {
            await _email.SendPasswordResetAsync(options, _security.Email, code);
            StartCooldown();
            ResetCodeBox.Focus();
        }
        catch (Exception ex)
        {
            SendCodeButton.IsEnabled = true;
            ResetStatusText.Text = string.Empty;
            ResetErrorText.Text = ex.Message;
            ResetSmtpPanel.Visibility = Visibility.Visible;
        }
    }

    private void StartCooldown()
    {
        _cooldown = 30;
        SendCodeButton.IsEnabled = false;
        ResetStatusText.Text =
            $"Código enviado para {_security.MaskedEmail()}. Reenvio em {_cooldown}s.";
        _cooldownTimer.Start();
    }

    private void OnCooldownTick(object? sender, EventArgs e)
    {
        _cooldown--;

        if (_cooldown <= 0)
        {
            _cooldownTimer.Stop();
            SendCodeButton.IsEnabled = true;
            ResetStatusText.Text = "Você pode reenviar o código.";
            return;
        }

        ResetStatusText.Text =
            $"Código enviado para {_security.MaskedEmail()}. Reenvio em {_cooldown}s.";
    }

    private void UseDpapi_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        if (!_security.HasDpapiRecovery)
        {
            ResetErrorText.Text =
                "Este cofre não tem uma cópia de recuperação local do Windows.";
            return;
        }

        _useDpapiReset = true;
        ResetEmailArea.Visibility = Visibility.Collapsed;
        DpapiResetLink.Visibility = Visibility.Collapsed;
        ResetSubtitleText.Text = "Defina uma nova senha para acessar o cofre neste computador.";
        DpapiNoteText.Text =
            "A recuperação local usa a cópia do cofre protegida pelo Windows para este usuário. " +
            "Ela só funciona na mesma conta do Windows que criou o cofre. Se você trocar de " +
            "computador ou de usuário, use o código enviado por e-mail.";
        DpapiNoteText.Visibility = Visibility.Visible;
        ResetPasswordBox.Focus();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ClearErrors();

        string password = ResetPasswordBox.Password;

        if (password.Length < 8)
        {
            ResetErrorText.Text = "A nova senha precisa ter pelo menos 8 caracteres.";
            ResetPasswordBox.Focus();
            return;
        }

        if (!string.Equals(password, ResetConfirmBox.Password, StringComparison.Ordinal))
        {
            ResetErrorText.Text = "As senhas não são iguais.";
            ResetConfirmBox.SelectAll();
            ResetConfirmBox.Focus();
            return;
        }

        if (!_useDpapiReset && !_security.VerifyResetCode(ResetCodeBox.Text))
        {
            ResetErrorText.Text = "Código inválido ou expirado. Envie um novo código.";
            ResetCodeBox.SelectAll();
            ResetCodeBox.Focus();
            return;
        }

        try
        {
            _security.ResetPasswordWithDpapi(password);
            Complete();
        }
        catch (Exception ex)
        {
            ResetErrorText.Text = ex.Message;
        }
    }

    private void PrefillResetSmtp()
    {
        var defaults = SmtpDefaults.ForEmail(_security.Email);

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
            errorText.Text = "Informe o servidor SMTP do seu provedor de e-mail.";
            hostBox.Focus();
            return false;
        }

        int port = 587;
        if (!string.IsNullOrWhiteSpace(portBox.Text) &&
            (!int.TryParse(portBox.Text.Trim(), out port) || port < 1 || port > 65535))
        {
            errorText.Text = "Informe uma porta SMTP válida (normalmente 587).";
            portBox.Focus();
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

    // ---------------------------------------------------------------- chrome

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
