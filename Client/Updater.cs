using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace VoiceClient;

public sealed record UpdateInfo(Version Version, string Tag, string Url, long Size, string Sha256)
{
    public string VersionText => Version.ToString(3);
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
}

/// <summary>Aktualizacje klienta z GitHub Releases (repozytorium ustawia się w VoiceClient.csproj: GitHubRepo).</summary>
public static class Updater
{
    public const string InstallerName = "Voxly-Setup.exe";

    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Voxly-Updater/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>"konto/repozytorium" z metadanych programu.</summary>
    public static string Repo =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value ?? "";

    /// <summary>Bezpośredni link do najnowszego instalatora w GitHub Releases.</summary>
    public static string DownloadUrl =>
        $"https://github.com/{(Repo.Contains('/') ? Repo : "Marcoxxdev/VOXLY")}/releases/latest/download/{InstallerName}";

    static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
    public static Version Current => Norm(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
    public static string CurrentText => Current.ToString(3);

    /// <summary>Zwraca nowszą wersję albo null, gdy masz aktualną. Wymaga sumy SHA-256 instalatora (pole digest lub plik .sha256).</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var repo = Repo;
        if (!repo.Contains('/')) throw new UpdateException(Loc.T("upd_norepo"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", cts.Token);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) throw new UpdateException(Loc.T("upd_404", repo));
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var latest = Norm(parsed);
        if (latest <= Current) return null;

        string? url = null, sha = null, shaUrl = null;
        long size = 0;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.Equals(InstallerName, StringComparison.OrdinalIgnoreCase))
            {
                url = a.GetProperty("browser_download_url").GetString();
                if (a.TryGetProperty("size", out var sz)) size = sz.GetInt64();
                if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String &&
                    (dg.GetString() ?? "").StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = dg.GetString()![7..];
            }
            else if (name.Equals(InstallerName + ".sha256", StringComparison.OrdinalIgnoreCase))
                shaUrl = a.GetProperty("browser_download_url").GetString();
        }
        if (url == null) throw new UpdateException(Loc.T("upd_noasset", InstallerName));
        if (sha == null && shaUrl != null)
        {
            var text = await Http.GetStringAsync(shaUrl, cts.Token);
            sha = text.Split(' ', '\t', '\r', '\n')[0];
        }
        if (string.IsNullOrWhiteSpace(sha) || sha.Length != 64) throw new UpdateException(Loc.T("upd_nohash"));
        return new UpdateInfo(latest, tag, url, size, sha.ToLowerInvariant());
    }

    /// <summary>Pobiera instalator do %TEMP% i sprawdza sumę SHA-256. Zwraca ścieżkę pliku.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Voxly-Update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Voxly-Setup-{info.VersionText}.exe");

        using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report(Math.Min(1.0, (double)done / total));
            }
        }

        string actual;
        await using (var fs = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (actual != info.Sha256)
        {
            try { File.Delete(path); } catch { }
            throw new UpdateException(Loc.T("upd_badhash"));
        }
        progress.Report(1);
        return path;
    }

    /// <summary>Uruchamia instalator po cichu; po instalacji sam uruchomi nową wersję (/restart=1). Wywołaj potem Shutdown().</summary>
    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /restart=1")
        { UseShellExecute = true });
}
