using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Video.Models;
using Video.Services;

namespace Video.Views
{
    /// <summary>新增或修改一部影片。可以全程用鍵盤：片名 Enter → 演員 Enter … → 性質 Enter … → 空白時 Enter 儲存。</summary>
    public partial class EditVideoWindow : Window
    {
        /// <summary>實際存檔由主視窗處理；回傳 false 代表存檔失敗，視窗要留著。</summary>
        public delegate bool SaveHandler(VideoEntry? existing, string title, IReadOnlyList<string> actors, IReadOnlyList<string> genres);

        private const int MaxSuggestions = 8;

        private readonly AppData _data;
        private readonly VideoEntry? _existing;
        private readonly SaveHandler _save;
        private readonly ObservableCollection<string> _actors = new();
        private readonly ObservableCollection<GenreOption> _genres = new();
        private List<string> _knownActors = new();
        private string _snapshot = "";
        private bool _allowClose;
        private bool _splittingActors;

        public EditVideoWindow(AppData data, VideoEntry? existing, SaveHandler save)
        {
            _data = data;
            _existing = existing;
            _save = save;
            InitializeComponent();

            ActorChips.ItemsSource = _actors;
            GenreChips.ItemsSource = _genres;

            Title = existing == null ? "新增影片" : "修改影片";
            SaveNextButton.Visibility = existing == null ? Visibility.Visible : Visibility.Collapsed;

            LoadFields(existing);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        }

        private void LoadFields(VideoEntry? video)
        {
            TitleBox.Text = video?.Title ?? "";
            TitleError.Visibility = Visibility.Collapsed;
            ActorInput.Text = "";
            GenreInput.Text = "";

            _actors.Clear();
            foreach (var actor in video?.Actors ?? new()) _actors.Add(actor);

            _genres.Clear();
            foreach (var genre in _data.Genres)
                _genres.Add(new GenreOption(genre, video?.Genres.Contains(genre, NameComparer.Instance) == true));

            _knownActors = CatalogRules.AllActors(_data);
            HideSuggestions();
            _snapshot = CurrentState();
        }

        // ===== 片名 =====

