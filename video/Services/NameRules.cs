using System.Text;

namespace Video.Services
{
    /// <summary>
    /// 片名、演員、性質的文字規則。比對時全形半形、大小寫、空白都不計較：
    /// 「ＡＢＣ」等於「abc」，「王 小明」等於「王小明」。
    /// </summary>
    public static class NameRules
    {
        private static readonly char[] Separators = { '、', ',', '，', ';', '；', '\r', '\n', '\t' };

        /// <summary>顯示用：去掉前後空白，連續空白變一個。</summary>
        public static string Clean(string? text) =>
            string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>比對用的鍵。</summary>
        public static string Key(string? text)
        {
            var value = text ?? "";
            try
            {
                value = value.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // 文字裡有不合法的 Unicode（例如半個 emoji）就不轉換，照原樣比對
            }

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        public static bool Same(string? a, string? b) => Key(a) == Key(b);

        /// <summary>一次打好幾個名字時拆開（用頓號、逗號、分號或換行分隔）。</summary>
        public static List<string> SplitNames(string? text) =>
            (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(Clean)
                .Where(name => name.Length > 0)
                .ToList();

        public static bool HasSeparator(string? text) => (text ?? "").IndexOfAny(Separators) >= 0;
    }

    /// <summary>用 <see cref="NameRules.Key"/> 比對的比較器，給 HashSet / Dictionary 用。</summary>
    public sealed class NameComparer : IEqualityComparer<string>
    {
        public static readonly NameComparer Instance = new();

        public bool Equals(string? x, string? y) => NameRules.Same(x, y);

        public int GetHashCode(string obj) => NameRules.Key(obj).GetHashCode();
    }
}
