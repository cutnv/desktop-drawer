using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopDrawer
{
    internal sealed class NoteRecord : INotifyPropertyChanged
    {
        internal string Id = Guid.NewGuid().ToString("N"), Title = "", Body = "";
        internal bool Pinned, Deleted, Recovered;
        internal int X, Y, Width, Height;
        internal long Created = DateTime.UtcNow.Ticks;
        public string DisplayTitle { get { return String.IsNullOrWhiteSpace(Title) ? "未命名便签" : Title; } }
        public event PropertyChangedEventHandler PropertyChanged;
        internal void Notify() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("")); }
        public override string ToString() { return DisplayTitle; }
    }

    internal sealed class NoteStore
    {
        private const string Header = "DesktopDrawer.Note.v1";
        private readonly string directory;
        internal readonly List<NoteRecord> Items = new List<NoteRecord>();
        private readonly HashSet<NoteRecord> dirty = new HashSet<NoteRecord>();
        internal event Action<NoteRecord> Changed;
        internal string Error = "", LoadWarning = "";
        internal bool HasPending { get { return dirty.Count > 0; } }
        internal NoteStore(string folder)
        {
            directory = folder;
            if (!Directory.Exists(directory)) return;
            try
            {
                var files = Directory.GetFiles(directory, "*.note*").Where(path => path.EndsWith(".note", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".note.bak", StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? path.Substring(0,path.Length-4) : path).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    NoteRecord note = null;
                    foreach (string candidate in new[] { file, file + ".bak" })
                    {
                        if (!File.Exists(candidate)) continue;
                        try { note = Read(candidate, Path.GetFileNameWithoutExtension(file)); note.Recovered = candidate != file; break; }
                        catch (Exception ex) { Program.Log(ex); }
                    }
                    if (note != null) { Items.Add(note); if (note.Recovered) dirty.Add(note); }
                    else LoadWarning = "部分便签记录无法读取，原文件已保留。";
                }
            }
            catch (Exception ex) { Program.Log(ex); LoadWarning = "便签目录暂时无法读取：" + ex.Message; }
        }
        private static NoteRecord Read(string path, string expectedId)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8); Guid id;
            if (lines.Length != 11 || lines[0] != Header || !Guid.TryParseExact(lines[1], "N", out id) || lines[1] != expectedId ||
                (lines[4] != "0" && lines[4] != "1") || (lines[5] != "0" && lines[5] != "1")) throw new InvalidDataException("便签记录格式无效。");
            return new NoteRecord { Id = lines[1], Title = Encoding.UTF8.GetString(Convert.FromBase64String(lines[2])), Body = Encoding.UTF8.GetString(Convert.FromBase64String(lines[3])),
                Pinned = lines[4] == "1", Deleted = lines[5] == "1", X = Int32.Parse(lines[6],CultureInfo.InvariantCulture), Y = Int32.Parse(lines[7],CultureInfo.InvariantCulture),
                Width = Int32.Parse(lines[8],CultureInfo.InvariantCulture), Height = Int32.Parse(lines[9],CultureInfo.InvariantCulture), Created = Int64.Parse(lines[10],CultureInfo.InvariantCulture) };
        }
        internal NoteRecord Add() { var note = new NoteRecord(); Items.Add(note); Touch(note); return note; }
        internal void Touch(NoteRecord note) { dirty.Add(note); note.Notify(); if (Changed != null) Changed(note); }
        internal bool Flush()
        {
            foreach (NoteRecord note in dirty.ToArray())
            {
                try
                {
                    Guid id; if (!Guid.TryParseExact(note.Id,"N",out id)) throw new InvalidDataException("便签标识无效。");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory,note.Id + ".note");
                    if (note.Recovered && File.Exists(path) && !Native.MoveFileEx(path,path + ".corrupt-" + Guid.NewGuid().ToString("N"),0x2 | 0x8))
                        throw new IOException("无法保留损坏记录，便签暂未保存。");
                    Files.WriteRecord(path,new[] { Header, note.Id, Convert.ToBase64String(Encoding.UTF8.GetBytes(note.Title)), Convert.ToBase64String(Encoding.UTF8.GetBytes(note.Body)),
                        note.Pinned ? "1" : "0", note.Deleted ? "1" : "0", note.X.ToString(CultureInfo.InvariantCulture), note.Y.ToString(CultureInfo.InvariantCulture),
                        note.Width.ToString(CultureInfo.InvariantCulture), note.Height.ToString(CultureInfo.InvariantCulture), note.Created.ToString(CultureInfo.InvariantCulture) });
                    note.Recovered = false; dirty.Remove(note);
                }
                catch (Exception ex) { if (Error != ex.Message) Program.Log(ex); Error = ex.Message; return false; }
            }
            Error = ""; return true;
        }
    }

    internal sealed class NoteEditor : IDisposable
    {
        internal readonly Grid View = new Grid();
        private readonly TextBox title, body;
        private readonly NoteRecord note;
        private readonly NoteStore store;
        private bool syncing;
        internal NoteEditor(NoteRecord record, NoteStore storage, Style inputStyle, Button action = null)
        {
            note = record; store = storage;
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition());
            View.ColumnDefinitions.Add(new ColumnDefinition()); View.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title = new TextBox { Style = inputStyle, FontSize = 14, FontWeight = FontWeights.SemiBold, Padding = new Thickness(8,0,8,0), Margin = new Thickness(0,0,0,8), MaxLength = 200, IsReadOnly = note.Deleted };
            body = new TextBox { Style = inputStyle, Height = Double.NaN, FontSize = 13, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), MaxLength = 1000000, IsReadOnly = note.Deleted };
            System.Windows.Automation.AutomationProperties.SetName(title,"便签标题");
            System.Windows.Automation.AutomationProperties.SetName(body,"便签正文");
            System.Windows.Automation.AutomationProperties.SetAutomationId(title,"NoteTitle." + note.Id);
            System.Windows.Automation.AutomationProperties.SetAutomationId(body,"NoteBody." + note.Id);
            View.Children.Add(title); Grid.SetRow(body,1); Grid.SetColumnSpan(body,2); View.Children.Add(body);
            if (action != null) { action.Margin = new Thickness(7,0,0,8); Grid.SetColumn(action,1); View.Children.Add(action); }
            Sync(); title.TextChanged += Edited; body.TextChanged += Edited; note.PropertyChanged += NoteChanged;
        }
        private void Edited(object sender, TextChangedEventArgs e)
        {
            if (syncing) return;
            note.Title = title.Text; note.Body = body.Text; store.Touch(note);
        }
        private void NoteChanged(object sender, PropertyChangedEventArgs e) { Sync(); }
        private void Sync()
        {
            syncing = true;
            try { if (title.Text != note.Title) title.Text = note.Title; if (body.Text != note.Body) body.Text = note.Body; }
            finally { syncing = false; }
        }
        internal void FocusTitle() { title.Focus(); }
        public void Dispose() { note.PropertyChanged -= NoteChanged; }
    }

    internal sealed class NotesController : IDisposable
    {
        internal readonly Grid View = new Grid { Visibility = Visibility.Collapsed };
        internal readonly NoteStore Store;
        private readonly Window owner;
        private readonly Style buttons, inputs;
        private readonly ToggleButton trash;
        private readonly TextBlock status, empty;
        private readonly ScrollViewer scroller;
        private readonly StackPanel cards;
        private readonly Button previous, next;
        private readonly DispatcherTimer saveTimer;
        private readonly Dictionary<string, NoteEditor> editors = new Dictionary<string, NoteEditor>();
        private string query = "";
        private bool suspended, disposing;
        private double cardWidth = 300;
        private const double Gap = 12;
        internal event Action<bool> BusyChanged;
        internal event Action ResetSearch;
        internal bool IsEditing { get { return View.IsVisible && cards.IsKeyboardFocusWithin; } }
        internal NotesController(Window parent, Style buttonStyle, Style inputStyle, Style scrollStyle, string directory)
        {
            owner = parent; buttons = buttonStyle; inputs = inputStyle; Store = new NoteStore(directory);
            View.Resources[typeof(ScrollBar)] = scrollStyle;
            View.Resources[SystemParameters.VerticalScrollBarWidthKey] = 8.0;
            View.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 8.0;
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); View.RowDefinitions.Add(new RowDefinition());
            var toolbar = new DockPanel { Margin = new Thickness(0,0,0,10) }; View.Children.Add(toolbar);
            var navigation = new StackPanel { Orientation = Orientation.Horizontal };
            previous = MakeButton("‹",delegate { Scroll(-1); }); previous.Width = 32; previous.ToolTip = "向左滚动便签"; previous.AutomationName("向左滚动便签");
            next = MakeButton("›",delegate { Scroll(1); }); next.Width = 32; next.Margin = new Thickness(8,0,0,0); next.ToolTip = "向右滚动便签"; next.AutomationName("向右滚动便签");
            navigation.Children.Add(previous); navigation.Children.Add(next); DockPanel.SetDock(navigation,Dock.Right); toolbar.Children.Add(navigation);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(MakeButton("＋ 新建便签",delegate { NewNote(); }));
            var trashStyle = new Style(typeof(ToggleButton),buttons.BasedOn);
            var checkedStyle = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(65,76,94)))); trashStyle.Triggers.Add(checkedStyle);
            trash = new ToggleButton { Style = trashStyle, Content = "回收站", Height = 30, Padding = new Thickness(8,2,8,2), Margin = new Thickness(8,0,0,0),
                Background = new SolidColorBrush(Color.FromRgb(43,48,58)), Foreground = Brushes.White };
            trash.Checked += delegate { Reload(); }; trash.Unchecked += delegate { Reload(); }; actions.Children.Add(trash);
            DockPanel.SetDock(actions,Dock.Left); toolbar.Children.Add(actions);
            status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(170,180,195)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14,0,14,0), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            toolbar.Children.Add(status);
            cards = new StackPanel { Orientation = Orientation.Horizontal };
            scroller = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false, PanningMode = PanningMode.HorizontalOnly };
            System.Windows.Automation.AutomationProperties.SetName(scroller,"横向便签列表");
            Grid.SetRow(scroller,1); View.Children.Add(scroller);
            empty = new TextBlock { Text = "新建一张便签，记录想法与待办。", Foreground = new SolidColorBrush(Color.FromRgb(170,180,195)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Grid.SetRow(empty,1); View.Children.Add(empty);
            View.SizeChanged += delegate { SizeCards(); };
            scroller.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) SizeCards(); UpdateNavigation(); };
            scroller.PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e) {
                if (scroller.ScrollableWidth <= 0) return;
                var text = FindParent<TextBox>(e.OriginalSource as DependencyObject);
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0 && text != null && text.AcceptsReturn)
                {
                    var inner = FindChild<ScrollViewer>(text);
                    if (inner != null && inner.ScrollableHeight > 0) return;
                }
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset-e.Delta); e.Handled = true;
            };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            saveTimer.Tick += delegate { if (Flush()) saveTimer.Stop(); else saveTimer.Interval = TimeSpan.FromSeconds(2); };
            Store.Changed += delegate { saveTimer.Interval = TimeSpan.FromMilliseconds(500); saveTimer.Start(); UpdateStatus(); };
            // Keep the existing record format; old desktop pins are retired without changing note content.
            foreach (NoteRecord note in Store.Items.Where(item => item.Pinned)) { note.Pinned = false; Store.Touch(note); }
            Reload(); if (Store.HasPending) saveTimer.Start();
        }
        private Button MakeButton(string text, RoutedEventHandler action)
        {
            var button = new Button { Style = buttons, Content = text, Height = 30, Padding = new Thickness(10,3,10,3) }; button.AutomationName(text); button.Click += action; return button;
        }
        internal void SetQuery(string value) { query = value.Trim(); Reload(); }
        internal void NewNote()
        {
            trash.IsChecked = false; query = ""; if (ResetSearch != null) ResetSearch();
            NoteRecord note = Store.Add(); Reload(); scroller.ScrollToHome();
            NoteEditor editor; if (editors.TryGetValue(note.Id,out editor)) editor.FocusTitle();
        }
        private void Reload()
        {
            if (cards == null) return;
            foreach (NoteEditor editor in editors.Values) editor.Dispose(); editors.Clear(); cards.Children.Clear();
            NoteRecord[] matches = Store.Items.Where(note => note.Deleted == (trash.IsChecked == true) && (query.Length == 0 || note.Title.IndexOf(query,StringComparison.CurrentCultureIgnoreCase) >= 0 || note.Body.IndexOf(query,StringComparison.CurrentCultureIgnoreCase) >= 0))
                .OrderByDescending(note => note.Created).ToArray();
            foreach (NoteRecord record in matches)
            {
                NoteRecord note = record;
                var action = MakeButton(note.Deleted ? "恢复" : "删除",delegate { DeleteOrRestore(note); });
                action.FontSize = 11; action.Height = 28; action.Padding = new Thickness(7,2,7,2); action.AutomationName(note.Deleted ? "恢复便签" : "删除便签");
                var editor = new NoteEditor(note,Store,inputs,action); editors.Add(note.Id,editor);
                var card = new Border { Child = editor.View, Width = cardWidth, Height = 160, Padding = new Thickness(10), CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(29,34,42)), BorderBrush = new SolidColorBrush(Color.FromRgb(59,68,82)), BorderThickness = new Thickness(1) };
                cards.Children.Add(card);
            }
            empty.Text = trash.IsChecked == true ? "便签回收站为空。" : query.Length > 0 ? "没有匹配的便签。" : "新建一张便签，记录想法与待办。";
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SizeCards(); scroller.ScrollToHome(); UpdateStatus();
        }
        private void SizeCards()
        {
            double available = scroller.ViewportWidth > 0 ? scroller.ViewportWidth : View.ActualWidth;
            if (available <= 0) return;
            int columns = available >= 1400 ? 5 : available >= 960 ? 4 : Math.Max(1,(int)(available/240));
            cardWidth = Math.Max(120,(available-Gap*(columns-1))/columns);
            double height = Math.Max(80,scroller.ViewportHeight);
            for (int index = 0; index < cards.Children.Count; index++)
            {
                var card = (Border)cards.Children[index]; card.Width = cardWidth; card.Height = height;
                card.Margin = new Thickness(0,0,index < cards.Children.Count-1 ? Gap : 0,0);
            }
            UpdateNavigation();
        }
        private void Scroll(int direction) { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + direction*(cardWidth+Gap)); }
        private void UpdateNavigation()
        {
            previous.IsEnabled = scroller.HorizontalOffset > 0.5;
            next.IsEnabled = scroller.HorizontalOffset < scroller.ScrollableWidth-0.5;
        }
        private void UpdateStatus()
        {
            status.Text = Store.Error.Length > 0 ? "保存失败，将自动重试：" + Store.Error : Store.HasPending ? "正在保存…" : Store.LoadWarning.Length > 0 ? Store.LoadWarning : "已自动保存";
        }
        internal bool Flush() { bool result = Store.Flush(); UpdateStatus(); return result; }
        private void DeleteOrRestore(NoteRecord note)
        {
            if (suspended || disposing) return;
            if (!note.Deleted)
            {
                if (BusyChanged != null) BusyChanged(true);
                try { if (MessageBox.Show(owner,"将“" + note.DisplayTitle + "”移入便签回收站？可随时恢复。","删除便签",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No) != MessageBoxResult.Yes) return; }
                finally { if (BusyChanged != null) BusyChanged(false); }
            }
            note.Deleted = !note.Deleted; note.Pinned = false; Store.Touch(note); Flush(); Reload();
        }
        internal void SetSuspended(bool value) { suspended = value; View.IsEnabled = !value; if (value) Flush(); }
        public void Dispose()
        {
            disposing = true; saveTimer.Stop(); Flush();
            foreach (NoteEditor editor in editors.Values) editor.Dispose(); editors.Clear();
        }
        private static T FindParent<T>(DependencyObject current) where T:DependencyObject
        {
            while (current != null) { if (current is T) return (T)current; current = VisualTreeHelper.GetParent(current); } return null;
        }
        private static T FindChild<T>(DependencyObject parent) where T:DependencyObject
        {
            for (int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
            { var child=VisualTreeHelper.GetChild(parent,index); if(child is T)return (T)child; T found=FindChild<T>(child); if(found != null)return found; }
            return null;
        }
    }
}
