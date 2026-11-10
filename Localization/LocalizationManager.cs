using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Pauth40.Localization;

/// <summary>
/// Loads the user interface strings from the <c>langs</c> folder. Every language
/// is a JSON file that the community can edit or add without recompiling the
/// application. Built-in translations are also embedded in the assembly, so a
/// missing or damaged folder never leaves the interface blank.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string BaseLanguageCode = "pt-PT";
    private const string FallbackLanguageCode = "en-US";
    private const string PreferenceFileName = "language.json";

    private static readonly Lazy<LocalizationManager> LazyInstance = new(() => new LocalizationManager());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, LanguageSet> _sets =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<LanguageOption> _languages = new();

    private LanguageOption _currentLanguage = new(BaseLanguageCode, "Português (Portugal)", "Português (Portugal)");

    private LocalizationManager()
    {
        LoadAll();
        SelectInitialLanguage();
    }

    public static LocalizationManager Instance => LazyInstance.Value;

    public IReadOnlyList<LanguageOption> Languages => _languages;

    public LanguageOption CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (ReferenceEquals(_currentLanguage, value))
                return;

            _currentLanguage = value;
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged("Item[]");
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Current translation. Falls back to the reference language.</summary>
    public string this[string key] => Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    /// <summary>Convenience call so startup code can make the intent explicit.</summary>
    public void Initialize()
    {
        // The static instance loads itself on first access.
    }

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (_sets.TryGetValue(_currentLanguage.Code, out var current) &&
            current.Strings.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_sets.TryGetValue(BaseLanguageCode, out var reference) &&
            reference.Strings.TryGetValue(key, out value))
        {
            return value;
        }

        if (_sets.TryGetValue(FallbackLanguageCode, out var fallback) &&
            fallback.Strings.TryGetValue(key, out value))
        {
            return value;
        }

        // Missing key: show it so a translator can spot it immediately.
        return key;
    }

    public string Format(string key, params object?[] args)
    {
        string template = Get(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // A community translation with a broken placeholder must not crash
            // the application.
            return template;
        }
    }

    public bool SetLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        LanguageOption? option = _languages.FirstOrDefault(
            language => string.Equals(language.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

        if (option is null)
            return false;

        if (string.Equals(option.Code, _currentLanguage.Code, StringComparison.OrdinalIgnoreCase))
            return true;

        CurrentLanguage = option;
        SavePreference(option.Code);
        ApplyCulture(option.Code);
        return true;
    }

    private void LoadAll()
    {
        LoadEmbeddedLanguages();

        LoadFolder(Path.Combine(AppContext.BaseDirectory, "langs"));
        LoadFolder(Path.Combine(AppPaths.DataDirectory, "langs"));

        if (_sets.Count == 0)
        {
            // Last-resort safety net. This should never be visible in a normal
            // installation because the JSON files are embedded and copied.
            _sets[BaseLanguageCode] = new LanguageSet(
                BaseLanguageCode,
                "Português (Portugal)",
                "Português (Portugal)",
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        _languages.Clear();
        _languages.AddRange(_sets.Values
            .Select(set => new LanguageOption(set.Code, set.Name, set.NativeName))
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase));

        if (!_languages.Any(language =>
                string.Equals(language.Code, _currentLanguage.Code, StringComparison.OrdinalIgnoreCase)))
        {
            _currentLanguage = _languages[0];
        }
    }

    private void LoadEmbeddedLanguages()
    {
        Assembly assembly = typeof(LocalizationManager).Assembly;

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                !resource.Contains(".langs.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream is null)
                    continue;

                using var reader = new StreamReader(stream, Encoding.UTF8);
                RegisterLanguage(reader.ReadToEnd(), resource);
            }
            catch
            {
                // Ignore a single unreadable built-in file and keep the others.
            }
        }
    }

    private void LoadFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        foreach (string file in files)
        {
            try
            {
                RegisterLanguage(File.ReadAllText(file, Encoding.UTF8), file);
            }
            catch
            {
                // A broken community translation must not stop the application.
            }
        }
    }

    private void RegisterLanguage(string json, string source)
    {
        LanguageFile? file = JsonSerializer.Deserialize<LanguageFile>(json, JsonOptions);

        if (file?.Strings is null || file.Strings.Count == 0)
            return;

        string fallbackCode = Path.GetFileNameWithoutExtension(source);
        string code = string.IsNullOrWhiteSpace(file.Code) ? fallbackCode : file.Code.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return;

        string name = string.IsNullOrWhiteSpace(file.Name) ? code : file.Name.Trim();
        string nativeName = string.IsNullOrWhiteSpace(file.NativeName) ? name : file.NativeName.Trim();

        _sets[code] = new LanguageSet(
            code,
            name,
            nativeName,
            new Dictionary<string, string>(file.Strings, StringComparer.Ordinal));
    }

    private void SelectInitialLanguage()
    {
        string? preferred = LoadPreference();

        if (!string.IsNullOrWhiteSpace(preferred) &&
            _languages.Any(language =>
                string.Equals(language.Code, preferred, StringComparison.OrdinalIgnoreCase)))
        {
            _currentLanguage = _languages.First(language =>
                string.Equals(language.Code, preferred, StringComparison.OrdinalIgnoreCase));
            ApplyCulture(_currentLanguage.Code);
            return;
        }

        string detected = DetectSystemLanguage();

        LanguageOption? match = _languages.FirstOrDefault(language =>
            string.Equals(language.Code, detected, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            match = _languages.FirstOrDefault(language =>
                        string.Equals(language.Code, BaseLanguageCode, StringComparison.OrdinalIgnoreCase))
                    ?? _languages[0];
        }

        _currentLanguage = match;
        ApplyCulture(_currentLanguage.Code);
    }

    private static string DetectSystemLanguage()
    {
        try
        {
            CultureInfo ui = CultureInfo.CurrentUICulture;
            if (string.Equals(ui.TwoLetterISOLanguageName, "pt", StringComparison.OrdinalIgnoreCase))
                return "pt-PT";

            if (string.Equals(ui.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
                return "en-US";
        }
        catch
        {
            // Fall through to the reference language.
        }

        return BaseLanguageCode;
    }

    private static void ApplyCulture(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch
        {
            // An unknown culture code only affects native dialog localization.
        }
    }

    private string? LoadPreference()
    {
        try
        {
            string path = PreferencePath;
            if (!File.Exists(path))
                return null;

            LanguagePreference? preference =
                JsonSerializer.Deserialize<LanguagePreference>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);

            return preference?.Code;
        }
        catch
        {
            return null;
        }
    }

    private void SavePreference(string code)
    {
        try
        {
            string path = PreferencePath;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                new LanguagePreference { Code = code },
                new JsonSerializerOptions { WriteIndented = true });

            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch
        {
            // The choice still applies for this session even if it cannot be saved.
        }
    }

    private static string PreferencePath =>
        Path.Combine(AppPaths.DataDirectory, PreferenceFileName);

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class LanguageFile
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? NativeName { get; set; }
        public Dictionary<string, string>? Strings { get; set; }
    }

    private sealed class LanguagePreference
    {
        public string? Code { get; set; }
    }

    private sealed record LanguageSet(
        string Code,
        string Name,
        string NativeName,
        Dictionary<string, string> Strings);
}
