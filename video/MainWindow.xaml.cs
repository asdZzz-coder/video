using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Video.Models;
using Video.Services;
using Video.Views;

namespace Video
{
    public partial class MainWindow : Window
    {
        public const string AppTitle = "影片紀錄";

        private readonly UpdateService _updater = new();
        private readonly AppData _data;
        private readonly ListCollectionView _view;
        private readonly VideoFilter _filter = new();

        // 打字時等一下再篩選，資料多的時候也不會卡住輸入
        private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

        public MainWindow(AppData data)
        {
            _data = data;
            _filter.Catalog = data;
            Resources.Add("GenreLabel", new GenreLabelConverter(data));
            InitializeComponent();

            Title = _updater.IsInstalled ? $"{AppTitle} v{_updater.CurrentVersion}" : $"{AppTitle}（開發版）";
            StatusText.Text = $"版本 {_updater.CurrentVersion}";

            _view = new ListCollectionView(_data.Videos);
            _view.SortDescriptions.Add(new SortDescription(nameof(VideoEntry.CreatedAt), ListSortDirection.Descending));
            _view.Filter = item => _filter.Matches((VideoEntry)item);
            VideoGrid.ItemsSource = _view;
            CreatedColumn.SortDirection = ListSortDirection.Descending;

            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                ApplyFilter();
            };

            ApplyFilter();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus();
            _ = Task.Run(UpdateService.CleanDownloads);
            await CheckForUpdateAsync(manual: false);
        }

        // ===== 搜尋與篩選 =====

        private void ApplyFilter()
        {
            _filter.SetText(SearchBox.Text);
            _view.Refresh();

            var results = _data.Videos.Where(_filter.Matches).ToList();

            CountText.Text = _filter.IsActive
                ? $"{results.Count} / {_data.Videos.Count} 部"
                : $"共 {_data.Videos.Count} 部";
            ClearFilterButton.Visibility = _filter.IsActive ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            EmptyText.Text = _data.Videos.Count == 0
                ? "還沒有影片。按右上角的「新增影片」開始記錄。"
                : "找不到符合的影片，可以少打幾個字或清除篩選。";
            EmptyPanel.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            BuildFacets(results);
        }

