using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Video.Models;
using Video.Services;

namespace Video.Views
{
    /// <summary>新增、改名、刪除性質，整理演員名字（打錯字、同一個人兩種寫法）。</summary>
    public partial class ManageWindow : Window
    {
        private readonly AppData _data;
        private readonly Action<string> _changed;
        private bool _ready;

        public ManageWindow(AppData data, Action<string> changed)
        {
            _data = data;
            _changed = changed;
            InitializeComponent();
            _ready = true;
            Reload(null);
        }

        private bool IsGenre => GenreTab.IsChecked == true;
        private string KindName => IsGenre ? "性質" : "演員";

        private void Window_Loaded(object sender, RoutedEventArgs e) => FilterBox.Focus();

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            FilterBox.Text = "";
            Reload(null);
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_ready) Reload((NameList.SelectedItem as Row)?.Name);
        }

        /// <summary>重新列出清單，並選回 keep 這個名字（如果還在）。</summary>
        private void Reload(string? keep)
        {
            var counts = SearchRules.Count(_data.Videos, v => IsGenre ? v.Genres : v.Actors);
            var names = IsGenre ? _data.Genres : CatalogRules.AllActors(_data);
            var key = NameRules.Key(FilterBox.Text);
            var rows = names
                .Where(n => key.Length == 0 || NameRules.Key(n).Contains(key, StringComparison.Ordinal))
                .Select(n => new Row(n, counts.GetValueOrDefault(n)))
                .ToList();

            NameList.ItemsSource = rows;
            NameList.SelectedItem = rows.FirstOrDefault(r => NameRules.Same(r.Name, keep));

            EmptyText.Text = names.Count == 0
                ? (IsGenre ? "還沒有性質，可以在右邊新增。" : "還沒有演員。新增影片時填上演員，就會出現在這裡。")
                : "沒有符合的名稱。";
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 演員是從影片來的，不用單獨新增
            AddButton.Visibility = IsGenre ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtons();
        }

        private void NameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NameList.SelectedItem is Row row) NameBox.Text = row.Name;
            else if (!NameBox.IsKeyboardFocused) NameBox.Text = "";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var row = NameList.SelectedItem as Row;
            RenameButton.IsEnabled = row != null;
            DeleteButton.IsEnabled = row != null;
            DeleteButton.Content = row == null ? "刪除" : $"刪除（{row.Count} 部影片使用中）";
            HintText.Text = row == null
                ? $"在左邊選一個{KindName}，可以改名或刪除。"
                : "改成已經有的名稱就會合併成同一個。";
        }

        private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (NameList.SelectedItem != null) Rename();
            else if (IsGenre) Add();
            e.Handled = true;
        }

        private void Add_Click(object sender, RoutedEventArgs e) => Add();

        private void Rename_Click(object sender, RoutedEventArgs e) => Rename();

        private void Add()
        {
            if (!TryReadName(out var name)) return;
            if (!CatalogRules.AddGenre(_data, name))
            {
                Warn($"已經有「{CatalogRules.Canonical(name, _data.Genres)}」這個性質了。");
                return;
            }
            FilterBox.Text = "";
            _changed($"已新增性質「{name}」");
            Reload(name);
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void Rename()
        {
            if (NameList.SelectedItem is not Row row || !TryReadName(out var name) || name == row.Name) return;

            var existing = (IsGenre ? _data.Genres : CatalogRules.AllActors(_data))
                .FirstOrDefault(n => NameRules.Same(n, name) && !NameRules.Same(n, row.Name));
            if (existing != null)
            {
                var answer = MessageBox.Show(this, $"「{existing}」已經存在，要把「{row.Name}」合併過去嗎？\n合併後不能自動復原。",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }

            var affected = IsGenre
                ? CatalogRules.RenameGenre(_data, row.Name, name)
                : CatalogRules.RenameActor(_data, row.Name, name);
            FilterBox.Text = "";
            _changed($"已把「{row.Name}」改成「{existing ?? name}」，更新了 {affected} 部影片");
            Reload(existing ?? name);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (NameList.SelectedItem is not Row row) return;

            var question = row.Count > 0
                ? $"確定要刪除{KindName}「{row.Name}」？\n會從 {row.Count} 部影片上移除這個{KindName}，影片本身不會被刪除。"
                : $"確定要刪除{KindName}「{row.Name}」？";
            if (MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                != MessageBoxResult.Yes) return;

            var affected = IsGenre
                ? CatalogRules.RemoveGenre(_data, row.Name)
                : CatalogRules.RemoveActor(_data, row.Name);
            _changed($"已刪除{KindName}「{row.Name}」，更新了 {affected} 部影片");
            NameBox.Text = "";
            Reload(null);
        }

        private bool TryReadName(out string name)
        {
            name = NameRules.Clean(NameBox.Text);
            if (name.Length == 0)
            {
                Warn("請先輸入名稱。");
                return false;
            }
            if (NameRules.HasSeparator(name))
            {
                Warn("名稱不能有頓號、逗號或分號，這些符號是用來分隔多個名字的。");
                return false;
            }
            return true;
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private sealed record Row(string Name, int Count)
        {
            public override string ToString() => Name;
        }
    }
}
