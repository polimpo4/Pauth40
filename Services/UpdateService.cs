using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pauth40.Services;

/// <summary>
/// Information about a GitHub release that is newer than the running build.
/// </summary>
public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string TagName,
    string ReleaseUrl,
    string? ReleaseNotes,
    string? AssetName,
    string? AssetUrl,
    long AssetSize,
    string? AssetDigest)
{
    public bool HasAsset => !string.IsNullOrWhiteSpace(AssetUrl);
}

public readonly record struct UpdateProgress(long Received, long Total)
{
    public double Percent =>
        Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
}

/// <summary>
/// A failure while checking, downloading or applying an update. The code is a
/// localization key, so the window can present a translated message.
/// </summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string code, string? detail = null, Exception? inner = null)
        : base(detail ?? code, inner)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// Checks the latest GitHub release of the project and applies a downloaded
/// executable. The check is deliberately silent on network, rate-limit and
/// private-repository errors: an offline user must still be able to open the
/// vault.
/// </summary>
public sealed class UpdateService
{
    private const string RepositoryOwner = "polimpo4";
    private const string RepositoryName = "Pauth40";
    private const string LatestReleaseUrl =
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
    private const string UpdateUrlVariable = "PAUTH40_UPDATE_URL";
    private const string GitHubTokenVariable = "PAUTH40_GITHUB_TOKEN";
    private const string UserAgent = "Pauth40-UpdateCheck";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// Returns the release when it is newer than the running build. Returns
    /// <c>null</c> when the app is current, there is no release, the repository
    /// is private, the network is unavailable or the feed is invalid.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetReleaseEndpoint(out Uri endpoint))
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            AddHeaders(request, endpoint);

