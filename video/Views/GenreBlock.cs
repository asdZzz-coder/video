using System.Globalization;
using System.Windows.Data;
using Video.Models;
using Video.Services;

namespace Video.Views
{
    /// <summary>
    /// 性質按鈕的一區：有小項的大項自成一區（大項在上、小項在下面縮排）；
    /// 沒有小項的大項全部放在最後一區，一個接一個排，和以前一樣省空間。
    /// </summary>
    public sealed record GenreBlock<T>(T? Root, IReadOnlyList<T> Items) where T : class
    {
        public bool HasRoot => Root != null;

        public static List<GenreBlock<T>> Build(IEnumerable<T> options, Func<T, string> name, Func<T, string?> parent)
        {
            var list = options.ToList();
            var roots = list.Where(o => parent(o) == null).OrderBy(name, CatalogRules.DisplayOrder).ToList();
            var blocks = new List<GenreBlock<T>>();
            var loose = new List<T>();

            foreach (var root in roots)
            {
                var children = list.Where(o => NameRules.Same(parent(o), name(root)))
                    .OrderBy(name, CatalogRules.DisplayOrder)
                    .ToList();
                if (children.Count > 0) blocks.Add(new GenreBlock<T>(root, children));
                else loose.Add(root);
            }

            // 找不到大項的小項（不應該發生）也要顯示出來，不然選不到
            loose.AddRange(list.Where(o => parent(o) != null && !roots.Any(r => NameRules.Same(name(r), parent(o)))));
            if (loose.Count > 0) blocks.Add(new GenreBlock<T>(null, loose));
            return blocks;
        }
    }

    /// <summary>表格上的性質標籤：小項前面加上大項，例如「動作／槍戰」。</summary>
    public sealed class GenreLabelConverter : IValueConverter
    {
        private readonly AppData _data;

        public GenreLabelConverter(AppData data) => _data = data;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string genre ? GenreTree.Label(_data, genre) : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
