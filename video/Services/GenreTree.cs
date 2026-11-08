using Video.Models;

namespace Video.Services
{
    /// <summary>
    /// 性質的大項、小項。影片可以標大項，也可以標小項；
    /// 篩選或搜尋大項時，標了它底下小項的影片也算。
    /// </summary>
    public static class GenreTree
    {
        /// <summary>打「大項/小項」可以直接把新的性質加在大項底下。</summary>
        public static readonly char[] PathSeparators = { '/', '／' };

        /// <summary>所屬的大項；本身是大項就回傳 null。</summary>
        public static string? ParentOf(AppData data, string? genre) =>
            genre != null && data.GenreParents.TryGetValue(genre, out var parent) ? parent : null;

        public static bool IsSub(AppData data, string genre) => ParentOf(data, genre) != null;

        /// <summary>自己是大項就是自己，小項就是所屬的大項。</summary>
        public static string RootOf(AppData data, string genre) => ParentOf(data, genre) ?? genre;

        public static List<string> Roots(AppData data) =>
            data.Genres.Where(g => !IsSub(data, g)).ToList();

        public static List<string> Children(AppData data, string root) =>
            data.Genres.Where(g => NameRules.Same(ParentOf(data, g), root)).ToList();

        public static bool HasChildren(AppData data, string genre) =>
            data.GenreParents.Values.Any(p => NameRules.Same(p, genre));

        /// <summary>顯示順序：每個大項後面接著它的小項。</summary>
        public static List<string> Ordered(AppData data) =>
            Roots(data).SelectMany(root => Children(data, root).Prepend(root)).ToList();

        /// <summary>選了 selected 這個性質時，標 genre 的影片算不算（大項包含底下的小項）。</summary>
        public static bool Covers(AppData data, string selected, string genre) =>
            NameRules.Same(genre, selected) || NameRules.Same(ParentOf(data, genre), selected);

        /// <summary>表格上顯示的文字：小項前面加上大項，例如「動作／槍戰」。</summary>
        public static string Label(AppData data, string genre) =>
            ParentOf(data, genre) is { } parent ? $"{parent}／{genre}" : genre;

        /// <summary>把「大項/小項」拆開；沒有分隔符號就回傳 null。</summary>
        public static (string Parent, string Child)? SplitPath(string text)
        {
            var index = text.IndexOfAny(PathSeparators);
            if (index < 0) return null;
            var parent = NameRules.Clean(text[..index]);
            var child = NameRules.Clean(text[(index + 1)..]);
            if (parent.Length == 0 || child.Length == 0 || child.IndexOfAny(PathSeparators) >= 0) return null;
            return (parent, child);
        }

        /// <summary>
        /// 整理大項對照表：名字改成清單上的寫法，拿掉不存在的性質，
        /// 手動改檔造成的三層或繞圈都攤平成兩層。讀檔後和每次修改後都要呼叫。
        /// </summary>
        public static void Normalize(AppData data)
        {
            var result = new Dictionary<string, string>(NameComparer.Instance);
            foreach (var (child, parent) in data.GenreParents ?? new())
            {
                var c = data.Genres.FirstOrDefault(g => NameRules.Same(g, child));
                var p = data.Genres.FirstOrDefault(g => NameRules.Same(g, parent));
                if (c == null || p == null || NameRules.Same(c, p)) continue;
                result.TryAdd(c, p);
            }

            // 大項本身不能是小項：改掛到上一層；繞圈的話拿掉
            var changed = true;
            for (var round = 0; changed && round < 100; round++)
            {
                changed = false;
                foreach (var child in result.Keys.ToList())
                {
                    if (!result.TryGetValue(child, out var parent) || !result.TryGetValue(parent, out var grand)) continue;
                    if (NameRules.Same(grand, child)) result.Remove(child);
                    else result[child] = grand;
                    changed = true;
                }
            }

            data.GenreParents = result;
        }
    }
}
