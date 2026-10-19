using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopDrawer
{
    internal sealed class DesktopLock : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint kind);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        private readonly Style buttonStyle;
        private readonly Dispatcher dispatcher;
        private readonly DesktopPin pin;
        private readonly EventWaitHandle recoverySignal;
        private readonly List<Window> windows = new List<Window>();
        private readonly Dictionary<Window, PinView> views = new Dictionary<Window, PinView>();
        private readonly HashSet<IntPtr> handles = new HashSet<IntPtr>();
        private readonly List<IntPtr> hiddenTaskbars = new List<IntPtr>();
        private readonly DispatcherTimer monitor;
        private readonly HookProc keyboardCallback, mouseCallback;
        private IntPtr keyboardHook, mouseHook, originalForeground;
        private Window unlockWindow;
        private Button unlockEntry;
        private object shell;
        private bool verifying, recovery, rebuilding, displayPending;
        private int failedAttempts;
        private DateTime retryAfter = DateTime.MinValue;
        private sealed class PinView
        {
            internal Border Card;
            internal PasswordBox Input;
            internal TextBlock Status;
            internal Button Submit;
        }
        private static readonly Style PinStyle = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type PasswordBox}'>
            <Setter Property='Background' Value='#232831'/><Setter Property='Foreground' Value='#F5F7FA'/><Setter Property='BorderBrush' Value='#657080'/><Setter Property='BorderThickness' Value='1'/>
            <Setter Property='Padding' Value='12,6'/><Setter Property='FontSize' Value='22'/><Setter Property='Height' Value='42'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
            <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type PasswordBox}'><Border CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center' Focusable='False'/></Border></ControlTemplate></Setter.Value></Setter>
        </Style>");
        internal bool IsLocked { get; private set; }
        internal bool IsVerifying { get { return verifying; } }
        internal event Action Unlocked;
        internal static string RecoveryEventName(int processId) { return @"Local\DesktopDrawer.LockRecovery." + processId; }

        internal DesktopLock(Style style, Dispatcher ui, DesktopPin credential)
        {
            buttonStyle = style; dispatcher = ui; pin = credential;
            recoverySignal = new EventWaitHandle(false, EventResetMode.ManualReset, RecoveryEventName(System.Diagnostics.Process.GetCurrentProcess().Id));
            keyboardCallback = KeyboardHook; mouseCallback = MouseHook;
            monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            monitor.Tick += Monitor;
        }
        internal void Enter()
        {
            if (IsLocked) return;
            pin.CheckReadable();
            originalForeground = Native.GetForegroundWindow();
            failedAttempts = 0; retryAfter = DateTime.MinValue;
            IsLocked = true;
            try
            {
                recoverySignal.Set();
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                ((dynamic)shell).MinimizeAll();
                HideTaskbars();
                CreateWindows();
                keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
                mouseHook = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
                if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用桌面输入保护。");
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
                monitor.Start();
                Program.LogDesktopLock("LOCKED_BY_MANUAL_CLICK");
            }
            catch { Exit(); throw; }
        }
        private void HideTaskbars()
        {
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused) {
                var name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
                if ((name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd") && Native.IsWindowVisible(window))
                {
                    if (!hiddenTaskbars.Contains(window)) hiddenTaskbars.Add(window);
                    Native.ShowWindow(window, 0);
                }
                return true;
            }, IntPtr.Zero);
        }
        private void CreateWindows()
        {
            foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            {
                Window window = CreatePassiveOverlay();
                window.Closing += delegate(object sender, CancelEventArgs e) { if (IsLocked && !rebuilding) e.Cancel = true; };
                window.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
                windows.Add(window);
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                handles.Add(handle);
                Native.SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x10);
                window.Show();
                Native.SetWindowPos(handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x10 | 0x40);
            }
            unlockWindow = CreateUnlockWindow();
            windows.Add(unlockWindow);
            handles.Add(new WindowInteropHelper(unlockWindow).EnsureHandle());
            PositionUnlockWindow(false);
            unlockWindow.Show(); PositionUnlockWindow(false);
            unlockWindow.Activate(); unlockWindow.Focus();
        }
        internal static Window CreatePassiveOverlay()
        {
            var window = new Window { Title = "桌面锁", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, Width = 300, Height = 200 };
            window.SourceInitialized += delegate {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                // Fullscreen input shields must not become the foreground fullscreen app that pauses Wallpaper Engine.
                SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080));
            };
            return window;
        }
        private Window CreateUnlockWindow()
        {
            var window = new Window { Title = "桌面解锁", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, Width = 72, Height = 28 };
            var panel = new Grid(); window.Content = panel;
            var entry = new Button { Style = buttonStyle, Content = "解锁", FontSize = 11, Height = 28,
                Padding = new Thickness(10, 3, 10, 3), Opacity = 0.55, Cursor = Cursors.Hand, ToolTip = "解锁桌面" };
            unlockEntry = entry; entry.AutomationName("解锁桌面");
            entry.MouseEnter += delegate { entry.Opacity = 0.95; };
            entry.MouseLeave += delegate { entry.Opacity = 0.55; };
            entry.Click += delegate { ShowPin(window); }; panel.Children.Add(entry);
            var input = NewPinInput();
            var status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(242, 166, 166)), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            var submit = new Button { Style = buttonStyle, Content = "解锁", Height = 36, Margin = new Thickness(0, 12, 0, 0) };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = "输入桌面锁 PIN", Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(0, 0, 0, 16) });
            content.Children.Add(input); content.Children.Add(status); content.Children.Add(submit);
            var cancel = new Button { Style = buttonStyle, Content = "取消", Height = 32, Margin = new Thickness(0, 8, 0, 0) }; content.Children.Add(cancel);
            var card = new Border { Background = new SolidColorBrush(Color.FromArgb(244, 24, 27, 33)), BorderBrush = new SolidColorBrush(Color.FromRgb(86, 96, 111)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(24), Width = 300, Child = content, Visibility = Visibility.Collapsed };
            panel.Children.Add(card);
            views.Add(window, new PinView { Card = card, Input = input, Status = status, Submit = submit });
            submit.Click += delegate { Verify(window); };
            cancel.Click += delegate { HidePin(); };
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (card.Visibility != Visibility.Visible) return;
                if (e.Key == Key.Enter) { e.Handled = true; Verify(window); }
                else if (e.Key == Key.Escape) { e.Handled = true; HidePin(); }
            };
            window.Closing += delegate(object sender, CancelEventArgs e) { if (IsLocked && !rebuilding) e.Cancel = true; };
            window.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            return window;
        }
        private void PositionUnlockWindow(bool expanded)
        {
            if (unlockWindow == null) return;
            IntPtr handle = new WindowInteropHelper(unlockWindow).EnsureHandle();
            var bounds = Forms.Screen.PrimaryScreen.Bounds;
            double scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96.0;
            int width = (int)Math.Round((expanded ? 300 : 72) * scale), height = (int)Math.Round((expanded ? 280 : 28) * scale);
            int margin = (int)Math.Round(18 * scale);
            Native.SetWindowPos(handle, new IntPtr(-1), bounds.Right - margin - width, bounds.Bottom - margin - height, width, height, 0x10);
        }
        private void HidePin()
        {
            if (!IsLocked || verifying || unlockWindow == null) return;
            PinView view = views[unlockWindow]; view.Input.Clear(); view.Card.Visibility = Visibility.Collapsed;
            unlockEntry.Visibility = Visibility.Visible; PositionUnlockWindow(false);
        }
        private void ShowPin(Window owner)
        {
            if (!IsLocked || verifying || unlockWindow == null) return;
            owner = unlockWindow;
            foreach (var pair in views) { pair.Value.Input.Clear(); pair.Value.Card.Visibility = pair.Key == owner ? Visibility.Visible : Visibility.Collapsed; }
            unlockEntry.Visibility = Visibility.Collapsed; PositionUnlockWindow(true);
            owner.Activate(); views[owner].Input.Focus();
        }
        private async void Verify(Window owner)
        {
            if (!IsLocked || verifying) return;
            PinView view = views[owner];
            if (view.Card.Visibility != Visibility.Visible) { ShowPin(owner); return; }
            if (DateTime.UtcNow < retryAfter) return;
            string attempt = view.Input.Password; view.Input.Clear();
            if (!DesktopPin.IsValid(attempt)) { view.Status.Text = "请输入 6 位数字 PIN。"; view.Input.Focus(); return; }
            verifying = true;
            view.Submit.IsEnabled = false; view.Input.IsEnabled = false;
            try
            {
                bool verified = await Task.Run(delegate { return pin.Verify(attempt); });
                Program.LogDesktopLock(verified ? "PIN_VERIFIED" : "PIN_REJECTED");
                if (verified && IsLocked) Exit();
                else
                {
                    failedAttempts++;
                    if (failedAttempts >= 5) { retryAfter = DateTime.UtcNow.AddSeconds(30); failedAttempts = 0; }
                    view.Status.Text = "PIN 不正确，请重试。";
                }
            }
            catch (Exception ex) { Program.Log(ex); view.Status.Text = "无法读取 PIN 记录，请通过任务管理器退出后重新配置。"; }
            finally
            {
                verifying = false;
                if (IsLocked) { view.Input.IsEnabled = true; view.Submit.IsEnabled = DateTime.UtcNow >= retryAfter; owner.Activate(); view.Input.Focus(); }
            }
        }
        private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || !IsLocked || recovery) return CallNextHookEx(keyboardHook, code, message, data);
            int kind = message.ToInt32();
            // Always pass releases so modifiers held when locking cannot become stuck.
            if (kind == 0x101 || kind == 0x105) return CallNextHookEx(keyboardHook, code, message, data);
            int key = Marshal.ReadInt32(data);
            if (key == 0x5B || key == 0x5C || key == 0x12 || key == 0xA4 || key == 0xA5) return new IntPtr(1);
            bool control = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            if (control && key != 0x41 && key != 0x56 && key != 0x43 && key != 0x58 && key != 0x11 && key != 0xA2 && key != 0xA3) return new IntPtr(1);
            IntPtr foreground = Native.GetForegroundWindow();
            Window inputOwner = views.Keys.FirstOrDefault(window => new WindowInteropHelper(window).Handle == foreground && views[window].Card.Visibility == Visibility.Visible);
            if (inputOwner != null && !verifying) return CallNextHookEx(keyboardHook, code, message, data);
            if ((kind == 0x100 || kind == 0x104) && key == 13)
                dispatcher.BeginInvoke(new Action(delegate { if (IsLocked && unlockWindow != null) ShowPin(unlockWindow); }));
            return new IntPtr(1);
        }
        private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || !IsLocked || recovery) return CallNextHookEx(mouseHook, code, message, data);
            var point = new Native.Point { X = Marshal.ReadInt32(data), Y = Marshal.ReadInt32(data, 4) };
            if (!handles.Contains(GetAncestor(WindowFromPoint(point), 2))) return new IntPtr(1);
            return CallNextHookEx(mouseHook, code, message, data);
        }
        private void Monitor(object sender, EventArgs e)
        {
            if (!IsLocked) return;
            IntPtr foreground = Native.GetForegroundWindow();
            var name = new StringBuilder(64); Native.GetClassName(foreground, name, name.Capacity);
            // Keep the OS Task Manager available as the explicitly accepted emergency recovery route.
            recovery = name.ToString() == "TaskManagerWindow";
            foreach (PinView view in views.Values)
                if (DateTime.UtcNow < retryAfter) { view.Submit.IsEnabled = false; view.Status.Text = "尝试过多，请在 " + Math.Ceiling((retryAfter - DateTime.UtcNow).TotalSeconds) + " 秒后重试。"; }
                else if (!verifying) { view.Submit.IsEnabled = true; if (view.Status.Text.StartsWith("尝试过多")) view.Status.Text = "请输入 PIN。"; }
            if (displayPending && !verifying) { displayPending = false; RebuildWindows(); }
            // Keep the small unlock window above the passive input shields and keep keyboard focus there.
            foreach (Window window in windows) Native.SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(recovery ? -2 : -1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            IntPtr unlockHandle = unlockWindow == null ? IntPtr.Zero : new WindowInteropHelper(unlockWindow).Handle;
            if (!recovery && unlockHandle != IntPtr.Zero && foreground != unlockHandle) SetForegroundWindow(unlockHandle);
            if (!recovery) HideTaskbars();
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            dispatcher.BeginInvoke(new Action(delegate {
                if (!IsLocked) return;
                if (verifying) { displayPending = true; return; }
                RebuildWindows();
            }));
        }
        private void RebuildWindows()
        {
            try
            {
                rebuilding = true;
                foreach (PinView view in views.Values) view.Input.Clear();
                foreach (Window window in windows) window.Close();
                windows.Clear(); handles.Clear(); views.Clear();
                CreateWindows(); HideTaskbars();
            }
            catch (Exception ex) { Program.Log(ex); Exit(); }
            finally { rebuilding = false; }
        }
        internal void Exit()
        {
            if (!IsLocked) return;
            IsLocked = false; monitor.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            foreach (PinView view in views.Values) view.Input.Clear();
            foreach (Window window in windows) window.Close();
            windows.Clear(); handles.Clear(); views.Clear();
            unlockWindow = null; unlockEntry = null;
            foreach (IntPtr taskbar in hiddenTaskbars) if (Native.IsWindow(taskbar)) Native.ShowWindow(taskbar, 4);
            hiddenTaskbars.Clear();
            if (shell != null)
            {
                try { ((dynamic)shell).UndoMinimizeAll(); }
                catch (Exception ex) { Program.Log(ex); }
                finally { if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); shell = null; }
            }
            if (Native.IsWindow(originalForeground)) SetForegroundWindow(originalForeground);
            recoverySignal.Reset();
            Program.LogDesktopLock("UNLOCKED");
            if (Unlocked != null) Unlocked();
        }
        public void Dispose() { Exit(); recoverySignal.Dispose(); }
        internal static void RecoverAfterCrash()
        {
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused) {
                var name = new StringBuilder(64); Native.GetClassName(window, name, name.Capacity);
                if (name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd") Native.ShowWindow(window, 8);
                return true;
            }, IntPtr.Zero);
            object recoveryShell = null;
            try { recoveryShell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")); ((dynamic)recoveryShell).UndoMinimizeAll(); }
            catch (Exception ex) { Program.Log(ex); }
            finally { if (recoveryShell != null && Marshal.IsComObject(recoveryShell)) Marshal.FinalReleaseComObject(recoveryShell); }
        }
        private static PasswordBox NewPinInput() { var input = new PasswordBox { Style = PinStyle, MaxLength = 6 }; InputMethod.SetIsInputMethodEnabled(input, false); return input; }
        internal static bool SetUpPin(Window owner, Style style, DesktopPin credential)
        {
            var dialog = new Window { Title = "设置桌面锁 PIN", Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 370, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(24, 27, 33)), Foreground = Brushes.White };
            bool saving = false;
            dialog.Closing += delegate(object sender, CancelEventArgs e) { if (saving) e.Cancel = true; };
            var content = new StackPanel { Margin = new Thickness(24) }; dialog.Content = content;
            content.Children.Add(new TextBlock { Text = "设置独立的 6 位数字 PIN", FontSize = 18, Margin = new Thickness(0, 0, 0, 10) });
            content.Children.Add(new TextBlock { Text = "用于桌面内解锁，与 Windows 登录 PIN 无关。", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
            content.Children.Add(new TextBlock { Text = "设置 PIN", Margin = new Thickness(0, 0, 0, 6) });
            PasswordBox first = NewPinInput(), second = NewPinInput(); content.Children.Add(first);
            content.Children.Add(new TextBlock { Text = "再次输入", Margin = new Thickness(0, 14, 0, 6) }); content.Children.Add(second);
            var status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(242, 166, 166)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; content.Children.Add(status);
            var save = new Button { Style = style, Content = "保存 PIN（不立即上锁）", Height = 38, Margin = new Thickness(0, 12, 0, 0) }; content.Children.Add(save);
            save.Click += async delegate {
                string value = first.Password;
                if (!DesktopPin.IsValid(value)) { status.Text = "PIN 必须是 6 位数字。"; return; }
                if (value != second.Password) { status.Text = "两次输入不一致。"; return; }
                saving = true; save.IsEnabled = false; first.IsEnabled = false; second.IsEnabled = false;
                try { await Task.Run(delegate { credential.Save(value); }); first.Clear(); second.Clear(); saving = false; dialog.DialogResult = true; }
                catch (Exception ex) { saving = false; Program.Log(ex); status.Text = "保存失败：" + ex.Message; save.IsEnabled = true; first.IsEnabled = true; second.IsEnabled = true; }
            };
            dialog.Loaded += delegate { first.Focus(); };
            bool saved = dialog.ShowDialog() == true; first.Clear(); second.Clear(); return saved;
        }
    }
}
