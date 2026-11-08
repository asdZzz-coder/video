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
        /// 一律用現有的寫法，同一個人才不會變成兩筆。新的性質會自動加進性質清單；
        /// newParents 有寫的新性質會加成那個大項底下的小項。影片上的性質依大項、小項的順序排。
        /// </summary>
        public static VideoEntry Upsert(AppData data, VideoEntry? existing, string title,
            IEnumerable<string> actors, IEnumerable<string> genres, DateTime now,
            IReadOnlyDictionary<string, string>? newParents = null)
        {
            var knownActors = AllActors(data);
            var cleanActors = CleanNames(actors, knownActors);
            var cleanGenres = CleanNames(genres, data.Genres);

            foreach (var genre in cleanGenres.Where(g => !data.Genres.Contains(g, NameComparer.Instance)))
            {
                var parent = newParents?.FirstOrDefault(p => NameRules.Same(p.Key, genre)).Value;
                AddGenre(data, genre, parent);
            }

            var order = GenreTree.Ordered(data);
            cleanGenres = cleanGenres.OrderBy(g => order.FindIndex(o => NameRules.Same(o, g))).ToList();

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

        /// <summary>
        /// 新增性質；已經有了就回傳 false。parent 有填就加成它底下的小項
        /// （大項還沒有的話一起新增；填的是小項就改放到它的大項底下，因為只有兩層）。
        /// </summary>
        public static bool AddGenre(AppData data, string name, string? parent = null)
        {
            var clean = NameRules.Clean(name);
            if (clean.Length == 0 || data.Genres.Contains(clean, NameComparer.Instance)) return false;

            string? root = null;
            var cleanParent = NameRules.Clean(parent);
            if (cleanParent.Length > 0 && !NameRules.Same(cleanParent, clean))
            {
                if (!data.Genres.Contains(cleanParent, NameComparer.Instance)) data.Genres.Add(cleanParent);
                root = GenreTree.RootOf(data, Canonical(cleanParent, data.Genres));
            }

            data.Genres.Add(clean);
            if (root != null) data.GenreParents[clean] = root;
            SortGenres(data);
            return true;
        }

        /// <summary>
        /// 改變性質所屬的大項；parent 是 null 或空白就變成大項。
        /// 不能移的時候回傳原因（給使用者看），成功回傳 null。
        /// </summary>
        public static string? MoveGenre(AppData data, string name, string? parent)
        {
            var genre = data.Genres.FirstOrDefault(g => NameRules.Same(g, name));
            if (genre == null) return $"找不到性質「{name}」。";

            if (string.IsNullOrWhiteSpace(parent))
            {
                data.GenreParents.Remove(genre);
                return null;
            }

            var target = data.Genres.FirstOrDefault(g => NameRules.Same(g, parent));
            if (target == null) return $"找不到大項「{parent}」。";
            if (NameRules.Same(target, genre)) return "不能放在自己底下。";
            if (GenreTree.IsSub(data, target)) return $"「{target}」是小項，小項底下不能再分。";
            if (GenreTree.HasChildren(data, genre))
                return $"「{genre}」底下還有小項，要先把小項移走或刪除，才能把它放到別的大項底下。";

            data.GenreParents.Remove(genre);
            data.GenreParents[genre] = target;
            return null;
        }

        /// <summary>
        /// 性質改名；新名字已經有了就是合併。回傳受影響的影片數。
        /// 大項改名時底下的小項跟著走；合併時小項移到合併後的大項底下。
        /// </summary>
        public static int RenameGenre(AppData data, string oldName, string newName)
        {
            var clean = NameRules.Clean(newName);
            var old = Canonical(oldName, data.Genres);
            var children = GenreTree.Children(data, old);
            var target = data.Genres.FirstOrDefault(g => NameRules.Same(g, clean) && !NameRules.Same(g, old));

            if (target == null)
            {
                var parent = GenreTree.ParentOf(data, old);
                data.Genres.RemoveAll(g => NameRules.Same(g, old));
                data.Genres.Add(clean);
                data.GenreParents.Remove(old);
                if (parent != null) data.GenreParents[clean] = parent;
                foreach (var child in children) data.GenreParents[child] = clean;
                target = clean;
            }
            else
            {
                // 合併到自己的小項時，那個小項升成大項，其他小項改掛到它底下
                var targetParent = GenreTree.ParentOf(data, target);
                var newRoot = NameRules.Same(targetParent, old) ? target : targetParent ?? target;
                if (NameRules.Same(targetParent, old)) data.GenreParents.Remove(target);
                data.GenreParents.Remove(old);
                foreach (var child in children.Where(c => !NameRules.Same(c, target)))
                    data.GenreParents[child] = newRoot;
                data.Genres.RemoveAll(g => NameRules.Same(g, old));
            }

            SortGenres(data);
            GenreTree.Normalize(data);
            return data.Videos.Count(v => ReplaceName(v.Genres, old, target));
        }

        /// <summary>刪除性質，影片本身留著；大項底下的小項會變成大項。回傳受影響的影片數。</summary>
        public static int RemoveGenre(AppData data, string name)
        {
            foreach (var child in GenreTree.Children(data, name)) data.GenreParents.Remove(child);
            data.GenreParents.Remove(name);
            data.Genres.RemoveAll(g => NameRules.Same(g, name));
            GenreTree.Normalize(data);
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
