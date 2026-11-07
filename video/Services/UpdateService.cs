using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Video.Services
{
    public record UpdateInfo(Version Version, string DownloadUrl, long Size);

    /// <summary>
    /// 線上更新：向 GitHub Releases 查詢最新版，使用者同意後下載安裝檔（msi）交給 Windows Installer，
    /// 新版會取代舊版，資料不受影響。這個 App 不放進「APP 啟動器」，所以只判斷是不是安裝版。
    /// </summary>
    public class UpdateService
    {
        private const string Owner = "asdZzz-coder";
        private const string Repo = "video";
        public const string InstallerAssetName = "Video-Setup.msi";

        /// <summary>下載更新檔的資料夾（啟動時清掉）。</summary>
        public static readonly string DownloadFolder = Path.Combine(Path.GetTempPath(), "Video-Update");

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Video-Updater"); // GitHub API 要求有 User-Agent
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>是否從安裝檔裝的（放在 %LocalAppData%\Programs\Video）。直接從原始碼執行時是 false。</summary>
        public bool IsInstalled { get; } = IsInstalledPath(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        internal static bool IsInstalledPath(string baseDirectory, string localAppData)
        {
            var installDir = Path.Combine(localAppData, "Programs", "Video") + Path.DirectorySeparatorChar;
            var current = Path.TrimEndingDirectorySeparator(baseDirectory) + Path.DirectorySeparatorChar;
            return current.StartsWith(installDir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>程式本身的版本（建置時由 -p:Version 帶入）。</summary>
        public Version Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public string CurrentVersion => IsInstalled ? Version.ToString() : "開發版";

        /// <summary>檢查是否有新版；沒有、開發版、或還沒發佈過都回傳 null。</summary>
        public async Task<UpdateInfo?> CheckAsync()
        {
            if (!IsInstalled) return null;

            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return ParseRelease(doc.RootElement, Version);
        }

        internal static UpdateInfo? ParseRelease(JsonElement root, Version current)
        {
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= current) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == InstallerAssetName)
                    return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64());
            }
            return null; // 該版本還沒有附上安裝檔（打包尚未完成）
        }

        /// <summary>下載安裝檔並開始安裝；呼叫端應在這之後結束程式，安裝完會自動開啟新版。</summary>
        public async Task DownloadAndInstallAsync(UpdateInfo info, Action<int>? progress = null)
        {
            CleanDownloads();
            Directory.CreateDirectory(DownloadFolder);
            var msi = Path.Combine(DownloadFolder, $"Video-Setup-{info.Version}.msi");

            using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(msi);
                var buffer = new byte[81920];
                long done = 0;
                int last = -1, n;
                while ((n = await src.ReadAsync(buffer)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n));
                    done += n;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != last) { last = percent; progress?.Invoke(percent); }
                }
            }

            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msi}\"") { UseShellExecute = true })?.Dispose();
        }

        /// <summary>清掉下載過的更新檔。被占用（例如還在安裝）就略過，下次再清。</summary>
        public static void CleanDownloads()
        {
            try { if (Directory.Exists(DownloadFolder)) Directory.Delete(DownloadFolder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
