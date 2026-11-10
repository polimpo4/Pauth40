using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Pauth40.Localization;
using Pauth40.Services;

namespace Pauth40.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const string EmptyCode = "------";
    private static string DefaultCopyLabel => Loc.T("main_copy");

    private readonly AccountStore? _store;
    private readonly ClipboardService _clipboard;
    private readonly bool _isDemo;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _copyResetTimer;

    private TotpService? _totp;
    private Account? _selectedAccount;
    private string _currentCode = EmptyCode;
    private int _remainingSeconds;
    private double _fraction;
    private string _copyButtonText = DefaultCopyLabel;
    private bool _isCopyFeedback;
    private bool _initialized;
    private long _lastPeriodIndex = -1;

    private string? _pendingRemovalId;
    private DateTime _pendingRemovalUntil;

    public MainViewModel(AccountStore store, ClipboardService clipboard)
        : this(store, clipboard, false)
    {
    }

    private MainViewModel(AccountStore? store, ClipboardService clipboard, bool isDemo)
    {
        _store = store;
        _clipboard = clipboard;
        _isDemo = isDemo;

        Accounts.CollectionChanged += OnAccountsChanged;

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;

        CaptureCommand = new RelayCommand(ExecuteCapture);
        CopyCommand = new RelayCommand(ExecuteCopy, CanCopy);
        RemoveAccountCommand = new RelayCommand(ExecuteRemove, () => HasAccounts);
        ImportFilesCommand = new RelayCommand(async parameter =>
        {
            if (parameter is IEnumerable<string> paths)
                await ImportFilesAsync(paths);
        });

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += OnTimerTick;

        _copyResetTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.8)
        };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            IsCopyFeedback = false;
            CopyButtonText = DefaultCopyLabel;
        };

        if (isDemo)
        {
            Accounts.Add(new Account
            {
                Issuer = "a76011a7-f3bb-456d-b228-d26d421eca88",
                Label = "",
                Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
                Digits = 6,
                Period = 30
            });
            Accounts.Add(new Account
            {
                Issuer = "GitHub",
                Label = "dev@pauth40.local",
                Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
                Digits = 6,
                Period = 30
            });
            SelectedAccount = Accounts[0];
            StartTimer();
        }
    }

    public static MainViewModel CreatePreview(ClipboardService clipboard) =>
        new(null, clipboard, true);

    public ObservableCollection<Account> Accounts { get; } = new();

    public ICommand CaptureCommand { get; }

    public ICommand CopyCommand { get; }

    public ICommand RemoveAccountCommand { get; }

    public ICommand ImportFilesCommand { get; }

    /// <summary>
    /// Set by the window. Keeps the view model independent from WPF windows while
    /// still using the real screen-capture implementation.
    /// </summary>
    public Func<Task<Bitmap?>>? CaptureProvider { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? ToastRequested;

    public Account? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (ReferenceEquals(_selectedAccount, value))
                return;

            _selectedAccount = value;
            _pendingRemovalId = null;
            BuildTotp();

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAccountName));
            OnPropertyChanged(nameof(SelectedAccountSubtitle));
            OnPropertyChanged(nameof(SelectedAccountInitial));
            OnPropertyChanged(nameof(HasSelectedAccount));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string CurrentCode
    {
        get => _currentCode;
        private set => SetProperty(ref _currentCode, value);
    }

    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set => SetProperty(ref _remainingSeconds, value);
    }

    public double Fraction
    {
        get => _fraction;
        private set
        {
            if (SetProperty(ref _fraction, value))
                OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    public double ProgressPercent => _fraction * 100.0;

    public bool IsCopyFeedback
    {
        get => _isCopyFeedback;
        private set => SetProperty(ref _isCopyFeedback, value);
    }

    public string CopyButtonText
    {
        get => _copyButtonText;
        private set => SetProperty(ref _copyButtonText, value);
    }

    public bool HasAccounts => Accounts.Count > 0;

    public bool IsEmpty => !HasAccounts;

    public bool HasSelectedAccount => SelectedAccount is not null;

    public string SelectedAccountName => SelectedAccount?.Name ?? Loc.T("main_empty_name");

    public string SelectedAccountSubtitle =>
        SelectedAccount?.Subtitle ?? Loc.T("main_empty_subtitle");

    public string SelectedAccountInitial => SelectedAccount?.Initial ?? "+";

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;

        if (_isDemo)
        {
            StartTimer();
            return;
        }

        var loaded = _store?.Load() ?? new List<Account>();
        foreach (var account in loaded)
            Accounts.Add(account);

        SelectedAccount = Accounts.FirstOrDefault();
        StartTimer();

        // The original application stored QR screenshots in a local "novos"
        // folder. Import them once, without ever writing the secret anywhere.
        try
        {
            var existingSecrets = Accounts.Select(a => a.Secret).ToList();
            var imported = await Task.Run(() => ScanLegacyFolder(existingSecrets));
            int added = 0;

            foreach (var account in imported)
            {
                if (AddAccount(account, select: false))
                    added++;
            }

            if (added > 0)
            {
                SelectedAccount ??= Accounts.FirstOrDefault();
                RaiseToast(added == 1
                    ? Loc.T("toast_legacy_one")
                    : Loc.Format("toast_legacy_many", added));
            }
        }
        catch
        {
            // Legacy import is best-effort and must never block startup.
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        int added = 0;
        int failed = 0;

        foreach (var path in paths)
        {
            try
            {
                string? text = await Task.Run(() => QrDecoder.DecodeFile(path));
                if (string.IsNullOrWhiteSpace(text) ||
                    !OtpAuthParser.TryParse(text, out var account, out _))
                {
                    failed++;
                    continue;
                }

                if (AddAccount(account!, select: true))
                    added++;
            }
            catch
            {
                failed++;
            }
        }

        if (added > 0)
        {
            RaiseToast(added == 1 ? Loc.T("toast_qr_one") : Loc.Format("toast_qr_many", added));
        }
        else if (failed > 0)
        {
            RaiseToast(Loc.T("toast_no_valid_qr"));
        }
    }

    /// <summary>
    /// Merges accounts from a backup, skipping duplicates by secret. Returns the
    /// number of accounts that were actually added.
    /// </summary>
    public int MergeAccounts(IEnumerable<Account> accounts)
    {
        int added = 0;

        foreach (var account in accounts)
        {
            if (account is null || string.IsNullOrWhiteSpace(account.Secret))
                continue;

            if (Accounts.Any(a =>
                    string.Equals(a.Secret, account.Secret, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            account.Id = Guid.NewGuid().ToString("N");
            Accounts.Add(account);
            added++;
        }

        if (added > 0)
        {
            SaveQuietly();
            SelectedAccount ??= Accounts.FirstOrDefault();
            CommandManager.InvalidateRequerySuggested();
        }

        return added;
    }

    /// <summary>Reloads the vault after an external change (for example an import).</summary>
    public void ReloadAccounts()
    {
        if (_isDemo || _store is null)
            return;

        string? selectedId = SelectedAccount?.Id;

        var loaded = _store.Load();

        Accounts.Clear();
        foreach (var account in loaded)
            Accounts.Add(account);

        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId)
            ?? Accounts.FirstOrDefault();

        CommandManager.InvalidateRequerySuggested();
    }

    private bool AddAccount(Account account, bool select)
    {
        var existing = Accounts.FirstOrDefault(a =>
            string.Equals(a.Secret, account.Secret, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (select)
                SelectedAccount = existing;

            return false;
        }

        account.Id = Guid.NewGuid().ToString("N");
        Accounts.Add(account);

        if (!_isDemo)
            SaveQuietly();

        if (select)
            SelectedAccount = account;

        return true;
    }

    private void ImportBitmap(Bitmap bitmap, string source)
    {
        try
        {
            string? text = QrDecoder.Decode(bitmap);
            if (string.IsNullOrWhiteSpace(text))
            {
                RaiseToast(Loc.Format("toast_no_qr_in_source", source));
                return;
            }

            if (!OtpAuthParser.TryParse(text, out var account, out var error))
            {
                RaiseToast(error);
                return;
            }

            if (AddAccount(account!, select: true))
                RaiseToast(Loc.Format("toast_account_added", account!.Name));
            else
                RaiseToast(Loc.T("toast_account_exists"));
        }
        catch
        {
            RaiseToast(Loc.T("toast_image_unreadable"));
        }
    }

    private async void ExecuteCapture()
    {
        Func<Task<Bitmap?>>? provider = CaptureProvider;
        if (provider is null)
            return;

        try
        {
            using Bitmap? bitmap = await provider();
            if (bitmap is null)
                return;

            ImportBitmap(bitmap, "selection");
        }
        catch
        {
            RaiseToast(Loc.T("toast_capture_unavailable"));
        }
    }

    private bool CanCopy() => HasAccounts && CurrentCode != EmptyCode;

    private void ExecuteCopy()
    {
        if (!CanCopy())
            return;

        if (_clipboard.TrySetText(CurrentCode))
        {
            IsCopyFeedback = true;
            CopyButtonText = Loc.T("main_copied");
            RaiseToast(Loc.T("toast_code_copied"));

            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        else
        {
            RaiseToast(Loc.T("toast_clipboard_busy"));
        }
    }

    private void ExecuteRemove()
    {
        var account = SelectedAccount;
        if (account is null)
            return;

        var now = DateTime.UtcNow;

        if (_pendingRemovalId == account.Id && now <= _pendingRemovalUntil)
        {
            _pendingRemovalId = null;
            int index = Accounts.IndexOf(account);
            Accounts.Remove(account);

            if (!_isDemo)
                SaveQuietly();

            SelectedAccount = Accounts.Count == 0
                ? null
                : Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];

            RaiseToast(Loc.Format("toast_removed", account.Name));
            return;
        }

        _pendingRemovalId = account.Id;
        _pendingRemovalUntil = now.AddSeconds(6);
        RaiseToast(Loc.Format("toast_remove_confirm", account.Name));
    }

    private void BuildTotp()
    {
        _totp = null;
        _lastPeriodIndex = -1;

        if (SelectedAccount is not null)
        {
            try
            {
                _totp = new TotpService(SelectedAccount);
            }
            catch
            {
                _totp = null;
            }
        }

        if (_totp is null)
        {
            CurrentCode = EmptyCode;
            RemainingSeconds = 0;
            Fraction = 0;
        }
        else
        {
            RefreshNow();
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void StartTimer()
    {
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e) => RefreshNow();

    private void RefreshNow()
    {
        var totp = _totp;
        if (totp is null)
        {
            if (CurrentCode != EmptyCode)
                CurrentCode = EmptyCode;
            if (RemainingSeconds != 0)
                RemainingSeconds = 0;
            if (Fraction != 0)
                Fraction = 0;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        long index = now.ToUnixTimeSeconds() / totp.Period;

        if (index != _lastPeriodIndex)
        {
            _lastPeriodIndex = index;
            try
            {
                CurrentCode = totp.Compute();
            }
            catch
            {
                CurrentCode = EmptyCode;
            }

            CommandManager.InvalidateRequerySuggested();
        }

        Fraction = totp.Fraction;
        RemainingSeconds = totp.RemainingSeconds;
    }

    private static List<Account> ScanLegacyFolder(IEnumerable<string> existingSecrets)
    {
        var results = new List<Account>();
        var seenSecrets = new HashSet<string>(existingSecrets, StringComparer.OrdinalIgnoreCase);

        foreach (var directory in GetLegacyDirectories())
        {
            if (!Directory.Exists(directory))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    string? text = QrDecoder.DecodeFile(file);
                    if (string.IsNullOrWhiteSpace(text) ||
                        !OtpAuthParser.TryParse(text, out var account, out _))
                    {
                        continue;
                    }

                    if (seenSecrets.Add(account!.Secret))
                        results.Add(account);
                }
                catch
                {
                    // Ignore unreadable legacy images.
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> GetLegacyDirectories()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.CurrentDirectory, "novos"),
            Path.Combine(AppContext.BaseDirectory, "novos"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pauth40",
                "novos")
        };

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveQuietly()
    {
        if (_store is null)
            return;

        try
        {
            _store.Save(Accounts);
        }
        catch
        {
            RaiseToast(Loc.T("toast_vault_save_error"));
        }
    }

    private void RaiseToast(string message)
    {
        ToastRequested?.Invoke(this, message);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!IsCopyFeedback)
            CopyButtonText = DefaultCopyLabel;

        OnPropertyChanged(nameof(SelectedAccountName));
        OnPropertyChanged(nameof(SelectedAccountSubtitle));
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(IsEmpty));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _copyResetTimer.Stop();
        Accounts.CollectionChanged -= OnAccountsChanged;
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }
}
