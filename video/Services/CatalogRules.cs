using System.Globalization;
using Video.Models;

namespace Video.Services
{
    /// <summary>新增、修改影片，以及整理性質和演員名字的規則。</summary>
    public static class CatalogRules
    {
        /// <summary>顯示順序：繁體中文依筆劃。</summary>
        public static readonly StringComparer DisplayOrder =
            StringComparer.Create(CultureInfo.GetCultureInfo("zh-TW"), ignoreCase: true);

        /// <summary>
        /// 新增（existing 是 null）或修改一部影片。打的名字和現有的一樣時（不計全半形、大小寫、空白），
        /// 一律用現有的寫法，同一個人才不會變成兩筆。新的性質會自動加進性質清單。
        /// </summary>
        public static VideoEntry Upsert(AppData data, VideoEntry? existing, string title,
            IEnumerable<string> actors, IEnumerable<string> genres, DateTime now)
        {
            var knownActors = AllActors(data);
            var cleanActors = CleanNames(actors, knownActors);
            var cleanGenres = CleanNames(genres, data.Genres);

            var missing = cleanGenres.Where(g => !data.Genres.Contains(g, NameComparer.Instance)).ToList();
            if (missing.Count > 0)
            {
                data.Genres.AddRange(missing);
                SortGenres(data);
            }

            var entry = existing ?? new VideoEntry { CreatedAt = now };
            entry.Title = NameRules.Clean(title);
            entry.Actors = cleanActors;
            entry.Genres = cleanGenres;
            entry.UpdatedAt = now;
            if (existing == null) data.Videos.Add(entry);
            return entry;
        }

        /// <summary>找片名相同的另一部影片；沒有就回傳 null。</summary>
        public static VideoEntry? FindDuplicate(AppData data, string title, VideoEntry? excluding) =>
            data.Videos.FirstOrDefault(v => !ReferenceEquals(v, excluding) && NameRules.Same(v.Title, title));

        /// <summary>目前所有影片用到的演員（不重複）。</summary>
        public static List<string> AllActors(AppData data) =>
            data.Videos.SelectMany(v => v.Actors)
                .Distinct(NameComparer.Instance)
                .OrderBy(a => a, DisplayOrder)
                .ToList();

        /// <summary>現有寫法；清單裡沒有就回傳整理過的輸入。</summary>
        public static string Canonical(string name, IEnumerable<string> known)
        {
            var clean = NameRules.Clean(name);
            return known.FirstOrDefault(k => NameRules.Same(k, clean)) ?? clean;
        }

        /// <summary>輸入演員時的建議：包含打的字就算，開頭相符的排前面，已經選的不再出現。</summary>
        public static List<string> Suggest(string text, IEnumerable<string> known, IEnumerable<string> excluded, int limit)
        {
            var key = NameRules.Key(text);
            if (key.Length == 0) return new();
            var skip = new HashSet<string>(excluded, NameComparer.Instance);
            return known
                .Where(k => !skip.Contains(k) && NameRules.Key(k).Contains(key, StringComparison.Ordinal))
                .OrderBy(k => NameRules.Key(k).StartsWith(key, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(k => k, DisplayOrder)
                .Take(limit)
                .ToList();
        }

        /// <summary>新增性質；已經有了就回傳 false。</summary>
        public static bool AddGenre(AppData data, string name)
        {
            var clean = NameRules.Clean(name);
            if (clean.Length == 0 || data.Genres.Contains(clean, NameComparer.Instance)) return false;
            data.Genres.Add(clean);
            SortGenres(data);
            return true;
        }

        /// <summary>性質改名；新名字已經有了就是合併。回傳受影響的影片數。</summary>
        public static int RenameGenre(AppData data, string oldName, string newName)
        {
            var clean = NameRules.Clean(newName);
            data.Genres.RemoveAll(g => NameRules.Same(g, oldName));
            if (!data.Genres.Contains(clean, NameComparer.Instance)) data.Genres.Add(clean);
            SortGenres(data);

            var target = Canonical(clean, data.Genres);
            return data.Videos.Count(v => ReplaceName(v.Genres, oldName, target));
        }

        /// <summary>刪除性質，影片本身留著。回傳受影響的影片數。</summary>
        public static int RemoveGenre(AppData data, string name)
        {
            data.Genres.RemoveAll(g => NameRules.Same(g, name));
            return data.Videos.Count(v => v.Genres.RemoveAll(g => NameRules.Same(g, name)) > 0);
        }

        /// <summary>演員改名；新名字已經有了就是合併。回傳受影響的影片數。</summary>
        public static int RenameActor(AppData data, string oldName, string newName)
        {
            var clean = NameRules.Clean(newName);
            // 不是只改大小寫時，新名字如果已經有人用，就用現有的寫法
            var others = data.Videos.SelectMany(v => v.Actors).Where(a => !NameRules.Same(a, oldName));
            var target = Canonical(clean, others);
            return data.Videos.Count(v => ReplaceName(v.Actors, oldName, target));
        }

        /// <summary>把演員從所有影片移除，影片本身留著。回傳受影響的影片數。</summary>
        public static int RemoveActor(AppData data, string name) =>
            data.Videos.Count(v => v.Actors.RemoveAll(a => NameRules.Same(a, name)) > 0);

        public static void SortGenres(AppData data) => data.Genres.Sort(DisplayOrder);

        private static List<string> CleanNames(IEnumerable<string> names, IEnumerable<string> known) =>
            names.Select(n => Canonical(n, known))
                .Where(n => n.Length > 0)
                .Distinct(NameComparer.Instance)
                .ToList();

        /// <summary>把清單裡的舊名字換成新名字，換完如果重複就只留一個。回傳有沒有換到。</summary>
        private static bool ReplaceName(List<string> names, string oldName, string newName)
        {
            var index = names.FindIndex(n => NameRules.Same(n, oldName));
            if (index < 0) return false;
            names[index] = newName;
            for (int i = names.Count - 1; i >= 0; i--)
                if (i != index && NameRules.Same(names[i], newName)) names.RemoveAt(i);
            return true;
        }
    }
}
