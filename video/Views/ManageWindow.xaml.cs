using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Video.Models;
using Video.Services;

namespace Video.Views
{
    /// <summary>
    /// 新增、改名、刪除性質，調整性質的大項、小項；整理演員名字（打錯字、同一個人兩種寫法）。
    /// </summary>
    public partial class ManageWindow : Window
    {
        private readonly AppData _data;
        private readonly Action<string> _changed;
        private bool _ready;
        private bool _syncing; // 程式自己改下拉選單時不要當成使用者在移動
        private readonly bool _startAdding;

        /// <param name="startAdding">true：一打開就把游標放在「新增性質」，從主畫面的「新增」按鈕進來時用。</param>
        public ManageWindow(AppData data, Action<string> changed, bool startAdding = false)
        {
            _data = data;
            _changed = changed;
            _startAdding = startAdding;
            InitializeComponent();
            _ready = true;
            Reload(null);
        }

        private bool IsGenre => GenreTab.IsChecked == true;
        private string KindName => IsGenre ? "性質" : "演員";

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startAdding) NewNameBox.Focus();
            else FilterBox.Focus();
        }

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
            var rows = IsGenre ? GenreRows() : ActorRows();
            var key = NameRules.Key(FilterBox.Text);
            if (key.Length > 0) rows = rows.Where(r => r.Matches(key, rows)).ToList();
            if (IsGenre) FillNewParentBox(); // 要在選取之前，選取時會用到

            NameList.ItemsSource = rows;
            NameList.SelectedItem = rows.FirstOrDefault(r => NameRules.Same(r.Name, keep));

            var hasAny = IsGenre ? _data.Genres.Count > 0 : _data.Videos.Any(v => v.Actors.Count > 0);
            EmptyText.Text = hasAny
                ? "沒有符合的名稱。"
                : (IsGenre ? "還沒有性質，可以在右邊新增。" : "還沒有演員。新增影片時填上演員，就會出現在這裡。");
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            AddCard.Visibility = IsGenre ? Visibility.Visible : Visibility.Collapsed;
            ParentPanel.Visibility = IsGenre ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtons();
        }

        /// <summary>每個大項後面接著它的小項；大項的數字包含小項的影片。</summary>
        private List<Row> GenreRows()
        {
            var counts = SearchRules.CountGenres(_data.Videos, _data);
            var direct = SearchRules.Count(_data.Videos, v => v.Genres);
            return GenreTree.Ordered(_data).Select(g =>
            {
                var parent = GenreTree.ParentOf(_data, g);
                var level = parent != null ? RowLevel.Sub : GenreTree.HasChildren(_data, g) ? RowLevel.Group : RowLevel.Plain;
                return new Row(g, counts.GetValueOrDefault(g), direct.GetValueOrDefault(g), parent, level);
            }).ToList();
        }

        private List<Row> ActorRows()
        {
            var counts = SearchRules.Count(_data.Videos, v => v.Actors);
            return CatalogRules.AllActors(_data)
                .Select(a => new Row(a, counts.GetValueOrDefault(a), counts.GetValueOrDefault(a), null, RowLevel.Plain))
                .ToList();
        }

        private void NameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = NameList.SelectedItem as Row;
            NameBox.Text = row?.Name ?? "";
            if (IsGenre)
            {
                FillParentBox(row);
                // 新增時預設放在同一個大項底下，連續加小項比較快
                if (row != null) SelectChoice(NewParentBox, GenreTree.RootOf(_data, row.Name));
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var row = NameList.SelectedItem as Row;
            NameBox.IsEnabled = row != null;
            RenameButton.IsEnabled = row != null;
            DeleteButton.IsEnabled = row != null;
            ParentBox.IsEnabled = row != null;
            DeleteButton.Content = row == null ? "刪除" : $"刪除（{row.DirectCount} 部影片使用中）";

            if (row == null)
                HintText.Text = IsGenre
                    ? "在左邊選一個性質，可以改名、換大項或刪除。"
                    : "在左邊選一位演員，可以改名或刪除。";
            else if (row.Level == RowLevel.Group)
                HintText.Text = "這是大項，底下有小項。改名時小項會跟著；刪除時小項會變成大項。";
            else
                HintText.Text = "改成已經有的名稱就會合併成同一個。";
        }

        // ===== 所屬大項 =====

        private sealed record Choice(string? Name, string Display)
        {
            public override string ToString() => Display; // 下拉選單和無障礙工具讀到的文字
        }

        /// <summary>大項的選項；withNone 時第一項是「沒有」（用來把小項改成大項）。</summary>
        private List<Choice> RootChoices(string? excluding, bool withNone) =>
            GenreTree.Roots(_data)
                .Where(r => !NameRules.Same(r, excluding))
                .Select(r => new Choice(r, r))
                .Prepend(new Choice(null, "（沒有，本身就是大項）"))
                .Skip(withNone ? 0 : 1)
                .ToList();

        private void FillParentBox(Row? row)
        {
            _syncing = true;
            ParentBox.ItemsSource = RootChoices(row?.Name, withNone: true);
            SelectChoice(ParentBox, row?.Parent);
            _syncing = false;
        }

        private void FillNewParentBox()
        {
            var current = (NewParentBox.SelectedItem as Choice)?.Name;
            var choices = RootChoices(null, withNone: false);
            NewParentBox.ItemsSource = choices;
            // 還沒有大項時不能加小項，直接說要先做什麼
            NewParentBox.IsEnabled = AddSubButton.IsEnabled = choices.Count > 0;
            NoRootText.Visibility = choices.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            SelectChoice(NewParentBox, current);
        }

        private static void SelectChoice(ComboBox box, string? name)
        {
            if (box.ItemsSource is not List<Choice> choices) return;
            box.SelectedItem = choices.FirstOrDefault(c => NameRules.Same(c.Name, name)) ?? choices.FirstOrDefault();
        }

        private void ParentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || !_ready || NameList.SelectedItem is not Row row || ParentBox.SelectedItem is not Choice choice) return;
            if (NameRules.Same(choice.Name, row.Parent)) return;

            var error = CatalogRules.MoveGenre(_data, row.Name, choice.Name);
            if (error != null)
            {
                Warn(error);
                FillParentBox(row); // 選單改回原本的大項
                return;
            }

            _changed(choice.Name == null ? $"「{row.Name}」改成大項" : $"已把「{row.Name}」放到「{choice.Name}」底下");
            Reload(row.Name);
        }

        // ===== 新增、改名、刪除 =====

        private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Rename();
            e.Handled = true;
        }

        private void NewNameBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Add(null); // Enter：新增成大項
            e.Handled = true;
        }

        private void AddRoot_Click(object sender, RoutedEventArgs e) => Add(null);

        private void AddSub_Click(object sender, RoutedEventArgs e)
        {
            if (NewParentBox.SelectedItem is Choice { Name: { } parent }) Add(parent);
            else Warn("還沒有大項，請先新增一個大項。", NewNameBox);
        }

        private void Rename_Click(object sender, RoutedEventArgs e) => Rename();

        /// <summary>新增性質；parent 是 null 就是大項。</summary>
        private void Add(string? parent)
        {
            if (!TryReadName(NewNameBox, out var name)) return;
            if (!CatalogRules.AddGenre(_data, name, parent))
            {
                Warn($"已經有「{CatalogRules.Canonical(name, _data.Genres)}」這個性質了。", NewNameBox);
                return;
            }
            FilterBox.Text = "";
            _changed(parent == null ? $"已新增大項「{name}」" : $"已在「{parent}」底下新增「{name}」");
            Reload(name);
            NewNameBox.Clear();
            NewNameBox.Focus();
        }

        private void Rename()
        {
            if (NameList.SelectedItem is not Row row || !TryReadName(NameBox, out var name) || name == row.Name) return;

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

            var lines = new List<string>();
            if (row.Level == RowLevel.Group)
            {
                lines.Add($"確定要刪除大項「{row.Name}」？");
                lines.Add($"底下的 {GenreTree.Children(_data, row.Name).Count} 個小項會變成大項，不會被刪除。");
            }
            else
            {
                lines.Add($"確定要刪除{KindName}「{row.Name}」？");
            }
            if (row.DirectCount > 0)
                lines.Add($"會從 {row.DirectCount} 部影片上移除「{row.Name}」，影片本身不會被刪除。");

            if (MessageBox.Show(this, string.Join("\n", lines), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                != MessageBoxResult.Yes) return;

            var affected = IsGenre
                ? CatalogRules.RemoveGenre(_data, row.Name)
                : CatalogRules.RemoveActor(_data, row.Name);
            _changed($"已刪除{KindName}「{row.Name}」，更新了 {affected} 部影片");
            NameBox.Text = "";
            Reload(null);
        }

        private bool TryReadName(TextBox box, out string name)
        {
            name = NameRules.Clean(box.Text);
            if (name.Length == 0)
            {
                Warn("請先輸入名稱。", box);
                return false;
            }
            if (NameRules.HasSeparator(name))
            {
                Warn("名稱不能有頓號、逗號或分號，這些符號是用來分隔多個名字的。", box);
                return false;
            }
            if (IsGenre && name.IndexOfAny(GenreTree.PathSeparators) >= 0)
            {
                Warn("性質名稱不能有「/」，「/」是用來分開大項和小項的。要放在大項底下，請用下面的「所屬大項」選。", box);
                return false;
            }
            return true;
        }

        private void Warn(string message, TextBox? focus = null)
        {
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            (focus ?? NameBox).Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 下拉選單打開時 Esc 只收起選單
            if (e.Key == Key.Escape && !ParentBox.IsDropDownOpen && !NewParentBox.IsDropDownOpen)
            {
                Close();
                e.Handled = true;
            }
        }

        public enum RowLevel
        {
            /// <summary>沒有小項的大項（或演員）。</summary>
            Plain,
            /// <summary>底下有小項的大項。</summary>
            Group,
            /// <summary>小項。</summary>
            Sub,
        }

        /// <summary>清單的一列。Count 是顯示的數字（大項包含小項），DirectCount 是直接標了這個名字的影片數。</summary>
        private sealed record Row(string Name, int Count, int DirectCount, string? Parent, RowLevel Level)
        {
            /// <summary>篩選：自己符合、所屬大項符合、或底下有小項符合，都列出來，才看得出在哪一區。</summary>
            public bool Matches(string key, IEnumerable<Row> all) =>
                Hit(Name, key) || Hit(Parent, key) || all.Any(r => NameRules.Same(r.Parent, Name) && Hit(r.Name, key));

            private static bool Hit(string? text, string key) =>
                text != null && NameRules.Key(text).Contains(key, StringComparison.Ordinal);

            public override string ToString() => Name;
        }
    }
}