        /// <summary>左邊的數字跟著目前的結果變，一眼看到還能再怎麼縮小範圍。</summary>
        private void BuildFacets(List<VideoEntry> results)
        {
            var genreCounts = SearchRules.CountGenres(results, _data);
            GenreFacets.ItemsSource = GenreBlock<FacetItem>.Build(
                _data.Genres.Select(g => new FacetItem(g, genreCounts.GetValueOrDefault(g), _filter.Genres.Contains(g))),
                item => item.Name,
                item => GenreTree.ParentOf(_data, item.Name));
            GenreEmptyText.Visibility = _data.Genres.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var actorCounts = SearchRules.Count(results, v => v.Actors);
            foreach (var selected in _filter.Actors)
                actorCounts.TryAdd(selected, 0); // 選了的演員一定留在清單上，才能取消

            var actors = actorCounts
                .Select(pair => new FacetItem(pair.Key, pair.Value, _filter.Actors.Contains(pair.Key)))
                .OrderByDescending(item => item.IsSelected)
                .ThenByDescending(item => item.Count)
                .ThenBy(item => item.Name, CatalogRules.DisplayOrder)
                .ToList();
            ActorFacets.ItemsSource = actors;
            ActorEmptyText.Text = _data.Videos.Any(v => v.Actors.Count > 0)
                ? "目前沒有符合的演員。"
                : "新增影片時填上演員，就會出現在這裡。";
            ActorEmptyText.Visibility = actors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    SetSearchText("");
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Down:
                    FocusFirstResult();
                    e.Handled = true;
                    break;
            }
        }

        private void SetSearchText(string text)
        {
            SearchBox.Text = text;
            _searchTimer.Stop();
            ApplyFilter();
        }

        private void FocusFirstResult()
        {
            _searchTimer.Stop();
            ApplyFilter();
            if (VideoGrid.Items.Count > 0) FocusRow(VideoGrid.Items[0]);
        }

        private void FocusRow(object item)
        {
            VideoGrid.SelectedItem = item;
            VideoGrid.ScrollIntoView(item);
            VideoGrid.UpdateLayout();
            if (VideoGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void GenreFacet_Click(object sender, RoutedEventArgs e) => ToggleFacet(_filter.Genres, sender);

        private void ActorFacet_Click(object sender, RoutedEventArgs e) => ToggleFacet(_filter.Actors, sender);

        private void ToggleFacet(HashSet<string> selected, object sender)
        {
            var name = (string)((ToggleButton)sender).Tag;
            if (!selected.Remove(name)) selected.Add(name);
            ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SetSearchText("");
            SearchBox.Focus();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _filter.Actors.Clear();
            _filter.Genres.Clear();
            SetSearchText("");
            SearchBox.Focus();
        }

        // ===== 新增、修改、刪除 =====

        private void New_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

        private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

        private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void EditSelected()
        {
            if (VideoGrid.SelectedItem is VideoEntry video) OpenEditor(video);
        }

        private void OpenEditor(VideoEntry? video)
        {
            VideoEntry? last = null;
            var editor = new EditVideoWindow(_data, video, (existing, title, actors, genres, newParents) =>
            {
                last = CatalogRules.Upsert(_data, existing, title, actors, genres, DateTime.Now, newParents);
                var saved = Save();
                // 新增的存檔失敗時先拿掉，視窗會留著讓人重按儲存，不然會多出一筆
                if (!saved && existing == null)
                {
                    _data.Videos.Remove(last);
                    last = null;
                }
                ApplyFilter();
                if (saved) ShowMessage(existing == null ? $"已新增「{last!.Title}」" : $"已儲存「{last!.Title}」");
                return saved;
            }) { Owner = this };
            editor.ShowDialog();

            // 改完回到清單上那一部；被目前的搜尋條件濾掉就不用選
            if (last != null && _view.Contains(last)) FocusRow(last);
        }

        private void DeleteSelected()
        {
            var selected = VideoGrid.SelectedItems.Cast<VideoEntry>().ToList();
            if (selected.Count == 0) return;

            var question = selected.Count == 1
                ? $"確定要刪除「{selected[0].Title}」？"
                : $"確定要刪除選取的 {selected.Count} 部影片？";
            if (MessageBox.Show(this, question, "刪除影片", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                != MessageBoxResult.Yes) return;

            foreach (var video in selected) _data.Videos.Remove(video);
            PruneFilter();
            var saved = Save();
            ApplyFilter();
            if (saved) ShowMessage(selected.Count == 1 ? $"已刪除「{selected[0].Title}」" : $"已刪除 {selected.Count} 部影片");
        }

        private void Manage_Click(object sender, RoutedEventArgs e) => OpenManage(startAdding: false);

        /// <summary>左邊「性質」旁的「新增分類」：直接打開管理視窗，游標在新增性質的地方。</summary>
        private void AddGenre_Click(object sender, RoutedEventArgs e) => OpenManage(startAdding: true);

        private void OpenManage(bool startAdding)
        {
            var window = new ManageWindow(_data, message =>
            {
                PruneFilter();
                var saved = Save();
                ApplyFilter();
                if (saved) ShowMessage(message);
            }, startAdding) { Owner = this };
            window.ShowDialog();
        }

        /// <summary>改名或刪除後，篩選條件裡已經不存在的名字要拿掉，不然會一直篩不到東西。</summary>
        private void PruneFilter()
        {
            _filter.Genres.RemoveWhere(g => !_data.Genres.Contains(g, NameComparer.Instance));
            var actors = CatalogRules.AllActors(_data);
            _filter.Actors.RemoveWhere(a => !actors.Contains(a, NameComparer.Instance));
        }

        private bool Save()
        {
            try
            {
                DataStore.Save(_data);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"存檔失敗：{ex.Message}", AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ShowMessage(string message) => MessageText.Text = message;

        // ===== 表格與快速鍵 =====

        private void VideoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EditButton.IsEnabled = VideoGrid.SelectedItems.Count == 1;
            DeleteButton.IsEnabled = VideoGrid.SelectedItems.Count > 0;
        }

        private void VideoGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // 連按標題列或捲軸不算
            if (ItemsControl.ContainerFromElement(VideoGrid, (DependencyObject)e.OriginalSource) is DataGridRow)
                EditSelected();
        }

        private void VideoGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    EditSelected();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    DeleteSelected();
                    e.Handled = true;
                    break;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
            {
                OpenEditor(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !SearchBox.IsKeyboardFocused)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        // ===== 更新 =====

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

        private async Task CheckForUpdateAsync(bool manual)
        {
            const string title = "檢查更新";
            if (!_updater.IsInstalled)
            {
                if (manual) MessageBox.Show(this, "目前是開發版，不會檢查更新。", title);
                return;
            }

            try
            {
                StatusText.Text = "檢查更新中…";
                var info = await _updater.CheckAsync();
                StatusText.Text = $"版本 {_updater.CurrentVersion}";
                if (info == null)
                {
                    if (manual) MessageBox.Show(this, $"目前已經是最新版（{_updater.CurrentVersion}）。", title);
                    return;
                }

                var answer = MessageBox.Show(this,
                    $"發現新版本 {info.Version}（目前 {_updater.CurrentVersion}）。\n\n要現在下載並更新嗎？影片資料不會受影響。",
                    title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                StatusText.Text = "下載更新中…";
                await _updater.DownloadAndInstallAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = $"下載更新中… {p}%"));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動開啟新版
                Application.Current.Shutdown();
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException
                                           or KeyNotFoundException or UnauthorizedAccessException or Win32Exception)
            {
                StatusText.Text = $"版本 {_updater.CurrentVersion}";
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show(this, $"檢查更新失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
