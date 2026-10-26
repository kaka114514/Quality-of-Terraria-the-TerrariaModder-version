using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Terraria;

namespace QualityOfTerraria
{
    internal static class VersionUpdateService
    {
        internal const string VideoUrl = "https://www.bilibili.com/video/BV1rjn36XEeN/";
        private const string LatestReleaseApi = "https://api.github.com/repos/kaka114514/Quality-of-Terraria-the-TerrariaModder-version/releases/latest";
        private static bool _started;
        private static Task<string> _request;
        private static string _latestVersion;

        internal static string CurrentVersion
        {
            get
            {
                Version version = typeof(Mod).Assembly.GetName().Version ?? new Version(0, 0);
                return version.Build >= 0 ? version.ToString(3) : version.ToString(2);
            }
        }

        internal static string LatestVersion
        {
            get
            {
                if (_request != null && _request.IsCompleted && _request.Status == TaskStatus.RanToCompletion)
                    _latestVersion = _request.Result;
                return string.IsNullOrEmpty(_latestVersion) ? (_started ? "查询失败" : "尚未查询") : _latestVersion;
            }
        }

        internal static bool HasNewerVersion => IsNewer(_latestVersion);

        internal static void StartCheck()
        {
            if (Main.dedServ || _started) return;
            _started = true;
            _request = QueryLatestAsync();
        }

        internal static void ResetSession() { }

        internal static void Update()
        {
            StartCheck();
        }

        internal static void OpenVideo()
        {
            try { Process.Start(new ProcessStartInfo(VideoUrl) { UseShellExecute = true }); } catch { }
        }

        internal static bool IsNewer(string remoteTag)
        {
            if (string.IsNullOrEmpty(remoteTag)) return false;
            Version remote;
            Version local = typeof(Mod).Assembly.GetName().Version ?? new Version(0, 0);
            string value = remoteTag.Trim().TrimStart('v', 'V');
            return Version.TryParse(value, out remote) && remote > local;
        }

        private static async Task<string> QueryLatestAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("QualityOfTerraria-VersionChecker");
                    string json = await client.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
                    Match match = Regex.Match(json, "\\\"tag_name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
                    return match.Success ? match.Groups[1].Value : null;
                }
            }
            catch { return null; }
        }
    }
}