            using HttpResponseMessage response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound ||
                !response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);

            GitHubRelease? release =
                JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

            if (release is null ||
                release.Draft ||
                release.PreRelease ||
                string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            if (!SemanticVersion.TryParse(release.TagName, out SemanticVersion? latest) ||
                latest is null)
            {
                return null;
            }

            SemanticVersion? current = AppVersion.Semantic;
            if (current is null || latest.CompareTo(current) <= 0)
                return null;

            GitHubAsset? asset = SelectAsset(release.Assets);

            return new UpdateInfo(
                AppVersion.Current,
                latest.ToString(),
                release.TagName.Trim(),
                string.IsNullOrWhiteSpace(release.HtmlUrl)
                    ? $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases"
                    : release.HtmlUrl.Trim(),
                TrimNotes(release.Body),
                asset?.Name,
                asset?.BrowserDownloadUrl,
                asset?.Size ?? 0,
                asset?.Digest);
        }
        catch (OperationCanceledException)
        {
            // A cancelled check (shutdown) or a timeout is not an error the
            // user needs to see; the vault still opens normally.
            return null;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
            NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the release asset and validates its size, SHA-256 digest (when
    /// GitHub provides one) and PE header before it can replace the running
    /// executable.
    /// </summary>
    public async Task DownloadAsync(
        UpdateInfo info,
        string destination,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!info.HasAsset ||
            !Uri.TryCreate(info.AssetUrl, UriKind.Absolute, out Uri? assetUri))
        {
            throw new UpdateException("update_err_no_asset");
        }

        if (!IsSecure(assetUri))
            throw new UpdateException("update_err_insecure");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));

            using var request = new HttpRequestMessage(HttpMethod.Get, assetUri);
            AddHeaders(request, assetUri);

            using HttpResponseMessage response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException(
                    "update_err_http",
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            long total = response.Content.Headers.ContentLength ?? info.AssetSize;
            if (total <= 0)
                total = info.AssetSize;

            long received = 0;
            byte[] hash;

            await using (var source = await response.Content
                             .ReadAsStreamAsync(timeout.Token)
                             .ConfigureAwait(false))
            await using (var target = new FileStream(
                             destination,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];

                int read;
                while ((read = await source
                           .ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                           .ConfigureAwait(false)) > 0)
                {
                    await target
                        .WriteAsync(buffer.AsMemory(0, read), timeout.Token)
                        .ConfigureAwait(false);

                    sha.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(new UpdateProgress(received, total));
                }

                await target.FlushAsync(timeout.Token).ConfigureAwait(false);
                hash = sha.GetHashAndReset();
            }

            if (received == 0 || (total > 0 && received != total))
                throw new UpdateException("update_err_download");

            ValidateDigest(info.AssetDigest, hash);
            ValidateExecutable(destination);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(destination);
            throw new UpdateException("update_err_timeout");
        }
        catch (HttpRequestException exception)
        {
            TryDelete(destination);
            throw new UpdateException("update_err_download", exception.Message, exception);
        }
        catch (IOException exception)
        {
            TryDelete(destination);
            throw new UpdateException("update_err_download", exception.Message, exception);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }
    }

    /// <summary>
    /// Starts a hidden PowerShell helper that waits for this process to exit,
    /// replaces the executable and relaunches it. Windows cannot overwrite a
    /// running executable, so the copy happens after the application closes.
    /// </summary>
    public void LaunchUpdater(string downloadedFile)
    {
        if (!File.Exists(downloadedFile))
            throw new UpdateException("update_err_apply");

        string? target = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(target))
            target = Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrWhiteSpace(target) ||
            !string.Equals(
                Path.GetFileName(target),
                "Pauth40.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateException("update_err_apply");
        }

        EnsureTargetWritable(target);

        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"Pauth40-update-{Guid.NewGuid():N}.ps1");

        string script = BuildUpdaterScript(
            Process.GetCurrentProcess().Id,
            downloadedFile,
            target);

        try
        {
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden " +
                    $"-File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory =
                    Path.GetDirectoryName(target) ?? Environment.CurrentDirectory
            };

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            TryDelete(scriptPath);
            throw new UpdateException("update_err_apply", exception.Message, exception);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return client;
    }

    private static bool TryGetReleaseEndpoint(out Uri endpoint)
    {
        endpoint = new Uri(LatestReleaseUrl);

        string? overrideUrl = Environment.GetEnvironmentVariable(UpdateUrlVariable);

        if (string.IsNullOrWhiteSpace(overrideUrl))
            return true;

        if (!Uri.TryCreate(overrideUrl.Trim(), UriKind.Absolute, out Uri? custom))
            return false;

        if (!IsSecure(custom))
            return false;

        endpoint = custom;
        return true;
    }

    private static void AddHeaders(HttpRequestMessage request, Uri endpoint)
    {
        // The client already sends the User-Agent by default. GitHub-specific
        // headers are only valid for the API host, not for asset downloads.
        if (!endpoint.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            return;

        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        string? token = Environment.GetEnvironmentVariable(GitHubTokenVariable);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    private static bool IsSecure(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        uri.IsLoopback;

    private static GitHubAsset? SelectAsset(List<GitHubAsset>? assets)
    {
        if (assets is null)
            return null;

        var executables = assets
            .Where(asset =>
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                !string.IsNullOrWhiteSpace(asset.Name) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return executables.FirstOrDefault(asset =>
                   asset.Name!.Contains("Pauth40", StringComparison.OrdinalIgnoreCase))
               ?? executables.FirstOrDefault();
    }

    private static string? TrimNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        string value = notes.Trim();

        return value.Length <= 4000
            ? value
            : value[..4000] + "…";
    }

    private static void ValidateDigest(string? digest, byte[] hash)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return;

        string expected = digest.Trim();

        if (!expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("update_err_digest");

        expected = expected[7..].Trim();

        string actual = Convert.ToHexString(hash);

        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("update_err_digest");
    }

    private static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);

        int first = stream.ReadByte();
        int second = stream.ReadByte();

        if (first != 'M' || second != 'Z')
            throw new UpdateException("update_err_invalid_exe");
    }

    private static void EnsureTargetWritable(string target)
    {
        string? directory = Path.GetDirectoryName(target);

        if (string.IsNullOrEmpty(directory))
            throw new UpdateException("update_err_write");

        string probe = Path.Combine(directory, $".pauth40-write-{Guid.NewGuid():N}.tmp");

        try
        {
            using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception exception)
        {
            throw new UpdateException("update_err_write", exception.Message, exception);
        }
    }

    private static string BuildUpdaterScript(int processId, string source, string target)
    {
        string log = Path.Combine(Path.GetTempPath(), "Pauth40-update-error.log");

        return UpdaterTemplate
            .Replace("__PID__", processId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__SOURCE__", EscapePowerShellLiteral(source), StringComparison.Ordinal)
            .Replace("__TARGET__", EscapePowerShellLiteral(target), StringComparison.Ordinal)
            .Replace("__LOG__", EscapePowerShellLiteral(log), StringComparison.Ordinal);
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A leftover temporary file is harmless.
        }
    }

    private const string UpdaterTemplate = """
$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id __PID__ -Timeout 90 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    $target = '__TARGET__'
    Set-ItemProperty -LiteralPath $target -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath '__SOURCE__' -Destination $target -Force
    Start-Process -FilePath $target
    Remove-Item -LiteralPath '__SOURCE__' -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
catch {
    Add-Content -LiteralPath '__LOG__' -Value ("{0} {1}" -f (Get-Date -Format o), $_) -ErrorAction SilentlyContinue
    try { Start-Process -FilePath '__TARGET__' } catch { }
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
