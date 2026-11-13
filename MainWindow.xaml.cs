using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ShortcutBook;

public partial class MainWindow : Window
{
    const string DragFormat = "ShortcutBook.Item";

    public string ListPath { get; private set; }
    ListDocument _doc;
    readonly List<string> _back = new();
    readonly List<string> _fwd = new();
    readonly ObservableCollection<ItemVM> _all = new();
    readonly ListCollectionView _view;

    public static MainWindow? TryCreate(string path)
    {
        try
        {
            var doc = ListDocument.Load(path);
            return new MainWindow(path, doc);
        }
        catch (Exception ex)
        {
            Log.Write("목록 열기 실패 " + path + ": " + ex);
            MessageBox.Show(Loc.F("err_open_list", path, ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    MainWindow(string path, ListDocument doc)
    {
        ListPath = path;
        _doc = doc;
        InitializeComponent();
        ThemeService.Hook(this);

        Width = App.Settings.WindowWidth;
        Height = App.Settings.WindowHeight;

        _view = new ListCollectionView(_all);
        _view.Filter = FilterItem;
        List.ItemsSource = _view;
        LoadList(path, doc);
        ContentRendered += (_, _) =>
        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            List.Focus();
            if (Math.Abs(IconService.Scale - dpi.DpiScaleX) > 0.01)
            {
                IconService.Scale = dpi.DpiScaleX;
                IconService.Clear();
                RefreshAll();
            }
        };
        Closing += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                App.Settings.WindowWidth = ActualWidth;
                App.Settings.WindowHeight = ActualHeight;
                App.Settings.Save();
            }
        };
        App.ViewChanged += OnViewChanged;
        Closed += (_, _) => App.ViewChanged -= OnViewChanged;
    }

    /// <summary>이 창에 목록 하나를 채운다 (처음 열 때와, 창을 다른 목록으로 바꿀 때)</summary>
    void LoadList(string path, ListDocument doc)
    {
        ListPath = path;
        _doc = doc;
        SearchBox.Clear();
        _all.Clear();
        foreach (var it in doc.Items) _all.Add(new ItemVM(it));
        ApplySort();
        UpdateTitle();
        UpdateEmpty();
    }

    // ─────────────── 현재 창에서 다른 목록으로 이동 (뒤로 · 앞으로) ───────────────

    bool TryLoadFile(string path)
    {
        try
        {
            LoadList(path, ListDocument.Load(path));
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("목록 열기 실패 " + path + ": " + ex);
            ShowError(Loc.F("err_open_list", path, ex.Message));
            return false;
        }
    }

    /// <summary>이 창의 내용을 다른 목록으로 바꾼다. 이전 목록은 뒤로 가기 기록에 쌓인다.</summary>
    public void OpenInPlace(string path)
    {
        var old = ListPath;
        var oldDoc = _doc;
        if (!TryLoadFile(path)) { RestoreAfterFailedLoad(old, oldDoc); return; }
        _back.Add(old);
        _fwd.Clear();
        App.Instance.Rekey(old, path, this);
    }

    void RestoreAfterFailedLoad(string oldPath, ListDocument oldDoc)
    {
        if (ListPath != oldPath) LoadList(oldPath, oldDoc);
    }

    bool Navigate(List<string> from, List<string> to)
    {
        if (from.Count == 0) return false;
        var target = from[^1];
        if (!File.Exists(target))
        {
            from.RemoveAt(from.Count - 1);
            ShowError(Loc.F("err_target_missing", target));
            return true;
        }
        var other = App.Instance.FindWindow(target, this);
        if (other != null) { App.Instance.Activate(other); return true; }

        var old = ListPath;
        var oldDoc = _doc;
        if (!TryLoadFile(target)) { RestoreAfterFailedLoad(old, oldDoc); return true; }
        from.RemoveAt(from.Count - 1);
        to.Add(old);
        App.Instance.Rekey(old, target, this);
        return true;
    }

    void GoBack() => Navigate(_back, _fwd);
    void GoForward() => Navigate(_fwd, _back);

    void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1) { GoBack(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { GoForward(); e.Handled = true; }
    }

    // ─────────────── 키보드: Enter 실행 · 수정 단축키 · Backspace 뒤로 ───────────────

    void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool typing = Keyboard.FocusedElement is TextBox tb && tb != SearchBox;

        if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !typing)
        {
            if (e.IsRepeat) { e.Handled = true; return; }
            var sel = SelectedVms(); // 선택이 없으면 아무것도 실행하지 않는다
            if (sel.Count > 0) { RunAll(sel); e.Handled = true; }
            return;
        }