        private void TitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                ActorInput.Focus();
                e.Handled = true;
            }
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TitleBox.Text.Trim().Length > 0) TitleError.Visibility = Visibility.Collapsed;
        }

        // ===== 演員 =====

        private void AddActor(string name)
        {
            var actor = CatalogRules.Canonical(name, _knownActors);
            if (actor.Length > 0 && !_actors.Contains(actor, NameComparer.Instance)) _actors.Add(actor);
        }

        private void RemoveActor_Click(object sender, RoutedEventArgs e)
        {
            _actors.Remove((string)((Button)sender).Tag);
            ActorInput.Focus();
        }

        /// <summary>把輸入框裡的字加成演員（一次打好幾個也可以）。</summary>
        private void CommitActorInput()
        {
            foreach (var name in NameRules.SplitNames(ActorInput.Text)) AddActor(name);
            ActorInput.Clear();
            HideSuggestions();
        }

        private void ActorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_splittingActors) return;

            // 打了頓號、逗號或貼上一串名字：分隔符號前面的先加進去，後面的留著繼續打
            var text = ActorInput.Text;
            if (NameRules.HasSeparator(text))
            {
                var parts = NameRules.SplitNames(text);
                var endsWithSeparator = NameRules.HasSeparator(text[^1].ToString());
                var rest = endsWithSeparator || parts.Count == 0 ? "" : parts[^1];
                foreach (var name in endsWithSeparator ? parts : parts.Take(parts.Count - 1)) AddActor(name);

                _splittingActors = true;
                ActorInput.Text = rest;
                ActorInput.CaretIndex = rest.Length;
                _splittingActors = false;
                text = rest;
            }

            var suggestions = CatalogRules.Suggest(text, _knownActors, _actors, MaxSuggestions);
            ActorSuggestions.ItemsSource = suggestions;
            ActorSuggestions.SelectedIndex = -1;
            ActorSuggestions.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HideSuggestions()
        {
            ActorSuggestions.ItemsSource = null;
            ActorSuggestions.Visibility = Visibility.Collapsed;
        }

        private void ActorInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var hasSuggestions = ActorSuggestions.Visibility == Visibility.Visible;
            switch (e.Key)
            {
                case Key.Down when hasSuggestions:
                    MoveSuggestion(+1);
                    e.Handled = true;
                    break;
                case Key.Up when hasSuggestions:
                    MoveSuggestion(-1);
                    e.Handled = true;
                    break;
                case Key.Back when ActorInput.Text.Length == 0 && _actors.Count > 0:
                    _actors.RemoveAt(_actors.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                    if (ActorSuggestions.SelectedItem is string picked)
                    {
                        AddActor(picked);
                        ActorInput.Clear();
                        HideSuggestions();
                    }
                    else if (ActorInput.Text.Trim().Length > 0)
                    {
                        CommitActorInput();
                    }
                    else
                    {
                        GenreInput.Focus(); // 空白時按 Enter 就換下一欄
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSuggestion(int delta)
        {
            var count = ActorSuggestions.Items.Count;
            if (count == 0) return;
            ActorSuggestions.SelectedIndex = (ActorSuggestions.SelectedIndex + delta + count + (ActorSuggestions.SelectedIndex < 0 && delta < 0 ? 1 : 0)) % count;
            ActorSuggestions.ScrollIntoView(ActorSuggestions.SelectedItem);
        }

        private void ActorSuggestions_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 攔下來不讓清單搶走輸入框的焦點，點完可以繼續打字
            if (ItemsControl.ContainerFromElement(ActorSuggestions, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: string name })
            {
                AddActor(name);
                ActorInput.Clear();
                HideSuggestions();
                e.Handled = true;
            }
        }

        // ===== 性質 =====

        /// <summary>把輸入框裡的字選成性質：現有的就選取，沒有的就新增一個選項。</summary>
        private void CommitGenreInput()
        {
            foreach (var name in NameRules.SplitNames(GenreInput.Text))
            {
                var option = _genres.FirstOrDefault(g => NameRules.Same(g.Name, name));
                if (option != null)
                {
                    option.IsSelected = true;
                }
                else
                {
                    // 依顯示順序插入，和存檔後看到的順序一致
                    var index = 0;
                    while (index < _genres.Count && CatalogRules.DisplayOrder.Compare(_genres[index].Name, name) < 0) index++;
                    _genres.Insert(index, new GenreOption(name, true));
                }
            }
            GenreInput.Clear();
        }

        private void GenreInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;

            if (GenreInput.Text.Trim().Length > 0) CommitGenreInput();
            else if (TrySave()) CloseSaved();
            e.Handled = true;
        }

        // ===== 儲存與關閉 =====

        private bool TrySave()
        {
            // 打了還沒按 Enter 的字也算，免得白打
            CommitActorInput();
            CommitGenreInput();

            var title = NameRules.Clean(TitleBox.Text);
            if (title.Length == 0)
            {
                TitleError.Visibility = Visibility.Visible;
                TitleBox.Focus();
                return false;
            }

            var duplicate = CatalogRules.FindDuplicate(_data, title, _existing);
            if (duplicate != null)
            {
                var who = duplicate.Actors.Count > 0 ? $"（演員：{duplicate.ActorsText}）" : "";
                var answer = MessageBox.Show(this, $"已經有同片名的影片「{duplicate.Title}」{who}，還是要儲存嗎？",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    TitleBox.Focus();
                    TitleBox.SelectAll();
                    return false;
                }
            }

            return _save(_existing, title, _actors.ToList(), _genres.Where(g => g.IsSelected).Select(g => g.Name).ToList());
        }

        private void CloseSaved()
        {
            _allowClose = true;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (TrySave()) CloseSaved();
        }

        private void SaveNext_Click(object sender, RoutedEventArgs e) => SaveAndNext();

        private void SaveAndNext()
        {
            if (!TrySave()) return;
            var title = NameRules.Clean(TitleBox.Text);
            LoadFields(null);
            MessageText.Text = $"已新增「{title}」";
            TitleBox.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                if (TrySave()) CloseSaved();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                if (_existing == null) SaveAndNext();
                else if (TrySave()) CloseSaved();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // 視窗會比輸入框先收到按鍵，所以在這裡判斷：有建議清單時 Esc 只收起清單
                if (ActorSuggestions.Visibility == Visibility.Visible) HideSuggestions();
                else Close();
                e.Handled = true;
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (_allowClose || CurrentState() == _snapshot) return;

            var answer = MessageBox.Show(this, "剛剛填的內容還沒有儲存，確定要關閉嗎？", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) e.Cancel = true;
        }

        /// <summary>目前畫面內容，用來判斷有沒有改過。</summary>
        private string CurrentState() => string.Join('\n',
            TitleBox.Text.Trim(),
            ActorInput.Text.Trim(),
            GenreInput.Text.Trim(),
            string.Join('、', _actors),
            string.Join('、', _genres.Where(g => g.IsSelected).Select(g => g.Name)));

        /// <summary>編輯視窗裡的一個性質選項。</summary>
        private sealed class GenreOption : INotifyPropertyChanged
        {
            private bool _isSelected;

            public GenreOption(string name, bool isSelected)
            {
                Name = name;
                _isSelected = isSelected;
            }

            public string Name { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
