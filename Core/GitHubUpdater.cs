using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FLLiteStudio.Core;

public sealed class UpdaterSettings
{
    public bool AutoCheckOnStartup { get; set; } = true;
    public bool ShowGuideBanners { get; set; } = true;
}

public sealed class UpdateReleaseInfo
{
    public Version Version { get; init; } = new(1, 0, 0);
    public string TagName { get; init; } = "";
    public string Title { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public string? AssetName { get; init; }
    public string? AssetDownloadUrl { get; init; }
    public long AssetSizeBytes { get; init; }
    public bool IsNewerAvailable { get; init; }
}

public readonly record struct UpdateDownloadProgress(long BytesReceived, long TotalBytes, double Percent);

public static class GitHubUpdater
{
    // Repositorio oficial bloqueado en compilación (no editable por usuarios ni archivos locales)
    public const string OfficialRepoOwner = "iansitooas";
    public const string OfficialRepoName = "flstudio";

    public static string OfficialRepoSlug => $"{OfficialRepoOwner}/{OfficialRepoName}";
    private static string OfficialApiUrl => $"https://api.github.com/repos/{OfficialRepoOwner}/{OfficialRepoName}/releases/latest";
    private static string OfficialDownloadPrefix => $"https://github.com/{OfficialRepoOwner}/{OfficialRepoName}/releases/download/";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FLLiteStudio", CurrentVersionString));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 1, 0);
        }
    }

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static string SettingsPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FLLiteStudio");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "updater.json");
        }
    }

    public static UpdaterSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<UpdaterSettings>(json) ?? new UpdaterSettings();
            }
        }
        catch { }
        return new UpdaterSettings();
    }

    public static void SaveSettings(UpdaterSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    public static Version ParseTagVersion(string tag)
    {
        string clean = tag.Trim().TrimStart('v', 'V');
        int dash = clean.IndexOf('-');
        if (dash >= 0) clean = clean[..dash];
        var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int maj = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : 0;
        int min = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 0;
        int pat = parts.Length > 2 && int.TryParse(parts[2], out int c) ? c : 0;
        return new Version(maj, min, pat);
    }

    /// <summary>
    /// Consulta únicamente los Releases oficiales del repositorio bloqueado en compilación.
    /// </summary>
    public static async Task<UpdateReleaseInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(OfficialApiUrl, ct).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Todavía no hay Releases publicados en github.com/{OfficialRepoSlug}.");

        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tEl) ? (tEl.GetString() ?? "0.0.0") : "0.0.0";
        string title = root.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? tag) : tag;
        string body = root.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";
        string htmlUrl = root.TryGetProperty("html_url", out var hEl) ? (hEl.GetString() ?? "") : "";

        string? assetName = null;
        string? assetUrl = null;
        long assetSize = 0;

        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var an) ? (an.GetString() ?? "") : "";
                string dl = asset.TryGetProperty("browser_download_url", out var au) ? (au.GetString() ?? "") : "";
                long sz = asset.TryGetProperty("size", out var asz) ? asz.GetInt64() : 0;

                // Solo se aceptan descargas que provengan estrictamente del repositorio oficial
                if (!dl.StartsWith(OfficialDownloadPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name; assetUrl = dl; assetSize = sz;
                    break;
                }
                if (assetUrl == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name; assetUrl = dl; assetSize = sz;
                }
            }
        }

        Version remoteVersion = ParseTagVersion(tag);
        bool newer = remoteVersion > CurrentVersion;

        return new UpdateReleaseInfo
        {
            Version = remoteVersion,
            TagName = tag,
            Title = title,
            ReleaseNotes = string.IsNullOrWhiteSpace(body) ? "Mejoras generales y optimizaciones." : body,
            HtmlUrl = htmlUrl,
            AssetName = assetName,
            AssetDownloadUrl = assetUrl,
            AssetSizeBytes = assetSize,
            IsNewerAvailable = newer
        };
    }

    /// <summary>
    /// Descarga el paquete oficial verificado e instala la actualización al reiniciar.
    /// </summary>
    public static async Task<bool> DownloadAndApplyUpdateAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.AssetDownloadUrl) || string.IsNullOrWhiteSpace(release.AssetName))
            throw new InvalidOperationException("Esta versión en GitHub no incluye un archivo .zip o .exe descargable.");

        if (!release.AssetDownloadUrl.StartsWith(OfficialDownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Origen de descarga bloqueado: solo se permiten actualizaciones del repositorio oficial.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "FLLiteStudio_Update");
        if (Directory.Exists(tempRoot))
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
        Directory.CreateDirectory(tempRoot);

        string safeFile = Path.GetFileName(release.AssetName);
        string downloadedPath = Path.Combine(tempRoot, safeFile);

        using (var resp = await Http.GetAsync(release.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? release.AssetSizeBytes;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(downloadedPath);

            var buffer = new byte[65536];
            long received = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                double pct = total > 0 ? Math.Clamp((received * 100.0) / total, 0, 100) : 0;
                progress?.Report(new UpdateDownloadProgress(received, total, pct));
            }
        }

        string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "FLLiteStudio.exe");
        int currentPid = Environment.ProcessId;

        string stageDir = Path.Combine(tempRoot, "stage");
        Directory.CreateDirectory(stageDir);

        if (downloadedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(downloadedPath, stageDir, overwriteFiles: true);
            var subDirs = Directory.GetDirectories(stageDir);
            if (subDirs.Length == 1 && Directory.GetFiles(stageDir).Length == 0)
            {
                stageDir = subDirs[0];
            }
        }
        else if (downloadedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(downloadedPath, Path.Combine(stageDir, Path.GetFileName(currentExe)), overwrite: true);
        }

        string scriptPath = Path.Combine(tempRoot, "apply_update.cmd");
        string script = $"""
            @echo off
            setlocal
            :waitloop
            tasklist /FI "PID eq {currentPid}" 2>nul | find /I "{currentPid}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto waitloop
            )
            xcopy "{stageDir}\*" "{appDir}\" /E /Y /I /Q >nul
            start "" "{currentExe}"
            timeout /t 2 /nobreak >nul
            rmdir /S /Q "{tempRoot}" 2>nul
            """;

        File.WriteAllText(scriptPath, script, Encoding.ASCII);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return true;
    }
}