        if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox)
        {
            if (!e.IsRepeat) DeleteItems(SelectedVms()); // 확인 창을 거쳐 삭제
            e.Handled = true;
            return;
        }

        if (Hotkey.TryParse(App.Settings.EditHotkey, out var hk, out var hm) && key == hk && Keyboard.Modifiers == hm && !typing)
        {
            if (!e.IsRepeat && List.SelectedItems.Count == 1 && List.SelectedItem is ItemVM one) EditItem(one);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox)
        {
            // Backspace / 왼쪽 방향키 = 이전 목록, 오른쪽 방향키 = 다시 앞으로
            if ((key == Key.Back || key == Key.Left) && _back.Count > 0) { GoBack(); e.Handled = true; }
            else if (key == Key.Right && _fwd.Count > 0) { GoForward(); e.Handled = true; }
        }
    }

    void OnViewChanged()
    {
        RefreshAll();
        UpdateTitle();
        UpdateEmpty();
    }

    void RefreshAll()
    {
        foreach (var vm in _all) vm.Refresh();
    }

    void UpdateTitle()
    {
        Title = Path.GetFileNameWithoutExtension(ListPath) + " - " + Loc.T("app_title");
    }

    void UpdateEmpty()
    {
        EmptyHint.Text = _all.Count == 0 ? Loc.T("empty_hint") : Loc.T("no_result");
        EmptyHint.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─────────────── 저장 ───────────────

    void Save()
    {
        try
        {
            _doc.Items = _all.Select(v => v.Item).ToList();
            _doc.Save(ListPath);
        }
        catch (Exception ex)
        {
            Log.Write("목록 저장 실패 " + ListPath + ": " + ex);
            MessageBox.Show(this, Loc.F("err_save_list", ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────── 검색 · 정렬 ───────────────

    bool FilterItem(object o)
    {
        var q = SearchBox?.Text.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        var i = ((ItemVM)o).Item;
        return Has(i.Name, q) || Has(i.Description, q) || Has(i.Target, q) || Has(i.Arguments, q)
            || Has(i.Memo, q) || Has(i.WebUrl, q) || Has(i.ManualPath, q);
    }

    static bool Has(string? s, string q) => s != null && s.Contains(q, StringComparison.OrdinalIgnoreCase);

    bool Searching => !string.IsNullOrWhiteSpace(SearchBox.Text);
    bool CanReorder => _doc.Sort == SortMode.Custom && !Searching;

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _view.Refresh();
        UpdateEmpty();
        UpdateReorderFlag();
    }

    void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    void ApplySort()
    {
        _view.CustomSort = _doc.Sort switch
        {
            SortMode.NameAsc => new NameComparer(false),
            SortMode.NameDesc => new NameComparer(true),
            _ => null,
        };
        UpdateReorderFlag();
    }

    void UpdateReorderFlag()
    {
        bool can = CanReorder;
        foreach (var vm in _all) vm.CanReorder = can;
    }

    void OnSort(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(SortMode mode, string key)
        {
            var mi = new MenuItem { Header = Loc.T(key), IsCheckable = true, IsChecked = _doc.Sort == mode };
            mi.Click += (_, _) =>
            {
                _doc.Sort = mode;
                ApplySort();
                Save();
            };
            menu.Items.Add(mi);
        }
        Add(SortMode.Custom, "sort_custom");
        Add(SortMode.NameAsc, "sort_name_asc");
        Add(SortMode.NameDesc, "sort_name_desc");
        menu.IsOpen = true;
    }

    // ─────────────── 추가 · 수정 · 삭제 ───────────────

    void OnAdd(object sender, RoutedEventArgs e) => AddItem(new ListItem());

    void AddItem(ListItem seed)
    {
        var dlg = new ItemEditWindow(seed, isNew: true) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Deleted) return;
        var (vm, skipped) = Place(dlg.Result);
        if (skipped)
        {
            MessageBox.Show(this, Loc.F("dup_skipped_msg", dlg.Result.Target), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Save();
        UpdateEmpty();
        if (vm != null) List.ScrollIntoView(vm);
    }

    void AddMany(IEnumerable<string> paths)
    {
        ItemVM? last = null;
        foreach (var f in paths)
        {
            var it = ShortcutImport.FromPath(f);
            if (string.IsNullOrWhiteSpace(it.Name)) it.Name = Path.GetFileName(f);
            var (vm, skipped) = Place(it);
            if (!skipped) last = vm;
        }
        if (last == null) return;
        Save();
        UpdateEmpty();
        List.ScrollIntoView(last);
    }

    /// <summary>설정의 "같은 경로·주소 등록" 방식에 따라 목록에 넣는다. 제외하면 skipped = true.</summary>
    (ItemVM? Vm, bool Skipped) Place(ListItem it)
    {
        var dup = App.Settings.DuplicateMode == 0 ? null : FindDuplicate(it);
        if (dup == null)
        {
            var vm = new ItemVM(it) { CanReorder = CanReorder };
            _all.Add(vm);
            return (vm, false);
        }
        if (App.Settings.DuplicateMode == 1) return (null, true);
        dup.Item.CopyFrom(it);
        dup.Refresh();
        _view.Refresh();
        return (dup, false);
    }

    ItemVM? FindDuplicate(ListItem it)
    {
        var key = TargetKey(it.Target);
        if (key.Length == 0) return null;
        return _all.FirstOrDefault(v => TargetKey(v.Item.Target) == key);
    }

    static string TargetKey(string target)
    {
        var t = Launcher.Expand(target ?? "").Trim();
        if (t.Length == 0) return "";
        if (Launcher.IsUrl(t)) return t.TrimEnd('/').ToLowerInvariant();
        try { return Path.GetFullPath(t).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return t.ToLowerInvariant(); }
    }

    void DeleteItem(ItemVM vm) => DeleteItems(new List<ItemVM> { vm });

    void DeleteItems(List<ItemVM> vms)
    {
        if (vms.Count == 0) return;
        var msg = vms.Count == 1 ? Loc.F("confirm_delete", vms[0].Name) : Loc.F("confirm_delete_many", vms.Count);
        var r = MessageBox.Show(this, msg, AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        foreach (var vm in vms) _all.Remove(vm);
        Save();
        UpdateEmpty();
    }

    void RunAll(List<ItemVM> vms)
    {
        foreach (var vm in vms)
        {
            var err = Launcher.Run(vm.Item, this);
            if (!string.IsNullOrEmpty(err)) { ShowError(err); break; }
        }
    }

    /// <summary>선택한 항목들을 (현재 순서를 유지한 채) 목록 맨 위로 올린다. 임의 정렬일 때만 의미가 있다.</summary>
    void MoveToTop(List<ItemVM> vms)
    {
        var set = new HashSet<ItemVM>(vms);
        var ordered = _all.Where(set.Contains).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int from = _all.IndexOf(ordered[i]);
            if (from != i) _all.Move(from, i);
        }
        Save();
        if (ordered.Count > 0) List.ScrollIntoView(ordered[0]);
    }

    void EditItem(ItemVM vm)
    {
        var dlg = new ItemEditWindow(vm.Item.Clone(), isNew: false) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Deleted)
        {
            _all.Remove(vm);
        }
        else
        {
            vm.Item.CopyFrom(dlg.Result);
            vm.Refresh();
            _view.Refresh();
        }
        Save();
        UpdateEmpty();
    }

    static ItemVM? VmOf(object sender) => (sender as FrameworkElement)?.DataContext as ItemVM;

    void OnEditClick(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) EditItem(vm); }

    void OnManualClick(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ShowError(Launcher.OpenManual(vm.Item)); }
    void OnWebClick(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ShowError(Launcher.OpenWeb(vm.Item)); }

    void OnMemoClick(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is not { } vm) return;
        new MemoWindow(vm.Name, vm.Item.Memo) { Owner = this }.Show();
    }

    void ShowError(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            MessageBox.Show(this, message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ─────────────── 실행 · 우클릭 메뉴 ───────────────

    static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    ItemVM? ItemAt(Point posInList)
    {
        var hit = List.InputHitTest(posInList) as DependencyObject;
        var container = FindAncestor<ListBoxItem>(hit);
        return container == null ? null : List.ItemContainerGenerator.ItemFromContainer(container) as ItemVM;
    }

    void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) != null) return;
        if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(e.OriginalSource as DependencyObject) != null) return;
        var vm = ItemAt(e.GetPosition(List));
        if (vm != null) ShowError(Launcher.Run(vm.Item, this));
    }

    /// <summary>여러 개를 선택해 둔 상태에서 선택된 줄을 우클릭하면 선택이 한 줄로 줄지 않게 한다.</summary>
    void OnListRightDown(object sender, MouseButtonEventArgs e)
    {
        var vm = ItemAt(e.GetPosition(List));
        if (vm != null && List.SelectedItems.Count > 1 && List.SelectedItems.Contains(vm)) e.Handled = true;
    }

    void OnListRightClick(object sender, MouseButtonEventArgs e)
    {
        var vm = ItemAt(e.GetPosition(List));
        if (vm == null) return;
        if (!List.SelectedItems.Contains(vm)) List.SelectedItem = vm;
        var sel = SelectedVms();
        bool custom = _doc.Sort == SortMode.Custom;

        var menu = new ContextMenu { PlacementTarget = List };
        MenuItem Mi(string key, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = Loc.T(key), IsEnabled = enabled };
            mi.Click += (_, _) => act();
            return mi;
        }
        if (sel.Count > 1)
        {
            // 여러 개를 선택했을 때: 실행 · 맨 위로 · 삭제
            menu.Items.Add(Mi("menu_run", () => RunAll(sel)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Mi("menu_to_top", () => MoveToTop(sel), custom));
            menu.Items.Add(Mi("menu_delete", () => DeleteItems(sel)));
        }
        else
        {
            var t = Launcher.Expand(vm.Item.Target);
            bool local = t.Length > 0 && !Launcher.IsUrl(t) && (File.Exists(t) || Directory.Exists(t));
            menu.Items.Add(Mi("menu_run", () => ShowError(Launcher.Run(vm.Item, this)), t.Length > 0));
            menu.Items.Add(Mi("menu_open_location", () => Launcher.OpenLocation(vm.Item), local));
            menu.Items.Add(new Separator());
            menu.Items.Add(Mi("menu_to_top", () => MoveToTop(new List<ItemVM> { vm }), custom));
            menu.Items.Add(Mi("menu_edit", () => EditItem(vm)));
            menu.Items.Add(Mi("menu_delete", () => DeleteItem(vm)));
        }
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ─────────────── 설정 · 도움말 ───────────────

    void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    void OnHelp(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

    // ─────────────── 끌어서 순서 바꾸기 ───────────────

    ItemVM? _dragVm;
    Point _dragStart;
    bool _dragArmed;
    ItemVM? _markVm;

    void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (VmOf(sender) is not { } vm || !CanReorder) return;
        _dragVm = vm;
        _dragStart = e.GetPosition(this);
        _dragArmed = true;
        // 여러 개를 선택해 둔 줄의 손잡이를 누르면 선택이 한 줄로 줄어들지 않게 한다 (함께 끌기 위해)
        if (List.SelectedItems.Count > 1 && List.SelectedItems.Contains(vm)) e.Handled = true;
    }

    void OnHandleUp(object sender, MouseButtonEventArgs e) => _dragArmed = false;

    void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || _dragVm == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragArmed = false;
        var sel = SelectedVms();
        if (!sel.Contains(_dragVm)) { List.SelectedItem = _dragVm; sel = new List<ItemVM> { _dragVm }; }
        _dragVms = sel;
        _dragSource = this;
        _dragItems = sel;
        // 끄는 동안 마우스 휠로 (커서 아래 창의) 목록을 스크롤할 수 있게 한다
        using var wheel = new DragWheelHook((pt, delta) =>
        {
            foreach (var w in Application.Current.Windows.OfType<MainWindow>())
                DragWheelHook.ScrollListAt(w.List, pt, delta);
        });
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, _dragVm.Item.Id), DragDropEffects.Move); }
        finally { ClearMark(); _dragVm = null; _dragVms = new(); _dragSource = null; _dragItems = null; }
    }

    // 끌고 있는 항목 (프로그램 안의 다른 창으로도 옮길 수 있게 정적으로 둔다)
    static MainWindow? _dragSource;
    static List<ItemVM>? _dragItems;
    List<ItemVM> _dragVms = new();

    /// <summary>다른 창에서 끌어 온 항목이면 그 창을 돌려준다.</summary>
    MainWindow? ForeignSource => _dragSource != null && _dragSource != this && _dragItems != null ? _dragSource : null;

    /// <summary>선택된 항목들 (화면 순서)</summary>
    List<ItemVM> SelectedVms()
    {
        var set = new HashSet<ItemVM>(List.SelectedItems.Cast<ItemVM>());
        return _view.Cast<ItemVM>().Where(set.Contains).ToList();
    }

    /// <summary>다른 창이나 다른 목록에서 옮겨 온 항목을 이 목록 끝에 넣는다.</summary>
    /// <summary>같은 경로·주소 등록 설정을 따른다. 제외되면 false (원래 목록에서 빼지 않는다).</summary>
    bool ReceiveItem(ListItem it)
    {
        var (vm, skipped) = Place(it);
        if (skipped) return false;
        Save();
        UpdateEmpty();
        if (vm != null) List.ScrollIntoView(vm);
        return true;
    }

    /// <summary>파일로만 있는 목록에 넣을 때의 같은 처리.</summary>
    static bool PlaceInto(List<ListItem> items, ListItem it)
    {
        var key = TargetKey(it.Target);
        var dup = App.Settings.DuplicateMode == 0 || key.Length == 0 ? null : items.FirstOrDefault(x => TargetKey(x.Target) == key);
        if (dup == null) { items.Add(it); return true; }
        if (App.Settings.DuplicateMode == 1) return false;
        dup.CopyFrom(it);
        return true;
    }

    void ShowSkipped(ListItem it) =>
        MessageBox.Show(this, Loc.F("dup_skipped_msg", it.Target), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>항목을 이 목록에서 빼낸다 (다른 곳으로 옮긴 뒤).</summary>
    void RemoveMoved(IEnumerable<ItemVM> vms)
    {
        foreach (var vm in vms) _all.Remove(vm);
        Save();
        UpdateEmpty();
    }

    /// <summary>다른 창에서 끌어 온 항목들을 이 창으로 옮긴다.</summary>
    void TakeFromOtherWindow()
    {
        var src = _dragSource; var items = _dragItems?.ToList();
        if (src == null || items == null || src == this) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var moved = new List<ItemVM>();
            ListItem? skipped = null;
            foreach (var it in items) { if (ReceiveItem(it.Item)) moved.Add(it); else skipped ??= it.Item; }
            if (moved.Count > 0) src.RemoveMoved(moved);
            if (skipped != null) ShowSkipped(skipped);
        }));
    }

    bool IsListItem(ItemVM vm)
    {
        var t = Launcher.Expand(vm.Item.Target);
        return t.EndsWith(AppInfo.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(t);
    }

    /// <summary>끌고 있는 위치가 "다른 목록" 줄의 가운데면 그 줄 (그 목록 안으로 옮기기).</summary>
    ItemVM? IntoTarget(Point pos)
    {
        var vm = ItemAt(pos);
        if (vm == null || _dragVms.Contains(vm) || !IsListItem(vm)) return null;
        var c = (FrameworkElement)List.ItemContainerGenerator.ContainerFromItem(vm);
        double y = e_Y(c, pos);
        return y >= c.ActualHeight * 0.25 && y <= c.ActualHeight * 0.75 ? vm : null;
    }

    /// <summary>항목을 다른 목록 파일 안으로 옮긴다. 그 목록이 다른 창에 열려 있으면 그 창에 넣는다.</summary>
    void MoveIntoList(List<ItemVM> drags, ItemVM listRow)
    {
        string path;
        try { path = Path.GetFullPath(Launcher.Expand(listRow.Item.Target)); }
        catch { return; }
        if (path.Equals(ListPath, StringComparison.OrdinalIgnoreCase)) return;
        var w = App.Instance.FindWindow(path, this);
        var moved = new List<ItemVM>();
        ListItem? skipped = null;
        if (w != null)
        {
            foreach (var d in drags) { if (w.ReceiveItem(d.Item)) moved.Add(d); else skipped ??= d.Item; }
        }
        else
        {
            try
            {
                var doc = ListDocument.Load(path);
                foreach (var d in drags) { if (PlaceInto(doc.Items, d.Item)) moved.Add(d); else skipped ??= d.Item; }
                if (moved.Count > 0) doc.Save(path);
            }
            catch (Exception ex)
            {
                Log.Write("다른 목록으로 옮기기 실패 " + path + ": " + ex);
                ShowError(Loc.F("err_save_list", ex.Message));
                return;
            }
        }
        if (moved.Count > 0) RemoveMoved(moved);
        if (skipped != null) ShowSkipped(skipped);
    }

    void ClearMark()
    {
        if (_markVm != null) { _markVm.DropMark = 0; _markVm = null; }
    }

    /// <summary>끄는 위치에서 놓일 줄과 위/아래를 구해 표시한다.</summary>
    (ItemVM? vm, bool above) UpdateMark(Point pos)
    {
        ItemVM? vm = ItemAt(pos);
        bool above;
        if (vm != null)
        {
            var c = (FrameworkElement)List.ItemContainerGenerator.ContainerFromItem(vm);
            above = e_Y(c, pos) < c.ActualHeight / 2;
        }
        else
        {
            var items = _view.Cast<ItemVM>().ToList();
            if (items.Count == 0) return (null, false);
            above = pos.Y < List.ActualHeight / 2;
            vm = above ? items[0] : items[^1];
        }

        if (_markVm != vm) ClearMark();
        _markVm = vm;
        vm.DropMark = above ? 1 : 2;
        return (vm, above);
    }

    double e_Y(FrameworkElement container, Point posInList) => List.TranslatePoint(posInList, container).Y;

    void AutoScroll(Point pos)
    {
        var sv = FindScrollViewer(List);
        if (sv == null) return;
        if (pos.Y < 28) sv.ScrollToVerticalOffset(sv.VerticalOffset - 1);
        else if (pos.Y > List.ActualHeight - 28) sv.ScrollToVerticalOffset(sv.VerticalOffset + 1);
    }

    static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var r = FindScrollViewer(VisualTreeHelper.GetChild(d, i));
            if (r != null) return r;
        }
        return null;
    }

    static bool HasExternal(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || ExternalUrl(data) != null;

    static string? ExternalUrl(IDataObject data)
    {
        foreach (var fmt in new[] { DataFormats.UnicodeText, DataFormats.Text })
        {
            if (data.GetDataPresent(fmt) && data.GetData(fmt) is string s)
            {
                s = s.Trim();
                if (!s.Contains('\n') && Launcher.IsUrl(s)) return s;
            }
        }
        return null;
    }

    void OnListDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            if (ForeignSource != null) { ClearMark(); e.Effects = DragDropEffects.Move; e.Handled = true; return; }
            if (!CanReorder) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            var pos = e.GetPosition(List);
            var into = IntoTarget(pos);
            if (into != null)
            {
                if (_markVm != into) ClearMark();
                _markVm = into; into.DropMark = 3;
                e.Effects = DragDropEffects.Move; e.Handled = true; return;
            }
            UpdateMark(pos);
            AutoScroll(pos);
            e.Effects = DragDropEffects.Move;
        }
        else e.Effects = HasExternal(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnListDragLeave(object sender, DragEventArgs e) => ClearMark();

    void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            if (ForeignSource != null) { ClearMark(); e.Handled = true; TakeFromOtherWindow(); return; }
            var drags = _dragVms.ToList();
            var into = drags.Count > 0 && CanReorder ? IntoTarget(e.GetPosition(List)) : null;
            if (into != null)
            {
                ClearMark();
                e.Handled = true;
                Dispatcher.BeginInvoke(new Action(() => MoveIntoList(drags, into)));
                return;
            }
            var (target, above) = UpdateMark(e.GetPosition(List));
            ClearMark();
            e.Handled = true;
            if (drags.Count == 0 || target == null || drags.Contains(target) || !CanReorder) return;

            // 끌어 온 항목들(화면 순서 그대로)을 놓은 자리 앞/뒤로 모은다
            var set = new HashSet<ItemVM>(drags);
            var final = _all.Where(v => !set.Contains(v)).ToList();
            int at = final.IndexOf(target) + (above ? 0 : 1);
            final.InsertRange(at, _all.Where(set.Contains));
            bool changed = false;
            for (int i = 0; i < final.Count; i++)
            {
                if (ReferenceEquals(_all[i], final[i])) continue;
                _all.Move(_all.IndexOf(final[i]), i);
                changed = true;
            }
            if (changed) Save();
            return;
        }
        OnWindowDrop(sender, e);
    }

    // ─────────────── 파일·주소를 끌어다 놓아 추가 ───────────────

    void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            // 목록 안의 순서 변경은 목록이 처리한다. 다른 창에서 끌어 온 항목은 어디에 놓아도 이 창으로 옮긴다.
            if (ForeignSource != null) { e.Effects = DragDropEffects.Move; e.Handled = true; }
            return;
        }
        e.Effects = HasExternal(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            if (ForeignSource != null) { e.Handled = true; TakeFromOtherWindow(); }
            return;
        }
        e.Handled = true;

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            // 드롭을 처리한 뒤에 추가한다 (끌고 온 프로그램이 멈춰 보이지 않게).
            // 하나면 세부 설정 팝업, 여럿이면 팝업 없이 이름 그대로 일괄 등록한다.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (files.Length == 1) { AddItem(ShortcutImport.FromPath(files[0])); return; }
                AddMany(files);
            }));
            return;
        }
        var url = ExternalUrl(e.Data);
        if (url != null) Dispatcher.BeginInvoke(new Action(() => AddItem(ShortcutImport.FromUrl(url))));
    }
}
