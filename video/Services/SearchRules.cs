using Video.Models;

namespace Video.Services
{
    /// <summary>
    /// 目前的搜尋條件。三種條件要全部符合：
    /// 搜尋框的每個關鍵字（出現在片名、演員或性質都算）、選取的每位演員、選取的每種性質。
    /// </summary>
    public sealed class VideoFilter
    {
        private static readonly char[] KeywordSeparators = { ' ', '\t', '\r', '\n', '、', ',', '，', ';', '；' };

        public IReadOnlyList<string> Keywords { get; private set; } = Array.Empty<string>();
        public HashSet<string> Actors { get; } = new(NameComparer.Instance);
        public HashSet<string> Genres { get; } = new(NameComparer.Instance);

        public bool IsActive => Keywords.Count > 0 || Actors.Count > 0 || Genres.Count > 0;

        public void SetText(string? text)
        {
            // 全形空白也要能分隔關鍵字
            Keywords = (text ?? "").Replace('　', ' ')
                .Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(NameRules.Key)
                .Where(k => k.Length > 0)
                .Distinct()
                .ToList();
        }

        public bool Matches(VideoEntry video)
        {
            if (Actors.Count > 0 && !Actors.All(a => video.Actors.Contains(a, NameComparer.Instance))) return false;
            if (Genres.Count > 0 && !Genres.All(g => video.Genres.Contains(g, NameComparer.Instance))) return false;
            if (Keywords.Count == 0) return true;

            var text = SearchRules.SearchText(video);
            return Keywords.All(k => text.Contains(k, StringComparison.Ordinal));
        }
    }

    public static class SearchRules
    {
        /// <summary>一部影片可以被搜尋到的文字。用換行隔開，關鍵字不會跨欄位比對到。</summary>
        public static string SearchText(VideoEntry video) =>
            string.Join('\n', new[] { video.Title }.Concat(video.Actors).Concat(video.Genres).Select(NameRules.Key));

        /// <summary>數一下每個名字出現在幾部影片裡，側邊篩選的數字用。</summary>
        public static Dictionary<string, int> Count(IEnumerable<VideoEntry> videos, Func<VideoEntry, IEnumerable<string>> names)
        {
            var counts = new Dictionary<string, int>(NameComparer.Instance);
            foreach (var video in videos)
                foreach (var name in names(video).Distinct(NameComparer.Instance))
                    counts[name] = counts.GetValueOrDefault(name) + 1;
            return counts;
        }
    }
}
