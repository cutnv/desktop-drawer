using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopDrawer
{
    internal static class Native
    {
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr state);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ShellInfo
        {
            public IntPtr Icon; public int Index; public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct ShellFileOperation
        {
            public IntPtr Window;
            public uint Function;
            public IntPtr From, To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)] public bool AnyAborted;
            public IntPtr NameMappings, ProgressTitle;
        }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShellInfo info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern IntPtr SHGetFileInfo(IntPtr path, uint attributes, ref ShellInfo info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHParseDisplayName(string path, IntPtr binding, out IntPtr item, uint requestedAttributes, out uint attributes);
        [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", ExactSpelling = true)] internal static extern int SHFileOperation(ref ShellFileOperation operation);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool MoveFileEx(string existing, string destination, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out Rect value, uint size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, uint size);

        internal static readonly uint ShowMessage = RegisterWindowMessage("DesktopDrawer.Show.v1");
        internal static readonly uint ExitMessage = RegisterWindowMessage("DesktopDrawer.Exit.v1");
        internal static bool HasScreenCoveringWindow(System.Drawing.Rectangle monitor, IntPtr drawer)
        {
            bool found = false;
            EnumWindows(delegate(IntPtr window, IntPtr unused) {
                if (window == drawer || !IsScreenCoveringWindow(window, monitor)) return true;
                found = true;
                return false;
            }, IntPtr.Zero);
            return found;
        }
        internal static bool IsScreenCoveringWindow(IntPtr window, System.Drawing.Rectangle monitor)
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return false;
            long style = GetWindowLongPtr(window, -16).ToInt64();
            long extended = GetWindowLongPtr(window, -20).ToInt64();
            if ((style & 0x40000000L) != 0 || (extended & 0x08000080L) != 0) return false;
            bool maximizedHere = IsZoomed(window) && Forms.Screen.FromHandle(window).Bounds.Equals(monitor);
            Rect bounds;
            if (!GetWindowRect(window, out bounds) || (!maximizedHere && !CoversMonitor(bounds, monitor))) return false;
            var name = new StringBuilder(64); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW") return false;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            Rect frame;
            if (DwmGetWindowAttribute(window, 9, out frame, (uint)Marshal.SizeOf(typeof(Rect))) == 0) bounds = frame;
            return maximizedHere || CoversMonitor(bounds, monitor);
        }
        private static bool CoversMonitor(Rect bounds, System.Drawing.Rectangle monitor)
        {
            return bounds.Left <= monitor.Left + 2 && bounds.Top <= monitor.Top + 2 &&
                bounds.Right >= monitor.Right - 2 && bounds.Bottom >= monitor.Bottom - 2;
        }
        internal static IntPtr DesktopView()
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr unused) {
                var name = new StringBuilder(32); GetClassName(window, name, name.Capacity);
                if (name.ToString() != "Progman" && name.ToString() != "WorkerW") return true;
                IntPtr host = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (host == IntPtr.Zero) return true;
                result = FindWindowEx(host, IntPtr.Zero, "SysListView32", null);
                return result == IntPtr.Zero;
            }, IntPtr.Zero);
            return result;
        }
        internal static void SetDesktopVisible(bool visible)
        {
            IntPtr window = DesktopView();
            if (window != IntPtr.Zero) ShowWindow(window, visible ? 5 : 0);
        }
    }

    internal sealed class SystemUsage
    {
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
        {
            internal uint Length, MemoryLoad;
            internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        private ulong previousIdle, previousKernel, previousUser;
        private bool hasSample;

        internal string Read()
        {
            string cpu = "--", memory = "--";
            ulong idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                if (hasSample && idle >= previousIdle && kernel >= previousKernel && user >= previousUser)
                {
                    double total = (kernel - previousKernel) + (double)(user - previousUser);
                    if (total > 0) cpu = Math.Max(0, Math.Min(100, 100 * (1 - (idle - previousIdle) / total))).ToString("0");
                }
                previousIdle = idle; previousKernel = kernel; previousUser = user; hasSample = true;
            }
            else hasSample = false;
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref status)) memory = status.MemoryLoad.ToString();
            return "CPU  " + cpu + "%    内存  " + memory + "%";
        }
    }

    internal static class Startup
    {
        private const string TaskName = "DesktopDrawer";
        internal static bool IsEnabled()
        {
            try
            {
                dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); service.Connect();
                dynamic task = service.GetFolder(@"\").GetTask(TaskName);
                return task.Enabled && String.Equals((string)task.Definition.Actions.Item(1).Path, Program.Executable, StringComparison.OrdinalIgnoreCase);
            }
            catch (COMException) { return false; }
        }
        internal static void SetEnabled(bool enabled)
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); service.Connect();
            dynamic folder = service.GetFolder(@"\");
            if (enabled)
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                dynamic task = service.NewTask(0);
                task.RegistrationInfo.Description = "登录 Windows 后静默启动桌面抽屉。";
                task.Principal.UserId = user; task.Principal.LogonType = 3; task.Principal.RunLevel = 0;
                task.Settings.Enabled = true; task.Settings.StartWhenAvailable = true;
                task.Settings.DisallowStartIfOnBatteries = false; task.Settings.StopIfGoingOnBatteries = false;
                task.Settings.ExecutionTimeLimit = "PT0S"; task.Settings.MultipleInstances = 2;
                task.Settings.RestartCount = 3; task.Settings.RestartInterval = "PT1M";
                dynamic trigger = task.Triggers.Create(9); trigger.UserId = user; trigger.Delay = "PT3S";
                dynamic action = task.Actions.Create(0);
                action.Path = Program.Executable; action.Arguments = "--startup"; action.WorkingDirectory = Path.GetDirectoryName(Program.Executable);
                folder.RegisterTaskDefinition(TaskName, task, 6, user, null, 3, null);
            }
            else
            {
                try { folder.GetTask(TaskName).Enabled = false; }
                catch (COMException ex) { if (ex.ErrorCode != unchecked((int)0x80070002)) throw; }
            }
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (key != null) { key.DeleteValue(TaskName, false); key.Flush(); }
        }
    }

    internal static class Files
    {
        internal static void WriteRecord(string file, IEnumerable<string> lines)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temporary = file + ".tmp";
            File.WriteAllLines(temporary, lines.ToArray(), Encoding.UTF8);
            // ReplaceFile fails for these encrypted records; allow a copy fallback when the filesystem cannot rename them.
            if (File.Exists(file)) File.Copy(file, file + ".bak", true);
            if (!Native.MoveFileEx(temporary, file, 0x1 | 0x2 | 0x8))
                throw new IOException("无法保存记录：" + file, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
        internal static string Full(string path) { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
        internal static bool Same(string a, string b) { return String.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase); }
        internal static bool Inside(string child, string parent) { return Full(child).StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        internal static string GroupPath(string root, string name)
        {
            name = name.Trim();
            if (name == "待整理") throw new IOException("“待整理”是未分类文件的入口，请换一个分类名称。");
            string device = name.Split('.')[0].ToUpperInvariant();
            if (name.Length == 0 || name.Length > 80 || name == "." || name == ".." || name.EndsWith(".") || name.EndsWith(" ") ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                Enumerable.Range(1, 9).Any(i => device == "COM" + i || device == "LPT" + i))
                throw new IOException("分类名称无效，请使用普通文件夹名称。");
            string destination = Path.Combine(root, name);
            if (!Inside(destination, root)) throw new IOException("分类必须位于分类目录内。");
            return destination;
        }
        internal static string Move(string source, string destinationDirectory)
        {
            source = Full(source);
            destinationDirectory = Full(destinationDirectory);
            bool folder = Directory.Exists(source);
            if (!folder && !File.Exists(source)) throw new IOException("文件已被移动或暂时不可用。");
            if (!Directory.Exists(destinationDirectory)) throw new IOException("目标分类已不存在。");
            if (folder && (Same(source, destinationDirectory) || Inside(destinationDirectory, source)))
                throw new IOException("不能把文件夹放进它自己或子文件夹。");
            string destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
            if (Same(source, destination)) return destination;
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("已存在同名项目，未覆盖：" + Path.GetFileName(source));
            if (!String.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
                throw new IOException("跨磁盘移动请使用文件资源管理器，原文件未改动。");
            // ponytail: same-volume rename is atomic; never implement copy/delete for a cross-volume move.
            if (folder) Directory.Move(source, destination); else File.Move(source, destination);
            return destination;
        }
        internal static string[] DeletionPaths(IEnumerable<string> paths, IEnumerable<string> allowedParents, IEnumerable<string> protectedPaths)
        {
            string[] roots = allowedParents.Select(Full).ToArray();
            string[] protectedRoots = protectedPaths.Where(path => !String.IsNullOrWhiteSpace(path)).Select(Full).ToArray();
            var result = new List<string>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool driveAbsolute = path != null && path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
                bool uncAbsolute = path != null && path.StartsWith(@"\\", StringComparison.Ordinal);
                if (String.IsNullOrWhiteSpace(path) || (!driveAbsolute && !uncAbsolute) || path.IndexOfAny(new[] { '\0', '*', '?' }) >= 0)
                    throw new IOException("只能删除当前分类中选中的文件或文件夹。");
                string full;
                try { full = Full(path); }
                catch (ArgumentException) { throw new IOException("文件路径无效，未执行删除。"); }
                catch (NotSupportedException) { throw new IOException("文件路径不受支持，未执行删除。"); }
                string parent = Path.GetDirectoryName(full);
                if (protectedRoots.Any(root => Same(full, root)) || parent == null || !roots.Any(root => Same(parent, root)))
                    throw new IOException("不能删除桌面、分类总目录或当前分类以外的项目。");
                if (!File.Exists(full) && !Directory.Exists(full)) throw new IOException("项目已被移动或删除：" + Path.GetFileName(full));
                result.Add(full);
            }
            return result.ToArray();
        }
        internal static void Delete(IEnumerable<string> paths, string[] allowedParents, string[] protectedPaths, bool permanent, IntPtr owner)
        {
            string[] selected = DeletionPaths(paths, allowedParents, protectedPaths);
            if (selected.Length == 0) return;
            IntPtr names = Marshal.StringToHGlobalUni(String.Join("\0", selected) + "\0\0");
            try
            {
                // ponytail: let Windows handle recycling, links, permissions and folder deletion.
                var operation = new Native.ShellFileOperation {
                    Window = owner, Function = 3, From = names,
                    Flags = (ushort)(0x0010 | 0x2000 | (permanent ? 0 : 0x0040 | 0x4000))
                };
                int result = Native.SHFileOperation(ref operation);
                if (operation.AnyAborted) throw new OperationCanceledException();
                if (result != 0) throw new IOException("Windows 未能完成删除，请检查项目是否被占用或访问受限。");
            }
            finally { Marshal.FreeHGlobal(names); }
        }
        internal static IEnumerable<string> VisibleItems(string directory)
        {
            if (!Directory.Exists(directory)) return new string[0];
            return Directory.GetFileSystemEntries(directory).Where(path => {
                try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
                catch { return false; }
            }).ToArray();
        }
    }

    internal sealed class Entry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public ImageSource Icon { get; set; }
        public bool Virtual { get; set; }
        public string Category { get; set; }
        public string SearchCategory { get; set; }
        public override string ToString() { return Name; }
    }

    internal sealed class ItemOrder
    {
        private readonly string directory;
        private readonly Dictionary<string, List<string>> cache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        internal ItemOrder(string directory) { this.directory = directory; }
        private string FileFor(string category)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Files.Full(category).ToUpperInvariant()))).Replace("-", "").ToLowerInvariant() + ".order");
        }
        private List<string> Read(string category)
        {
            List<string> saved;
            if (cache.TryGetValue(category, out saved)) return saved;
            string file = FileFor(category);
            foreach (string candidate in new[] { file, file + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    string[] lines = File.ReadAllLines(candidate, Encoding.UTF8);
                    if (lines.Length == 0 || !String.Equals(lines[0], Files.Full(category), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("排列记录的分类路径不匹配。");
                    saved = lines.Skip(1).Where(line => !String.IsNullOrWhiteSpace(line)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    cache[category] = saved;
                    return saved;
                }
                catch (Exception ex) { Program.Log(ex); }
            }
            saved = new List<string>(); cache[category] = saved; return saved;
        }
        internal Entry[] Apply(string category, IEnumerable<Entry> entries, bool keepInputOrder = false)
        {
            var rank = Read(category).Select((path, index) => new { path, index }).ToDictionary(item => item.path, item => item.index, StringComparer.OrdinalIgnoreCase);
            var ordered = entries.OrderBy(item => rank.ContainsKey(item.Path) ? rank[item.Path] : Int32.MaxValue);
            return keepInputOrder ? ordered.ToArray() : ordered.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        internal bool Reorder(string category, IEnumerable<Entry> entries, IEnumerable<string> selected, string anchor, bool after, bool keepInputOrder = false)
        {
            var movingIds = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
            Entry[] original = Apply(category, entries, keepInputOrder);
            Entry[] moving = original.Where(item => movingIds.Contains(item.Path)).ToArray();
            if (moving.Length == 0 || (anchor != null && movingIds.Contains(anchor))) return false;
            var remaining = original.Where(item => !movingIds.Contains(item.Path)).ToList();
            int index = anchor == null ? remaining.Count : remaining.FindIndex(item => String.Equals(item.Path, anchor, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (anchor != null && after) index++;
            remaining.InsertRange(index, moving);
            string[] result = remaining.Select(item => item.Path).ToArray();
            if (result.SequenceEqual(original.Select(item => item.Path), StringComparer.OrdinalIgnoreCase)) return false;
            Write(category, result);
            return true;
        }
        private void Write(string category, IEnumerable<string> paths)
        {
            var order = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // ponytail: Windows paths cannot contain newlines, so one path per line needs no serializer.
            Files.WriteRecord(FileFor(category), new[] { Files.Full(category) }.Concat(order));
            cache[category] = order;
        }
        internal void RenameCategory(string oldPath, string newPath)
        {
            string prefix = Files.Full(oldPath) + Path.DirectorySeparatorChar;
            List<string> saved = Read(oldPath);
            if (saved.Count == 0) return;
            Write(newPath, saved.Select(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? Files.Full(newPath) + path.Substring(prefix.Length - 1) : path));
            cache.Remove(oldPath);
        }
        internal void RenameItem(string category, string oldPath, string newPath)
        {
            List<string> saved = Read(category);
            if (saved.Contains(oldPath, StringComparer.OrdinalIgnoreCase))
                Write(category, saved.Select(path => String.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase) ? newPath : path));
        }
    }

    internal sealed class VirtualCategories
    {
        private readonly string file;
        private Dictionary<string, string> categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal VirtualCategories(string file)
        {
            this.file = file;
            foreach (string candidate in new[] { file, file + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    string[] lines = File.ReadAllLines(candidate, Encoding.UTF8);
                    if (lines.Length == 0 || lines[0] != "DesktopDrawer.VirtualCategories.v1") throw new InvalidDataException("系统图标分类记录无效。");
                    var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines.Skip(1))
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length != 2 || !IsSystemId(parts[0]) || !Path.IsPathRooted(parts[1])) throw new InvalidDataException("系统图标分类记录无效。");
                        loaded.Add(parts[0], Files.Full(parts[1]));
                    }
                    categories = loaded; break;
                }
                catch (Exception ex) { Program.Log(ex); }
            }
        }
        private static bool IsSystemId(string id)
        {
            Guid value;
            return id != null && id.StartsWith("::{", StringComparison.Ordinal) && Guid.TryParse(id.Substring(2), out value);
        }
        internal string CategoryFor(string id, string desktop, string root)
        {
            string category;
            if (categories.TryGetValue(id, out category) && Directory.Exists(category) && String.Equals(Path.GetDirectoryName(category), Files.Full(root), StringComparison.OrdinalIgnoreCase)) return category;
            return desktop;
        }
        internal void Assign(IEnumerable<string> ids, string destination, string desktop, string root)
        {
            destination = Files.Full(destination);
            if (!Directory.Exists(destination) || (!Files.Same(destination, desktop) && !String.Equals(Path.GetDirectoryName(destination), Files.Full(root), StringComparison.OrdinalIgnoreCase)))
                throw new IOException("目标分类已不存在或不属于抽屉。");
            string[] selected = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Any(id => !IsSystemId(id))) throw new IOException("只能记录系统图标的分类归属。");
            var updated = new Dictionary<string, string>(categories, StringComparer.OrdinalIgnoreCase);
            foreach (string id in selected) { if (Files.Same(destination, desktop)) updated.Remove(id); else updated[id] = destination; }
            Save(updated);
        }
        internal void RenameCategory(string oldPath, string newPath)
        {
            if (!categories.Values.Any(path => Files.Same(path, oldPath))) return;
            Save(categories.ToDictionary(item => item.Key, item => Files.Same(item.Value, oldPath) ? Files.Full(newPath) : item.Value, StringComparer.OrdinalIgnoreCase));
        }
        private void Save(Dictionary<string, string> updated)
        {
            // ponytail: Windows folder paths cannot contain tabs or newlines.
            Files.WriteRecord(file, new[] { "DesktopDrawer.VirtualCategories.v1" }.Concat(updated.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(item => item.Key + "\t" + item.Value)));
            categories = updated;
        }
    }

    internal sealed class InsertionMarker : Adorner
    {
        internal Rect Bounds;
        internal bool After;
        internal InsertionMarker(UIElement element) : base(element) { IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext drawing)
        {
            if (Bounds.IsEmpty) return;
            double x = After ? Bounds.Right : Bounds.Left;
            drawing.PushClip(new RectangleGeometry(new Rect(RenderSize)));
            drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(230, 235, 242)), 3), new Point(x, Bounds.Top + 4), new Point(x, Bounds.Bottom - 4));
            drawing.Pop();
        }
    }

    internal sealed class Drawer : Window
    {
        internal const string TitleText = "桌面抽屉";
        private readonly string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private readonly string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        private readonly string groupsRoot;
        private string current;
        private string lastShownCategory;
        private readonly Dictionary<string, ImageSource> icons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer timer;
        private readonly DispatcherTimer usageTimer;
        private readonly DesktopPin desktopPin = new DesktopPin(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDrawer", "desktop-lock.pin"));
        private readonly DesktopLock desktopLock;
        private bool desktopLockStarting;
        private readonly SystemUsage systemUsage = new SystemUsage();
        private readonly TextBlock usageText;
        private readonly ListBox list;
        private readonly StackPanel tabs;
        private readonly ScrollViewer tabScroll;
        private readonly Style roundedButtonStyle, roundedToggleStyle, inputStyle;
        private readonly TextBox search;
        private readonly ToggleButton pinned;
        private readonly Border body;
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly bool originalIconsVisible;
        private readonly IntPtr handle;
        private readonly uint drawerProcessId = (uint)Process.GetCurrentProcess().Id;
        private DateTime edgeSince = DateTime.MaxValue, lastInside = DateTime.UtcNow;
        private string edgeMonitor = "";
        private bool dirty = true, interacting, showDesktop, closing, hiding, allowHoverReopen, automaticReveal, deleting;
        private int slideVersion;
        private int lastIconCheck;
        private Point dragStart;
        private const string InternalDragFormat = "DesktopDrawer.InternalItems.v1";
        private const string CategoryDragFormat = "DesktopDrawer.Categories.v1";
        private readonly ItemOrder itemOrder = new ItemOrder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDrawer", "item-order"));
        private readonly VirtualCategories virtualCategories = new VirtualCategories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDrawer", "system-icons.groups"));
        private Entry[] allEntries = new Entry[0], draggedEntries;
        private Entry[] searchEntries;
        private bool IsSearching { get { return search.Text.Trim().Length > 0; } }
        private string dragCategory, dragToken;
        private string draggedTab;
        private Button tabDragSource;
        private Point tabDragStart;
        private InsertionMarker tabInsertionMarker;
        private InsertionMarker insertionMarker;
        private ScrollViewer listScroll;
        private DateTime lastDragScroll;
        private readonly List<Entry> virtualEntries = new List<Entry>();
        private ShellMenu activeShellMenu;
        private bool shellBusy;

        internal Drawer()
        {
            groupsRoot = Path.Combine(desktop, "桌面分类");
            Directory.CreateDirectory(groupsRoot);
            current = desktop;
            IntPtr desktopView = Native.DesktopView();
            originalIconsVisible = desktopView == IntPtr.Zero || Native.IsWindowVisible(desktopView);
            Title = TitleText; Width = 1030; Height = 510; MinWidth = 360;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            Foreground = new SolidColorBrush(Color.FromRgb(237, 241, 247));
            var buttonBase = new Style(typeof(ButtonBase));
            buttonBase.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(43, 48, 58))));
            buttonBase.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
            buttonBase.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(71, 79, 93))));
            buttonBase.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            buttonBase.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34.0));
            buttonBase.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            buttonBase.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            buttonBase.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            buttonBase.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ButtonBase}'>
                <Border x:Name='Chrome' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' SnapsToDevicePixels='True'>
                    <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.9'/></Trigger>
                    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.75'/></Trigger>
                    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#A9B4C5'/></Trigger>
                    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.45'/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>")));
            roundedButtonStyle = new Style(typeof(Button), buttonBase);
            roundedToggleStyle = new Style(typeof(ToggleButton), buttonBase);
            var checkedStyle = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 243, 248))));
            checkedStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(28, 33, 42))));
            roundedToggleStyle.Triggers.Add(checkedStyle);
            inputStyle = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type TextBox}'>
                <Setter Property='Background' Value='#242932'/><Setter Property='Foreground' Value='#EDF1F7'/><Setter Property='CaretBrush' Value='#EDF1F7'/>
                <Setter Property='BorderBrush' Value='#474F5D'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Height' Value='34'/>
                <Setter Property='SelectionBrush' Value='#59677F'/><Setter Property='VerticalContentAlignment' Value='Center'/>
                <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TextBox}'>
                    <Border x:Name='Input' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
                        <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' Focusable='False'/>
                    </Border>
                    <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Input' Property='BorderBrush' Value='#A9B4C5'/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>");

            desktopLock = new DesktopLock(roundedButtonStyle, Dispatcher, desktopPin);
            desktopLock.Unlocked += delegate { lastInside = DateTime.UtcNow; edgeSince = DateTime.MaxValue; };
            var layout = new DockPanel { LastChildFill = true, Margin = new Thickness(22, 18, 22, 14) };
            body = new Border { Background = new SolidColorBrush(Color.FromArgb(224, 24, 27, 33)), BorderBrush = new SolidColorBrush(Color.FromArgb(130, 85, 94, 110)), BorderThickness = new Thickness(0, 0, 0, 1), Child = layout, RenderTransform = slide };
            Content = new Border { Background = Brushes.Transparent, Child = body, ClipToBounds = true };
            var header = new DockPanel { Height = 34, Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
            pinned = new ToggleButton { Style = roundedToggleStyle, Content = "保持展开", Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(8, 0, 0, 0) };
            controls.Children.Add(pinned);
            var add = Button("＋ 新建分类", delegate { CreateGroup(); }); add.AutomationName("新建分类"); controls.Children.Add(add);
            var lockButton = Button("桌面锁", delegate { BeginDesktopLock(); }); lockButton.AutomationName("桌面锁"); lockButton.ToolTip = "手动开启桌面锁，使用独立的 6 位 PIN 在桌面内解锁"; controls.Children.Add(lockButton);
            search = new TextBox { Style = inputStyle, Width = 175, TextAlignment = TextAlignment.Center, Padding = new Thickness(10, 0, 10, 0), ToolTip = "全局搜索：全部分类与待整理", Margin = new Thickness(12, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(search, "查找文件");
            search.TextChanged += delegate { dirty = true; }; controls.Children.Insert(0, search);
            tabs = new StackPanel { Orientation = Orientation.Horizontal };
            tabScroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, AllowDrop = true };
            tabScroll.SizeChanged += delegate { ScrollSelectedTabIntoView(); };
            tabScroll.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { if (tabScroll.ScrollableWidth > 0) { tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; } };
            tabScroll.PreviewDragOver += AcceptTabDrag;
            tabScroll.PreviewDragLeave += delegate(object sender, DragEventArgs e) { if (!new Rect(tabScroll.RenderSize).Contains(e.GetPosition(tabScroll))) ClearTabInsertionMarker(); };
            tabScroll.PreviewDrop += DropTab;
            var tabDecorator = new AdornerDecorator { Child = tabScroll };
            header.Children.Add(tabDecorator);
            list = new ListBox { Foreground = Foreground, FocusVisualStyle = null, Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, AllowDrop = true, Padding = new Thickness(0) };
            list.ItemContainerStyle = (Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBoxItem}'>
                <Setter Property='Padding' Value='4,2'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/>
                <Setter Property='Foreground' Value='#EDF1F7'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
                <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ListBoxItem}'>
                    <Border x:Name='Tile' CornerRadius='10' Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
                        <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Tile' Property='Background' Value='#16FFFFFF'/></Trigger>
                        <Trigger Property='IsSelected' Value='True'><Setter TargetName='Tile' Property='Background' Value='#26FFFFFF'/><Setter TargetName='Tile' Property='BorderBrush' Value='#60FFFFFF'/></Trigger>
                        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Tile' Property='BorderBrush' Value='#A0FFFFFF'/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate></Setter.Value></Setter>
            </Style>");
            list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel /></ItemsPanelTemplate>");
            list.ItemTemplate = (DataTemplate)XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <StackPanel Width='98' MinHeight='100' Margin='3,5' ToolTip='{Binding Path}'>
                    <Image Source='{Binding Icon}' Width='40' Height='40' Margin='0,7,0,9'/>
                    <TextBlock Text='{Binding Name}' TextAlignment='Center' TextWrapping='Wrap' TextTrimming='CharacterEllipsis' MaxHeight='37'/>
                    <TextBlock x:Name='CategoryLabel' Text='{Binding SearchCategory}' Foreground='#AAB4C3' FontSize='11' TextAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,4,0,0'/>
                </StackPanel>
                <DataTemplate.Triggers><DataTrigger Binding='{Binding SearchCategory}' Value='{x:Null}'><Setter TargetName='CategoryLabel' Property='Visibility' Value='Collapsed'/></DataTrigger></DataTemplate.Triggers>
            </DataTemplate>");
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            list.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) {
                var container = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (e.ChangedButton == MouseButton.Left && container != null && container.DataContext is Entry) Open((Entry)container.DataContext);
            };
            list.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { dragStart = e.GetPosition(list); };
            list.MouseMove += BeginDrag;
            list.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                var container = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (container != null && !container.IsSelected) { list.SelectedItems.Clear(); container.IsSelected = true; }
                if (container == null) list.SelectedItems.Clear();
                list.Focus();
            };
            list.ContextMenuOpening += BuildItemMenu;
            list.ContextMenu = new ContextMenu();
            list.DragOver += delegate(object sender, DragEventArgs e) { AcceptDrag(e, current, true); };
            list.DragLeave += delegate(object sender, DragEventArgs e) { if (!new Rect(list.RenderSize).Contains(e.GetPosition(list))) ClearInsertionMarker(); };
            list.Drop += delegate(object sender, DragEventArgs e) { DropFiles(e, current, true); };
            list.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && !shellBusy && !interacting && !deleting)
                { e.Handled = true; foreach (Entry item in list.SelectedItems.Cast<Entry>().ToArray()) Open(item); }
            };
            usageText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), ToolTip = "整台电脑的 CPU 与物理内存占用，每秒刷新" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(usageText, "SystemUsage");
            DockPanel.SetDock(usageText, Dock.Bottom); layout.Children.Add(usageText);
            layout.Children.Add(new AdornerDecorator { Child = list });
            PreviewKeyDown += DesktopKeyDown;
            handle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource.FromHwnd(handle).AddHook(WindowMessage);
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = TitleText, Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开抽屉", null, delegate { Reveal(true); });
            menu.Items.Add("桌面锁", null, delegate { BeginDesktopLock(); });
            menu.Items.Add("打开桌面文件夹", null, delegate { Start(desktop); });
            var desktopToggle = new Forms.ToolStripMenuItem("暂时显示桌面图标") { CheckOnClick = true };
            desktopToggle.CheckedChanged += delegate { showDesktop = desktopToggle.Checked; Native.SetDesktopVisible(showDesktop); };
            menu.Items.Add(desktopToggle);
            var startup = new Forms.ToolStripMenuItem("开机启动") { CheckOnClick = true, Checked = IsStartupEnabled() };
            startup.CheckedChanged += delegate { SetStartup(startup.Checked); }; menu.Items.Add(startup);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("恢复桌面图标并退出", null, delegate { Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Reveal(true); };
            foreach (string path in new[] { desktop, common, groupsRoot }.Distinct(StringComparer.OrdinalIgnoreCase)) Watch(path);
            LoadVirtualEntries();
            RefreshContent();
            StartWatchdog();
            Native.SetDesktopVisible(false);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += Tick; timer.Start();
            usageText.Text = systemUsage.Read();
            usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            usageTimer.Tick += delegate { usageText.Text = systemUsage.Read(); }; usageTimer.Start();
            Closed += delegate { Cleanup(); };
            AppDomain.CurrentDomain.ProcessExit += delegate { Native.SetDesktopVisible(originalIconsVisible); };
        }

        private Button Button(string text, RoutedEventHandler click)
        {
            var button = new Button { Style = roundedButtonStyle, Content = text, Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
            button.Click += click; return button;
        }
        private void BeginDesktopLock()
        {
            if (desktopLockStarting || desktopLock.IsLocked || shellBusy || deleting || interacting || closing) return;
            try
            {
                if (!desktopPin.IsConfigured)
                {
                    interacting = true;
                    try { DesktopLock.SetUpPin(this, roundedButtonStyle, desktopPin); }
                    finally { interacting = false; lastInside = DateTime.UtcNow; }
                    return;
                }
                desktopPin.CheckReadable();
                desktopLockStarting = true; HideDrawer();
                var launch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                launch.Tick += delegate {
                    launch.Stop();
                    try { if (!closing) desktopLock.Enter(); }
                    catch (Exception ex) { Program.Log(ex); Alert("无法开启桌面锁：" + ex.Message); }
                    finally { desktopLockStarting = false; }
                };
                launch.Start();
            }
            catch (Exception ex) { desktopLockStarting = false; Program.Log(ex); Alert("无法开启桌面锁：" + ex.Message); }
        }
        private void DesktopKeyDown(object sender, KeyEventArgs e)
        {
            lastInside = DateTime.UtcNow;
            if (shellBusy || deleting || interacting) return;
            ModifierKeys modifiers = Keyboard.Modifiers;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (e.Key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            { e.Handled = true; if (!e.IsRepeat) CreateFolder(); return; }
            if (e.Key == Key.F5 && modifiers == ModifierKeys.None)
            { e.Handled = true; if (!e.IsRepeat) ReloadContent(); return; }
            if ((e.Key == Key.F || e.Key == Key.E) && modifiers == ModifierKeys.Control)
            { e.Handled = true; search.Focus(); search.SelectAll(); return; }
            if (e.Key == Key.Escape) { e.Handled = true; HideDrawer(); return; }
            if (FindParent<TextBoxBase>(Keyboard.FocusedElement as DependencyObject) != null) return;
            if (e.Key == Key.F2 && modifiers == ModifierKeys.None) { e.Handled = true; if (!e.IsRepeat) RenameSelected(); }
            else if (e.Key == Key.Delete && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
            { e.Handled = true; if (!e.IsRepeat) DeleteSelected(modifiers == ModifierKeys.Shift); }
            else if (modifiers == ModifierKeys.Control && e.Key == Key.A) { e.Handled = true; list.Focus(); list.SelectAll(); }
            else if (modifiers == ModifierKeys.Control && (e.Key == Key.C || e.Key == Key.X || e.Key == Key.V))
            { e.Handled = true; if (!e.IsRepeat) ShellVerb(e.Key == Key.C ? "copy" : e.Key == Key.X ? "cut" : "paste", e.Key == Key.V); }
            else if (key == Key.Enter && modifiers == ModifierKeys.Alt)
            { e.Handled = true; if (!e.IsRepeat) ShellVerb("properties", false); }
        }
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null) { if (child is T) return (T)child; child = VisualTreeHelper.GetParent(child); } return null;
        }
        private void Watch(string path)
        {
            if (!Directory.Exists(path)) return;
            var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = Files.Same(path, groupsRoot), NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes };
            FileSystemEventHandler changed = delegate { Dispatcher.BeginInvoke(new Action(InvalidateContent)); };
            watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
            watcher.Renamed += delegate { Dispatcher.BeginInvoke(new Action(InvalidateContent)); };
            watcher.Error += delegate { Dispatcher.BeginInvoke(new Action(InvalidateContent)); };
            watcher.EnableRaisingEvents = true; watchers.Add(watcher);
        }
        private string[] Groups() { return Directory.Exists(groupsRoot) ? Directory.GetDirectories(groupsRoot).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray() : new string[0]; }
        private Entry[] CategoryEntries()
        {
            return itemOrder.Apply(groupsRoot, new[] { new Entry { Name = "待整理", Path = desktop } }
                .Concat(Groups().Select(path => new Entry { Name = Path.GetFileName(path), Path = path })), true);
        }
        private void RefreshTabs()
        {
            tabs.Children.Clear();
            foreach (Entry category in CategoryEntries()) AddTab(category.Name, category.Path);
            if (!String.Equals(lastShownCategory, current, StringComparison.OrdinalIgnoreCase)) ScrollSelectedTabIntoView();
            lastShownCategory = current;
        }
        private void ScrollSelectedTabIntoView()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate {
                if (closing) return;
                Button selected = tabs.Children.OfType<Button>().FirstOrDefault(button => Files.Same((string)button.Tag, current));
                if (selected != null) selected.BringIntoView();
            }));
        }
        private void InvalidateContent() { searchEntries = null; dirty = true; }
        private void ReloadContent() { InvalidateContent(); RefreshContent(); }
        private string CategoryOf(Entry item) { return item.Category ?? current; }
        private string CategoryName(string category) { return Files.Same(category, desktop) ? "待整理" : Path.GetFileName(category); }
        private IEnumerable<Entry> ReadCategory(string category, bool forSearch)
        {
            var entries = new List<Entry>();
            IEnumerable<string> paths = Files.Same(category, desktop) ? Files.VisibleItems(desktop).Concat(Files.VisibleItems(common)) : Files.VisibleItems(category);
            foreach (string path in paths)
            {
                if (Files.Same(path, groupsRoot)) continue;
                string name = Path.GetFileName(path);
                if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
                entries.Add(new Entry { Name = name, Path = path, Icon = forSearch ? null : IconFor(path), Category = category, SearchCategory = forSearch ? CategoryName(category) : null });
            }
            entries.AddRange(virtualEntries.Where(item => Files.Same(virtualCategories.CategoryFor(item.Path, desktop, groupsRoot), category))
                .Select(item => new Entry { Name = item.Name, Path = item.Path, Icon = item.Icon, Virtual = true, Category = category, SearchCategory = forSearch ? CategoryName(category) : null }));
            return itemOrder.Apply(category, entries);
        }
        private void RefreshContent()
        {
            var selection = new HashSet<string>(list.SelectedItems.Cast<Entry>().Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
            dirty = false;
            if (!Directory.Exists(current)) current = desktop;
            RefreshTabs();
            string query = search.Text.Trim();
            if (query.Length == 0) allEntries = ReadCategory(current, false).ToArray();
            else
            {
                if (searchEntries == null)
                {
                    var indexed = new List<Entry>();
                    foreach (Entry category in CategoryEntries())
                    {
                        try { indexed.AddRange(ReadCategory(category.Path, true)); }
                        catch (IOException ex) { Program.Log(ex); }
                        catch (UnauthorizedAccessException ex) { Program.Log(ex); }
                    }
                    // ponytail: cache the drawer's visible entries; filesystem watchers invalidate this snapshot.
                    searchEntries = indexed.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
                }
                allEntries = searchEntries;
            }
            Entry[] visible = allEntries.Where(item => query.Length == 0 || item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray();
            foreach (Entry item in visible) if (item.Icon == null) item.Icon = IconFor(item.Path);
            list.ItemsSource = visible;
            foreach (Entry item in list.Items) if (selection.Contains(item.Path)) list.SelectedItems.Add(item);
        }
        private void AddTab(string name, string destination)
        {
            bool selected = Files.Same(current, destination);
            var button = new Button { Style = roundedButtonStyle, Content = name, Tag = destination, ToolTip = "拖动调整分类顺序", Padding = new Thickness(15, 5, 15, 5), Margin = new Thickness(0, 0, 8, 0), AllowDrop = true, Cursor = Cursors.Hand,
                Background = new SolidColorBrush(selected ? Color.FromRgb(245, 247, 250) : Color.FromRgb(43, 48, 58)), Foreground = selected ? new SolidColorBrush(Color.FromRgb(28, 33, 42)) : Foreground,
                BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(245, 247, 250) : Color.FromRgb(71, 79, 93)) };
            button.Click += delegate { current = destination; search.Clear(); RefreshContent(); };
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { tabDragSource = button; tabDragStart = e.GetPosition(button); };
            button.PreviewMouseLeftButtonUp += delegate { tabDragSource = null; };
            button.PreviewMouseMove += BeginTabDrag;
            button.DragOver += delegate(object sender, DragEventArgs e) { AcceptDrag(e, destination, false); };
            button.Drop += delegate(object sender, DragEventArgs e) { DropFiles(e, destination, false); };
            var menu = new ContextMenu();
            menu.Items.Add(Menu("在文件资源管理器中打开", delegate { Start(destination); }));
            if (!Files.Same(destination, desktop)) menu.Items.Add(Menu("重命名分类", delegate { RenameGroup(destination); }));
            HoldMenu(menu); button.ContextMenu = menu; tabs.Children.Add(button);
        }
        private bool IsCategoryDrag(IDataObject data)
        {
            return draggedTab != null && data.GetDataPresent(CategoryDragFormat) && String.Equals(data.GetData(CategoryDragFormat) as string, dragToken, StringComparison.Ordinal);
        }
        private void BeginTabDrag(object sender, MouseEventArgs e)
        {
            var button = (Button)sender;
            if (e.LeftButton != MouseButtonState.Pressed || interacting || tabDragSource != button) return;
            Point point = e.GetPosition(button);
            if (Math.Abs(point.X - tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            e.Handled = true;
            draggedTab = (string)button.Tag; dragToken = Guid.NewGuid().ToString("N");
            var data = new DataObject(); data.SetData(CategoryDragFormat, dragToken);
            interacting = true; button.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(button, data, DragDropEffects.Move); }
            finally
            {
                ClearTabInsertionMarker(); draggedTab = dragToken = null; tabDragSource = null;
                interacting = false; lastInside = DateTime.UtcNow;
                RefreshTabs();
            }
        }
        private void GetTabDropPosition(DragEventArgs e, out string anchor, out bool after, out Rect marker)
        {
            Point point = e.GetPosition(tabs);
            Button target = tabs.Children.OfType<Button>().Last();
            foreach (Button button in tabs.Children)
            {
                target = button;
                if (point.X <= button.TranslatePoint(new Point(button.ActualWidth + 4, 0), tabs).X) break;
            }
            anchor = (string)target.Tag;
            after = point.X >= target.TranslatePoint(new Point(target.ActualWidth / 2, 0), tabs).X;
            marker = new Rect(target.TranslatePoint(new Point(), tabScroll), target.RenderSize);
        }
        private void AcceptTabDrag(object sender, DragEventArgs e)
        {
            if (!IsCategoryDrag(e.Data)) return;
            e.Handled = true; lastInside = DateTime.UtcNow;
            if ((DateTime.UtcNow - lastDragScroll).TotalMilliseconds >= 100)
            {
                double x = e.GetPosition(tabScroll).X;
                if (x < 28) tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset - 36);
                else if (x > tabScroll.ActualWidth - 28) tabScroll.ScrollToHorizontalOffset(tabScroll.HorizontalOffset + 36);
                lastDragScroll = DateTime.UtcNow;
            }
            string anchor; bool after; Rect marker;
            GetTabDropPosition(e, out anchor, out after, out marker);
            e.Effects = Files.Same(anchor, draggedTab) ? DragDropEffects.None : DragDropEffects.Move;
            if (e.Effects == DragDropEffects.None) { ClearTabInsertionMarker(); return; }
            var layer = AdornerLayer.GetAdornerLayer(tabScroll);
            if (layer == null) return;
            if (tabInsertionMarker == null) { tabInsertionMarker = new InsertionMarker(tabScroll); layer.Add(tabInsertionMarker); }
            tabInsertionMarker.Bounds = marker; tabInsertionMarker.After = after; tabInsertionMarker.InvalidateVisual();
        }
        private void DropTab(object sender, DragEventArgs e)
        {
            if (!IsCategoryDrag(e.Data)) return;
            e.Handled = true; e.Effects = DragDropEffects.None;
            try
            {
                string anchor; bool after; Rect marker;
                GetTabDropPosition(e, out anchor, out after, out marker);
                if (itemOrder.Reorder(groupsRoot, CategoryEntries(), new[] { draggedTab }, anchor, after, true)) e.Effects = DragDropEffects.Move;
            }
            catch (Exception ex) { Program.Log(ex); Alert("无法保存分类顺序：" + ex.Message); }
            finally { ClearTabInsertionMarker(); }
        }
        private void ClearTabInsertionMarker()
        {
            if (tabInsertionMarker == null) return;
            var layer = AdornerLayer.GetAdornerLayer(tabScroll); if (layer != null) layer.Remove(tabInsertionMarker);
            tabInsertionMarker = null;
        }
        private static MenuItem Menu(string text, RoutedEventHandler action) { var item = new MenuItem { Header = text }; item.Click += action; return item; }
        private void HoldMenu(ContextMenu menu) { menu.Opened += delegate { interacting = true; }; menu.Closed += delegate { interacting = false; lastInside = DateTime.UtcNow; }; }
        private void BuildItemMenu(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
            if (shellBusy || interacting || deleting) return;
            Native.Point point; Native.GetCursorPos(out point);
            if (e.CursorLeft < 0)
            {
                var item = list.SelectedItem == null ? null : list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as FrameworkElement;
                Point position = (item ?? list).PointToScreen(new Point(16, 20)); point.X = (int)position.X; point.Y = (int)position.Y;
            }
            Native.Point menuPoint = point;
            Dispatcher.BeginInvoke(new Action(delegate { ShowItemMenu(menuPoint); }));
        }
        private void ShowItemMenu(Native.Point point)
        {
            if (shellBusy || interacting || deleting) return;
            Entry[] selected = list.SelectedItems.Cast<Entry>().ToArray();
            if (NeedsCommonMenu(selected))
            {
                var commonMenu = new ContextMenu(); HoldMenu(commonMenu);
                commonMenu.Items.Add(Menu("打开", delegate { foreach (Entry item in selected) Open(item); }));
                if (selected.All(item => !item.Virtual))
                {
                    commonMenu.Items.Add(Menu("剪切", delegate { ShellVerb("cut", false); }));
                    commonMenu.Items.Add(Menu("复制", delegate { ShellVerb("copy", false); }));
                    commonMenu.Items.Add(new Separator());
                    commonMenu.Items.Add(Menu("删除（移到回收站）", delegate { DeleteSelected(false); }));
                    commonMenu.Items.Add(Menu("永久删除...", delegate { DeleteSelected(true); }));
                    commonMenu.Items.Add(Menu("属性", delegate { ShellVerb("properties", false); }));
                }
                var move = new MenuItem { Header = "移动到分类" };
                foreach (Entry category in MoveTargets(selected))
                {
                    string destination = category.Path;
                    move.Items.Add(Menu(category.Name, delegate { MoveEntries(selected.Select(item => item.Path).ToArray(), destination); }));
                }
                move.IsEnabled = move.Items.Count > 0; commonMenu.Items.Add(move);
                Point relative = list.PointFromScreen(new Point(point.X, point.Y));
                commonMenu.PlacementTarget = list; commonMenu.Placement = PlacementMode.RelativePoint;
                commonMenu.HorizontalOffset = relative.X; commonMenu.VerticalOffset = relative.Y; commonMenu.IsOpen = true;
                return;
            }
            shellBusy = true;
            var actions = new Dictionary<uint, Action>();
            try
            {
                using (var menu = new ShellMenu(handle, current, selected.Select(item => item.Path).ToArray()))
                {
                    activeShellMenu = menu; menu.Separator();
                    uint next = 0x7000;
                    if (selected.Length == 0)
                    {
                        menu.Add(++next, "新建文件夹\tCtrl+Shift+N"); actions[next] = CreateFolder;
                        menu.Add(++next, IsSearching ? "粘贴到「" + CategoryName(current) + "」\tCtrl+V" : "粘贴\tCtrl+V", ShellClipboard.CanPaste()); actions[next] = delegate { ShellClipboard.Paste(handle, current); };
                        menu.Add(++next, "刷新\tF5"); actions[next] = ReloadContent;
                    }
                    if (selected.Length > 0)
                    {
                        var destinations = new List<KeyValuePair<uint, string>>();
                        foreach (Entry category in MoveTargets(selected))
                        {
                            string target = category.Path; uint id = ++next;
                            destinations.Add(new KeyValuePair<uint, string>(id, category.Name));
                            actions[id] = delegate { MoveEntries(selected.Select(item => item.Path).ToArray(), target); };
                        }
                        if (destinations.Count > 0) menu.AddSubmenu("移动到分类", destinations);
                        if (selected.Any(item => !item.Virtual)) { menu.Add(++next, "打开所在文件夹"); actions[next] = delegate { RevealInExplorer(selected.First(item => !item.Virtual).Path); }; }
                        if (selected.All(item => !item.Virtual)) { menu.Add(++next, "永久删除...\tShift+Delete"); actions[next] = delegate { DeleteSelected(true); }; }
                    }
                    menu.Add(++next, "新建分类"); actions[next] = CreateGroup;
                    uint command = menu.Show(point);
                    if (command == 0) return;
                    Action action;
                    if (actions.TryGetValue(command, out action)) action();
                    else if (String.Equals(menu.Verb(command), "rename", StringComparison.OrdinalIgnoreCase)) RenameSelected();
                    else if (String.Equals(menu.Verb(command), "delete", StringComparison.OrdinalIgnoreCase)) DeleteSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                    else menu.Invoke(command, point);
                }
            }
            catch (Exception ex) { Program.Log(ex); Alert("无法执行系统菜单操作：" + ex.Message); }
            finally { activeShellMenu = null; shellBusy = false; InvalidateContent(); lastInside = DateTime.UtcNow; }
        }
        private IEnumerable<Entry> MoveTargets(Entry[] selected)
        {
            return CategoryEntries().Where(category => selected.Any(item => !Files.Same(CategoryOf(item), category.Path)));
        }
        private bool NeedsCommonMenu(Entry[] selected)
        {
            if (selected.Any(item => item.Virtual)) return selected.Any(item => !item.Virtual);
            string[] parents = selected.Select(item => Path.GetDirectoryName(item.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return parents.Length > 1 && !parents.All(parent => Files.Same(parent, desktop) || Files.Same(parent, common));
        }
        private void ShellVerb(string verb, bool background)
        {
            Entry[] selected = list.SelectedItems.Cast<Entry>().ToArray();
            if (!background && selected.Length == 0) return;
            shellBusy = true;
            try
            {
                if (background && verb == "paste") { ShellClipboard.Paste(handle, current); return; }
                if (NeedsCommonMenu(selected) && selected.All(item => !item.Virtual))
                {
                    string[] paths = selected.Select(item => item.Path).ToArray();
                    if (verb == "copy" || verb == "cut") ShellClipboard.Copy(paths, verb == "cut");
                    else if (verb == "properties") ShellClipboard.Properties(paths);
                    return;
                }
                using (var menu = new ShellMenu(handle, current, background ? new string[0] : selected.Select(item => item.Path).ToArray()))
                {
                    activeShellMenu = menu;
                    uint command = menu.FindCommand(verb);
                    if (command != 0) { Native.Point point; Native.GetCursorPos(out point); menu.Invoke(command, point); }
                }
            }
            catch (Exception ex) { Program.Log(ex); Alert("Windows 未能完成操作：" + ex.Message); }
            finally { activeShellMenu = null; shellBusy = false; InvalidateContent(); lastInside = DateTime.UtcNow; }
        }
        private void RenameSelected()
        {
            Entry[] selected = list.SelectedItems.Cast<Entry>().ToArray();
            if (selected.Length == 0) return;
            if (selected.Length != 1) { Alert("请选择一个项目后重命名。"); return; }
            Entry item = selected[0]; bool previousBusy = shellBusy; shellBusy = true;
            try
            {
                string initial = item.Virtual ? item.Name : Path.GetFileName(item.Path);
                string name = AskName("重命名", initial, !item.Virtual && !Directory.Exists(item.Path));
                if (name == null || String.Equals(name, initial, StringComparison.Ordinal)) return;
                if (!item.Virtual && File.Exists(item.Path) && !String.Equals(Path.GetExtension(initial), Path.GetExtension(name), StringComparison.OrdinalIgnoreCase) &&
                    MessageBox.Show(this, "更改扩展名可能导致文件无法打开。是否继续？", "更改扩展名", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                string target = ShellMenu.Rename(handle, item.Path, name);
                try { itemOrder.RenameItem(CategoryOf(item), item.Path, target); }
                catch (Exception ex) { Program.Log(ex); Alert("名称已更改，但排列位置未保存：" + ex.Message); }
                if (item.Virtual) { virtualEntries.Clear(); LoadVirtualEntries(); }
                icons.Remove(item.Path); ReloadContent();
                Entry renamed = allEntries.FirstOrDefault(entry => String.Equals(entry.Path, target, StringComparison.OrdinalIgnoreCase));
                if (renamed != null) { list.SelectedItem = renamed; list.ScrollIntoView(renamed); }
            }
            catch (Exception ex) { Program.Log(ex); Alert("无法重命名：" + ex.Message); }
            finally { shellBusy = previousBusy; lastInside = DateTime.UtcNow; if (IsVisible) list.Focus(); }
        }
        private bool IsInternalDrag(IDataObject data)
        {
            return draggedEntries != null && data.GetDataPresent(InternalDragFormat) && String.Equals(data.GetData(InternalDragFormat) as string, dragToken, StringComparison.Ordinal);
        }
        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) return (T)child;
                T found = FindChild<T>(child); if (found != null) return found;
            }
            return null;
        }
        private void ScrollWhileDragging(DragEventArgs e)
        {
            if ((DateTime.UtcNow - lastDragScroll).TotalMilliseconds < 100) return;
            Point point = e.GetPosition(list);
            if (listScroll == null) listScroll = FindChild<ScrollViewer>(list);
            if (listScroll == null) return;
            if (point.Y < 28) listScroll.ScrollToVerticalOffset(listScroll.VerticalOffset - 36);
            else if (point.Y > list.ActualHeight - 28) listScroll.ScrollToVerticalOffset(listScroll.VerticalOffset + 36);
            else return;
            lastDragScroll = DateTime.UtcNow;
        }
        private void GetDropPosition(DragEventArgs e, out string anchor, out bool after, out Rect marker)
        {
            var container = ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) as ListBoxItem;
            anchor = null; after = true; marker = Rect.Empty;
            if (container != null && container.DataContext is Entry)
            {
                anchor = ((Entry)container.DataContext).Path;
                after = e.GetPosition(container).X >= container.ActualWidth / 2;
            }
            else if (list.Items.Count > 0) container = list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) as ListBoxItem;
            if (container != null) marker = new Rect(container.TranslatePoint(new Point(), list), container.RenderSize);
        }
        private void ClearInsertionMarker()
        {
            if (insertionMarker == null) return;
            var layer = AdornerLayer.GetAdornerLayer(list); if (layer != null) layer.Remove(insertionMarker);
            insertionMarker = null;
        }
        private void AcceptDrag(DragEventArgs e, string target, bool inList)
        {
            bool local = IsInternalDrag(e.Data);
            if (IsSearching && inList)
            {
                e.Effects = DragDropEffects.None; e.Handled = true; ClearInsertionMarker(); return;
            }
            if (local && dragCategory != null && Files.Same(dragCategory, target))
            {
                e.Effects = DragDropEffects.None;
                if (inList)
                {
                    ScrollWhileDragging(e);
                    string anchor; bool after; Rect marker;
                    GetDropPosition(e, out anchor, out after, out marker);
                    if (!draggedEntries.Any(item => String.Equals(item.Path, anchor, StringComparison.OrdinalIgnoreCase)))
                    {
                        e.Effects = DragDropEffects.Move;
                        var layer = AdornerLayer.GetAdornerLayer(list);
                        if (layer != null && !marker.IsEmpty)
                        {
                            if (insertionMarker == null) { insertionMarker = new InsertionMarker(list); layer.Add(insertionMarker); }
                            insertionMarker.Bounds = marker; insertionMarker.After = after; insertionMarker.InvalidateVisual();
                        }
                    }
                }
                if (e.Effects == DragDropEffects.None) ClearInsertionMarker();
            }
            else
            {
                ClearInsertionMarker();
                e.Effects = local || e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
            }
            e.Handled = true; lastInside = DateTime.UtcNow;
        }
        private void DropFiles(DragEventArgs e, string target, bool inList)
        {
            e.Handled = true;
            if (IsSearching && inList) { e.Effects = DragDropEffects.None; ClearInsertionMarker(); return; }
            try
            {
                if (IsInternalDrag(e.Data) && dragCategory != null && Files.Same(dragCategory, target))
                {
                    if (!inList) return;
                    string anchor; bool after; Rect marker;
                    GetDropPosition(e, out anchor, out after, out marker);
                    if (itemOrder.Reorder(current, allEntries, draggedEntries.Select(item => item.Path), anchor, after))
                    {
                        e.Effects = DragDropEffects.Move;
                        var selected = new HashSet<string>(draggedEntries.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
                        RefreshContent();
                        foreach (Entry item in list.Items) if (selected.Contains(item.Path) && !list.SelectedItems.Contains(item)) list.SelectedItems.Add(item);
                    }
                }
                else if (IsInternalDrag(e.Data)) MoveEntries(draggedEntries.Select(item => item.Path).ToArray(), target);
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    MoveEntries((string[])e.Data.GetData(DataFormats.FileDrop), target);
            }
            catch (Exception ex) { Program.Log(ex); Alert("无法保存排列顺序：" + ex.Message); }
            finally { ClearInsertionMarker(); }
        }
        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || interacting) return;
            Point point = e.GetPosition(list);
            if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var selected = new HashSet<Entry>(list.SelectedItems.Cast<Entry>());
            draggedEntries = allEntries.Where(selected.Contains).ToArray();
            if (draggedEntries.Length == 0) { draggedEntries = null; return; }
            string[] sources = draggedEntries.Select(CategoryOf).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            dragCategory = sources.Length == 1 ? sources[0] : null; dragToken = Guid.NewGuid().ToString("N");
            var data = new DataObject(); data.SetData(InternalDragFormat, dragToken);
            string[] physical = draggedEntries.Where(item => !item.Virtual).Select(item => item.Path).ToArray();
            if (physical.Length == draggedEntries.Length) data.SetData(DataFormats.FileDrop, physical);
            interacting = true;
            try { DragDrop.DoDragDrop(list, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link); }
            finally { ClearInsertionMarker(); draggedEntries = null; dragCategory = dragToken = null; interacting = false; dirty = true; lastInside = DateTime.UtcNow; }
        }
        private void MoveEntries(string[] paths, string destination)
        {
            interacting = true;
            var failures = new List<string>();
            try
            {
                var systemIds = new HashSet<string>(virtualEntries.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
                string[] selectedSystemIds = paths.Where(systemIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (selectedSystemIds.Length > 0)
                {
                    try { virtualCategories.Assign(selectedSystemIds, destination, desktop, groupsRoot); }
                    catch (Exception ex) { Program.Log(ex); failures.Add("系统图标：" + ex.Message); }
                }
                foreach (string path in paths.Where(path => !systemIds.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (Files.Same(path, desktop) || Files.Same(path, common) || Files.Same(path, groupsRoot)) throw new IOException("不能移动桌面或分类总目录。");
                        Files.Move(path, destination);
                    }
                    catch (Exception ex) { failures.Add(Path.GetFileName(path) + "：" + ex.Message); }
                }
                ReloadContent();
                if (failures.Count > 0) MessageBox.Show(this, String.Join("\n", failures.Take(8)), "以下项目未移动", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { interacting = false; lastInside = DateTime.UtcNow; }
        }
        private void DeleteSelected(bool permanent)
        {
            if (deleting || draggedEntries != null) return;
            Entry[] selected = list.SelectedItems.Cast<Entry>().ToArray();
            if (selected.Length == 0) return;
            if (selected.Any(item => item.Virtual)) { Alert("系统图标不能删除，请只选择文件或文件夹。"); return; }
            string[] roots = IsSearching ? CategoryEntries().SelectMany(category => Files.Same(category.Path, desktop) ? new[] { desktop, common } : new[] { category.Path }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : Files.Same(current, desktop) ? new[] { desktop, common } : new[] { current };
            string[] protectedRoots = new[] { desktop, common, groupsRoot };
            deleting = true;
            try
            {
                string[] paths = Files.DeletionPaths(selected.Select(item => item.Path), roots, protectedRoots);
                if (permanent)
                {
                    string names = String.Join("\n", selected.Take(6).Select(item => item.Name));
                    if (selected.Length > 6) names += "\n……";
                    string message = "永久删除这 " + selected.Length + " 项？\n\n" + names + "\n\n不会移入 Windows 回收站。";
                    if (paths.Any(Directory.Exists)) message += "文件夹内的内容也会删除。";
                    if (MessageBox.Show(this, message, "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                }
                Files.Delete(paths, roots, protectedRoots, permanent, handle);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Alert(ex.Message); }
            finally
            {
                try
                {
                    var remaining = new HashSet<string>(selected.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
                    ReloadContent();
                    foreach (Entry item in list.Items) if (remaining.Contains(item.Path) && !list.SelectedItems.Contains(item)) list.SelectedItems.Add(item);
                }
                catch (Exception ex) { Program.Log(ex); }
                deleting = false; lastInside = DateTime.UtcNow;
                if (IsVisible) list.Focus();
            }
        }
        private string AskName(string title, string initial, bool selectStem = false)
        {
            interacting = true;
            try
            {
                var prompt = new Window { Title = title, Width = 360, Height = 160, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, Topmost = true, ShowInTaskbar = false, FontFamily = FontFamily, Background = new SolidColorBrush(Color.FromRgb(24, 27, 33)), Foreground = Foreground };
                var panel = new StackPanel { Margin = new Thickness(18) };
                var input = new TextBox { Style = inputStyle, Text = initial, Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 14) };
                System.Windows.Automation.AutomationProperties.SetName(input, title == "重命名" ? "文件名称" : "分类名称");
                panel.Children.Add(input);
                var ok = Button("确定", delegate { prompt.DialogResult = true; }); ok.IsDefault = true;
                panel.Children.Add(ok); prompt.Content = panel;
                prompt.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; prompt.DialogResult = false; } };
                prompt.Loaded += delegate { input.Focus(); input.Select(0, selectStem ? Path.GetFileNameWithoutExtension(initial).Length : initial.Length); };
                return prompt.ShowDialog() == true ? input.Text.Trim() : null;
            }
            finally { interacting = false; lastInside = DateTime.UtcNow; }
        }
        private void CreateGroup()
        {
            string name = AskName("新建分类", ""); if (name == null) return;
            try { string path = Files.GroupPath(groupsRoot, name); if (Directory.Exists(path) || File.Exists(path)) throw new IOException("已有同名分类。"); Directory.CreateDirectory(path); current = path; search.Clear(); ReloadContent(); }
            catch (Exception ex) { Alert(ex.Message); }
        }
        private void CreateFolder()
        {
            bool previousBusy = shellBusy; shellBusy = true;
            try
            {
                string path = ShellClipboard.CreateFolder(handle, current);
                search.Clear(); ReloadContent();
                Entry created = allEntries.FirstOrDefault(item => !item.Virtual && Files.Same(item.Path, path));
                if (created != null) { list.SelectedItem = created; list.ScrollIntoView(created); list.Focus(); RenameSelected(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Program.Log(ex); Alert("无法新建文件夹：" + ex.Message); }
            finally { shellBusy = previousBusy; lastInside = DateTime.UtcNow; }
        }
        private void RenameGroup(string path)
        {
            string name = AskName("重命名分类", Path.GetFileName(path)); if (name == null) return;
            try
            {
                string target = Files.GroupPath(groupsRoot, name); if (Files.Same(target, path)) return;
                Directory.Move(path, target); if (Files.Same(current, path)) current = target;
                try { virtualCategories.RenameCategory(path, target); itemOrder.RenameCategory(path, target); itemOrder.RenameItem(groupsRoot, path, target); }
                catch (Exception ex) { Program.Log(ex); Alert("分类已重命名，但图标归属或排列顺序未保存：" + ex.Message); }
                ReloadContent();
            }
            catch (Exception ex) { Alert(ex.Message); }
        }
        private void Alert(string message) { interacting = true; try { MessageBox.Show(this, message, TitleText, MessageBoxButton.OK, MessageBoxImage.Information); } finally { interacting = false; lastInside = DateTime.UtcNow; } }
        private void Open(Entry item)
        {
            try { Start(item.Virtual ? "shell:" + item.Path : item.Path); if (pinned.IsChecked != true) HideDrawer(); }
            catch (Exception ex) { Alert("无法打开：" + ex.Message); }
        }
        private static void Start(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        private static void RevealInExplorer(string path) { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); }
        private ImageSource IconFor(string path)
        {
            string key = path.StartsWith("::{", StringComparison.Ordinal) ? path : (Directory.Exists(path) ? "<folder>" : (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ? path : Path.GetExtension(path)));
            ImageSource image;
            if (icons.TryGetValue(key, out image)) return image;
            var info = new Native.ShellInfo();
            if (path.StartsWith("::{", StringComparison.Ordinal))
            {
                IntPtr item; uint attributes;
                if (Native.SHParseDisplayName(path, IntPtr.Zero, out item, 0, out attributes) == 0)
                { try { Native.SHGetFileInfo(item, 0, ref info, (uint)Marshal.SizeOf(info), 0x100 | 0x8); } finally { Marshal.FreeCoTaskMem(item); } }
            }
            else Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100);
            IntPtr icon = info.Icon;
            if (icon == IntPtr.Zero) icon = System.Drawing.SystemIcons.Application.Handle;
            try { image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40)); image.Freeze(); }
            finally { if (info.Icon != IntPtr.Zero) Native.DestroyIcon(info.Icon); }
            icons[key] = image; return image;
        }
        private void LoadVirtualEntries()
        {
            object shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic dynamicShell = shell;
                folder = dynamicShell.NameSpace(0);
                dynamic dynamicFolder = folder; items = dynamicFolder.Items();
                foreach (object value in (System.Collections.IEnumerable)items)
                {
                    try
                    {
                        dynamic item = value;
                        string path = (string)item.Path;
                        if (!(bool)item.IsFileSystem && path.StartsWith("::{", StringComparison.Ordinal) && IsDesktopVirtualVisible(path))
                            virtualEntries.Add(new Entry { Name = (string)item.Name, Path = path, Virtual = true, Icon = IconFor(path) });
                    }
                    finally { if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
                }
            }
            catch (Exception ex) { Program.Log(ex); }
            finally { foreach (object value in new[] { items, folder, shell }) if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        }
        private static bool IsDesktopVirtualVisible(string path)
        {
            string id = path.Substring(2);
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
            using (var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath))
            using (var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
            {
                object flag = user == null ? null : user.GetValue(id);
                if (flag == null && machine != null) flag = machine.GetValue(id);
                if (flag != null) return Convert.ToInt32(flag) == 0;
            }
            return id.Equals("{645FF040-5081-101B-9F08-00AA002F954E}", StringComparison.OrdinalIgnoreCase);
        }
        private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (activeShellMenu != null)
            {
                IntPtr result = activeShellMenu.HandleMessage(message, wParam, lParam, ref handled);
                if (handled) return result;
            }
            if ((uint)message == Native.ShowMessage) { Reveal(true); handled = true; }
            if ((uint)message == Native.ExitMessage) { Close(); handled = true; }
            return IntPtr.Zero;
        }
        private void Tick(object sender, EventArgs e)
        {
            if (desktopLock.IsLocked) return;
            if (++lastIconCheck >= 120) { lastIconCheck = 0; if (!showDesktop) Native.SetDesktopVisible(false); }
            Native.Point cursor; if (!Native.GetCursorPos(out cursor)) return;
            DateTime now = DateTime.UtcNow;
            if (IsVisible)
            {
                IntPtr foreground = Native.GetForegroundWindow();
                uint foregroundProcessId; Native.GetWindowThreadProcessId(foreground, out foregroundProcessId);
                bool ownDialog = foreground != handle && foregroundProcessId == drawerProcessId;
                if (automaticReveal && !interacting && !deleting && !shellBusy && !ownDialog && foreground != handle &&
                    Native.IsScreenCoveringWindow(foreground, Forms.Screen.FromHandle(handle).Bounds))
                {
                    if (!hiding) HideDrawer();
                    return;
                }
                Native.Rect rect; Native.GetWindowRect(handle, out rect);
                bool inside = cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
                if (hiding)
                {
                    if (allowHoverReopen && (inside || interacting || deleting || shellBusy || ownDialog || pinned.IsChecked == true)) AnimateSlide(true);
                    return;
                }
                if (dirty && !interacting && !deleting && !shellBusy) { try { RefreshContent(); } catch (Exception ex) { Program.Log(ex); } }
                if (inside || interacting || deleting || shellBusy || ownDialog || pinned.IsChecked == true) lastInside = now;
                else if ((now - lastInside).TotalMilliseconds > 120) HideDrawer(true);
                return;
            }
            Forms.Screen monitor = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
            bool atEdge = cursor.Y == monitor.Bounds.Top;
            if (!atEdge) { edgeSince = DateTime.MaxValue; edgeMonitor = ""; return; }
            if (edgeMonitor != monitor.DeviceName) { edgeMonitor = monitor.DeviceName; edgeSince = now; }
            if ((now - edgeSince).TotalMilliseconds >= 80) Reveal();
        }
        internal void Reveal(bool explicitRequest = false)
        {
            if (desktopLock.IsLocked || desktopLockStarting) return;
            Native.Point cursor; Native.GetCursorPos(out cursor);
            Forms.Screen monitor = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
            if (!explicitRequest && Native.HasScreenCoveringWindow(monitor.Bounds, handle))
            {
                edgeSince = DateTime.UtcNow;
                return;
            }
            automaticReveal = !explicitRequest;
            if (IsVisible)
            {
                if (hiding) AnimateSlide(true);
                lastInside = DateTime.UtcNow;
                return;
            }
            if (dirty) { try { RefreshContent(); } catch (Exception ex) { Alert(ex.Message); } }
            Native.SetWindowPos(handle, new IntPtr(-1), monitor.Bounds.Left, monitor.Bounds.Top, 0, 0, 0x1 | 0x10);
            // Native coordinates avoid mixing physical pixels and WPF units on monitors with different DPI.
            int dpi = (int)Native.GetDpiForWindow(handle); if (dpi < 96) dpi = 96;
            double scale = dpi / 96.0;
            int width = monitor.Bounds.Width;
            int height = monitor.Bounds.Height / 3;
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = -height / scale;
            Show();
            Native.SetWindowPos(handle, new IntPtr(-1), monitor.Bounds.Left, monitor.Bounds.Top, width, height, 0x10 | 0x40);
            UpdateLayout();
            slide.Y = -body.ActualHeight;
            AnimateSlide(true);
            lastInside = DateTime.UtcNow; edgeSince = DateTime.MaxValue; edgeMonitor = "";
        }
        private void AnimateSlide(bool opening)
        {
            int version = ++slideVersion;
            double from = slide.Y;
            double to = opening ? 0 : -body.ActualHeight;
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = from;
            hiding = !opening;
            Action complete = delegate {
                if (version != slideVersion) return;
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = to;
                hiding = false;
                if (!opening) Hide();
            };
            if (!SystemParameters.ClientAreaAnimation) { complete(); return; }
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(opening ? 180 : 150)) {
                EasingFunction = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseInOut }
            };
            animation.Completed += delegate { complete(); };
            slide.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        private void HideDrawer(bool reopenOnHover = false)
        {
            pinned.IsChecked = false;
            if (!IsVisible || hiding) return;
            allowHoverReopen = reopenOnHover;
            AnimateSlide(false);
            edgeSince = DateTime.MaxValue; edgeMonitor = "";
        }
        private void StartWatchdog()
        {
            Process.Start(new ProcessStartInfo(Program.Executable, "--watch " + Process.GetCurrentProcess().Id + " " + (originalIconsVisible ? "1" : "0")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        private bool IsStartupEnabled()
        {
            return Startup.IsEnabled();
        }
        private void SetStartup(bool enabled)
        {
            try { Startup.SetEnabled(enabled); }
            catch (Exception ex) { Program.Log(ex); Alert("无法修改开机启动：" + ex.Message); }
        }
        internal void Cleanup()
        {
            if (closing) return; closing = true;
            if (desktopLock != null) desktopLock.Dispose();
            if (timer != null) timer.Stop();
            if (usageTimer != null) usageTimer.Stop();
            foreach (var watcher in watchers) watcher.Dispose();
            tray.Visible = false; tray.Dispose(); Native.SetDesktopVisible(originalIconsVisible);
        }
    }

    internal static class Extensions
    {
        internal static void AutomationName(this DependencyObject control, string name) { System.Windows.Automation.AutomationProperties.SetName(control, name); }
    }

    internal static class Program
    {
        internal static readonly string Executable = Process.GetCurrentProcess().MainModule.FileName;
        private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDrawer", "errors.log");
        internal static void Log(Exception exception)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)); File.AppendAllText(LogFile, DateTime.Now.ToString("s") + " " + exception + Environment.NewLine); } catch { }
        }
        internal static void LogDesktopLock(string state)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogFile)); File.AppendAllText(Path.Combine(Path.GetDirectoryName(LogFile), "desktop-lock.log"), DateTime.Now.ToString("s") + " " + state + Environment.NewLine); } catch { }
        }
        [STAThread] private static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--watch")
            {
                int id;
                if (Int32.TryParse(args[1], out id))
                {
                    EventWaitHandle recovery = null;
                    try { recovery = EventWaitHandle.OpenExisting(DesktopLock.RecoveryEventName(id)); } catch (WaitHandleCannotBeOpenedException) { }
                    try { using (Process process = Process.GetProcessById(id)) process.WaitForExit(); } catch (ArgumentException) { }
                    if (recovery != null) { try { if (recovery.WaitOne(0)) DesktopLock.RecoverAfterCrash(); } finally { recovery.Dispose(); } }
                    Native.SetDesktopVisible(args[2] == "1");
                }
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTest(args[1]);
            if (args.Length == 2 && args[0] == "--self-test-desktop-pin") return SelfTestDesktopPin(args[1]);
            if (args.Length == 1 && (args[0] == "--enable-startup" || args[0] == "--disable-startup"))
            {
                try { Startup.SetEnabled(args[0] == "--enable-startup"); return 0; }
                catch (Exception ex) { Log(ex); return 1; }
            }
            if (args.Length > 0 && (args[0] == "--exit" || args[0] == "--restore-icons"))
            {
                IntPtr existing = Native.FindWindow(null, Drawer.TitleText);
                if (existing != IntPtr.Zero)
                {
                    uint processId; Native.GetWindowThreadProcessId(existing, out processId);
                    Native.PostMessage(existing, Native.ExitMessage, IntPtr.Zero, IntPtr.Zero);
                    try { using (Process process = Process.GetProcessById((int)processId)) process.WaitForExit(3000); } catch (ArgumentException) { }
                }
                if (args[0] == "--restore-icons") Native.SetDesktopVisible(true);
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\DesktopDrawer.9e7e0429", out created))
            {
                if (!created) { if (!args.Contains("--startup")) Native.PostMessage(Native.FindWindow(null, Drawer.TitleText), Native.ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                Drawer drawer = null;
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { Log(e.Exception); e.Handled = true; app.Shutdown(1); };
                    drawer = new Drawer(); app.MainWindow = drawer;
                    if (args.Contains("--show")) drawer.Reveal(true);
                    return app.Run();
                }
                catch (Exception ex) { Log(ex); if (!args.Contains("--startup")) MessageBox.Show("启动失败：" + ex.Message, Drawer.TitleText); return 1; }
                finally { if (drawer != null) drawer.Cleanup(); mutex.ReleaseMutex(); }
            }
        }
        private static int SelfTestDesktopPin(string output)
        {
            string path = Path.Combine(Path.GetTempPath(), "DesktopPin-check-" + Guid.NewGuid().ToString("N") + ".pin");
            try
            {
                var pin = new DesktopPin(path);
                Require(!pin.IsConfigured && !DesktopPin.IsValid("12345") && !DesktopPin.IsValid("1234567") && !DesktopPin.IsValid("abc123"), "six ASCII digits required");
                pin.Save("615203"); pin.CheckReadable();
                Require(pin.Verify("615203") && !pin.Verify("615204") && !pin.Verify(""), "correct PIN accepted, incorrect PIN rejected");
                Require(new DesktopPin(path).Verify("615203"), "PIN persists across instances");
                string original = File.ReadAllText(path); bool rejected = false;
                try { pin.Save("12"); } catch (ArgumentException) { rejected = true; }
                Require(rejected && original == File.ReadAllText(path), "invalid replacement preserves original record");
                pin.Save("615203"); Require(original != File.ReadAllText(path) && pin.Verify("615203"), "fresh salt for each save");
                string[] protectedRecord = File.ReadAllLines(path);
                byte[] changedRecord = Convert.FromBase64String(protectedRecord[1]); changedRecord[changedRecord.Length - 1] ^= 1;
                File.WriteAllLines(path, new[] { protectedRecord[0], Convert.ToBase64String(changedRecord) }, Encoding.UTF8); rejected = false;
                try { pin.Verify("615203"); } catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
                Require(rejected, "tampered encrypted record never authenticates");
                File.WriteAllText(path, "corrupted"); rejected = false;
                try { pin.Verify("615203"); } catch (IOException) { rejected = true; }
                Require(rejected, "corrupt record never authenticates");
                File.WriteAllText(output, "PASS: six-digit validation, current-user encrypted hash, correct/wrong PIN, persistence, invalid replacement, random salt, encrypted-record tamper rejection, corrupt record rejection; no desktop lock activated.", Encoding.UTF8);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(output, "FAIL: " + ex, Encoding.UTF8); return 1; }
            finally { foreach (string file in new[] { path, path + ".tmp", path + ".bak" }) if (File.Exists(file)) File.Delete(file); }
        }
        private static int SelfTest(string output)
        {
            string root = Path.Combine(Path.GetTempPath(), "DesktopDrawer-check-" + Guid.NewGuid().ToString("N"));
            var checks = new List<string>();
            try
            {
                Directory.CreateDirectory(root);
                string source = Path.Combine(root, "source"), target = Path.Combine(root, "target"); Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                string file = Path.Combine(source, "data.txt"); File.WriteAllText(file, "完整内容测试");
                string moved = Files.Move(file, target); Require(!File.Exists(file) && File.ReadAllText(moved) == "完整内容测试", "move preserves contents"); checks.Add("文件移动及内容完整");
                File.WriteAllText(file, "另一个文件"); bool rejected = false;
                try { Files.Move(file, target); } catch (IOException) { rejected = true; }
                Require(rejected && File.ReadAllText(file) == "另一个文件" && File.ReadAllText(moved) == "完整内容测试", "collision never overwrites"); checks.Add("同名冲突保留双方");
                foreach (string invalid in new[] { "..", "../escape", "CON", "COM1.txt", "x.", "", "待整理" })
                { rejected = false; try { Files.GroupPath(target, invalid); } catch (IOException) { rejected = true; } Require(rejected, "invalid group " + invalid); }
                Require(Files.Inside(Files.GroupPath(target, "工作"), target), "valid group"); checks.Add("分类名称及路径边界");
                string child = Path.Combine(source, "child"); Directory.CreateDirectory(child); rejected = false;
                try { Files.Move(source, child); } catch (IOException) { rejected = true; } Require(rejected && Directory.Exists(source), "reject recursive move"); checks.Add("拒绝移入自身子目录");
                string folder = Path.Combine(source, "folder"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "nested.txt"), "nested"); string movedFolder = Files.Move(folder, target);
                Require(!Directory.Exists(folder) && File.ReadAllText(Path.Combine(movedFolder, "nested.txt")) == "nested", "folder move"); checks.Add("文件夹整体移动");
                string category = Path.Combine(root, "manual-order"), orderDirectory = Path.Combine(root, "order-data");
                Directory.CreateDirectory(category);
                Entry[] entries = new[] { "a", "b", "c", "d" }.Select(name => new Entry { Name = name, Path = Path.Combine(category, name + ".txt") }).ToArray();
                foreach (Entry entry in entries) File.WriteAllText(entry.Path, "original:" + entry.Name);
                var order = new ItemOrder(orderDirectory);
                Require(order.Reorder(category, entries, new[] { entries[2].Path }, entries[0].Path, false), "single item reorder");
                Require(String.Join(",", order.Apply(category, entries).Select(item => item.Name)) == "c,a,b,d", "insert before anchor");
                Require(String.Join(",", new ItemOrder(orderDirectory).Apply(category, entries).Select(item => item.Name)) == "c,a,b,d", "persist order across restart");
                checks.Add("单项拖动排序与重启持久化");
                Require(order.Reorder(category, entries, new[] { entries[0].Path, entries[2].Path }, entries[3].Path, true), "multi selection reorder");
                Require(String.Join(",", order.Apply(category, entries).Select(item => item.Name)) == "b,d,c,a", "preserve dragged selection order");
                Require(!order.Reorder(category, entries, new[] { entries[0].Path }, entries[0].Path, true), "drop on self is unchanged");
                checks.Add("多选相对顺序与拖回自身");
                var added = new Entry { Name = "aa", Path = Path.Combine(category, "aa.txt") };
                File.WriteAllText(added.Path, "original:aa");
                entries = entries.Concat(new[] { added }).ToArray();
                Require(order.Apply(category, entries).Last() == added, "new item appends after custom order");
                Require(order.Reorder(category, entries, new[] { entries[0].Path }, entries[1].Path, false), "filtered anchor reorder");
                Require(String.Join(",", order.Apply(category, entries).Select(item => item.Name)) == "a,b,d,c,aa", "hidden items keep their order");
                Require(entries.All(item => File.ReadAllText(item.Path) == "original:" + item.Name), "sorting never moves or changes files");
                checks.Add("新增项目追加、筛选排序与原文件完整");
                Require(String.Join(",", order.Apply(Path.Combine(root, "other-category"), entries).Select(item => item.Name)) == "a,aa,b,c,d", "categories have independent order");
                string renamedCategory = Path.Combine(root, "renamed-order");
                Directory.Move(category, renamedCategory); order.RenameCategory(category, renamedCategory);
                Entry[] renamed = entries.Select(item => new Entry { Name = item.Name, Path = Path.Combine(renamedCategory, Path.GetFileName(item.Path)) }).ToArray();
                Require(String.Join(",", new ItemOrder(orderDirectory).Apply(renamedCategory, renamed).Select(item => item.Name)) == "a,b,d,c,aa", "renamed category preserves order");
                checks.Add("分类独立排列与分类重命名");
                var systemIcon = new Entry { Name = "回收站", Path = "::{645FF040-5081-101B-9F08-00AA002F954E}", Virtual = true };
                var virtualCategory = Path.Combine(root, "virtual-order");
                Entry[] withSystemIcon = renamed.Take(1).Concat(new[] { systemIcon }).ToArray();
                Require(order.Reorder(virtualCategory, withSystemIcon, new[] { systemIcon.Path }, withSystemIcon[0].Path, false), "virtual icon reorder");
                Require(new ItemOrder(orderDirectory).Apply(virtualCategory, withSystemIcon).First().Virtual, "virtual icon order persists");
                checks.Add("系统图标排序与持久化");
                string tabRoot = Path.Combine(root, "tab-order");
                Entry pendingTab = new Entry { Name = "待整理", Path = source };
                Entry workTab = new Entry { Name = "工作", Path = Path.Combine(tabRoot, "工作") };
                Entry learnTab = new Entry { Name = "学习", Path = Path.Combine(tabRoot, "学习") };
                Entry[] categoryTabs = { pendingTab, workTab, learnTab };
                Require(order.Apply(tabRoot, categoryTabs, true).SequenceEqual(categoryTabs), "keep initial category order");
                Require(order.Reorder(tabRoot, categoryTabs, new[] { learnTab.Path }, pendingTab.Path, false, true), "move category to front");
                Require(new ItemOrder(orderDirectory).Apply(tabRoot, categoryTabs, true).First() == learnTab, "category order persists");
                string oldTabPath = learnTab.Path; learnTab.Path = Path.Combine(tabRoot, "新学习");
                order.RenameItem(tabRoot, oldTabPath, learnTab.Path);
                Require(new ItemOrder(orderDirectory).Apply(tabRoot, categoryTabs, true).First() == learnTab, "renamed category keeps position");
                Entry newTab = new Entry { Name = "a", Path = Path.Combine(tabRoot, "a") };
                Require(order.Apply(tabRoot, categoryTabs.Concat(new[] { newTab }), true).Last() == newTab, "new category appends");
                Require(order.Reorder(tabRoot, categoryTabs, new[] { pendingTab.Path }, workTab.Path, true, true), "pending category can move");
                Require(!order.Reorder(tabRoot, categoryTabs, new[] { pendingTab.Path }, pendingTab.Path, true, true), "category self drop unchanged");
                Require(new ItemOrder(orderDirectory).Apply(tabRoot, categoryTabs.Where(item => item != workTab), true).SequenceEqual(new[] { learnTab, pendingTab }), "removed category does not disturb others");
                Require(new ItemOrder(orderDirectory).Apply(virtualCategory, withSystemIcon).First().Virtual, "tab order does not change file order");
                checks.Add("分类拖动排序、重启、重命名、新增及删除分类后的顺序");
                string iconRoot = Path.Combine(root, "system-icon-groups"), iconGroup = Path.Combine(iconRoot, "系统");
                Directory.CreateDirectory(iconGroup);
                string iconRecord = Path.Combine(root, "system-icons.groups");
                string[] systemIds = { "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", "::{645FF040-5081-101B-9F08-00AA002F954E}" };
                var iconGroups = new VirtualCategories(iconRecord);
                Require(systemIds.All(id => Files.Same(iconGroups.CategoryFor(id, source, iconRoot), source)), "system icons default to pending");
                iconGroups.Assign(systemIds, iconGroup, source, iconRoot);
                Require(systemIds.All(id => Files.Same(new VirtualCategories(iconRecord).CategoryFor(id, source, iconRoot), iconGroup)), "all three system icon categories persist");
                iconGroups.Assign(new[] { systemIds[0] }, source, source, iconRoot);
                Require(Files.Same(new VirtualCategories(iconRecord).CategoryFor(systemIds[0], source, iconRoot), source), "move system icon back to pending");
                string renamedIconGroup = Path.Combine(iconRoot, "系统重命名"); Directory.Move(iconGroup, renamedIconGroup); iconGroups.RenameCategory(iconGroup, renamedIconGroup);
                Require(systemIds.Skip(1).All(id => Files.Same(new VirtualCategories(iconRecord).CategoryFor(id, source, iconRoot), renamedIconGroup)), "category rename preserves virtual membership");
                string beforeInvalid = File.ReadAllText(iconRecord); rejected = false;
                try { iconGroups.Assign(systemIds, target, source, iconRoot); } catch (IOException) { rejected = true; }
                Require(rejected && File.ReadAllText(iconRecord) == beforeInvalid, "outside category cannot change membership");
                Directory.Delete(renamedIconGroup);
                Require(systemIds.All(id => Files.Same(new VirtualCategories(iconRecord).CategoryFor(id, source, iconRoot), source)), "missing group returns icons to pending");
                checks.Add("三个系统图标分类、移回待整理、重启、分类重命名及分类丢失回退");
                string deleteParent = Path.Combine(root, "delete-check"); Directory.CreateDirectory(deleteParent);
                string deleteFile = Path.Combine(deleteParent, "selected.txt"), unselected = Path.Combine(root, "unselected.txt");
                File.WriteAllText(deleteFile, "selected"); File.WriteAllText(unselected, "unselected");
                string[] allowedDeleteParents = { deleteParent }, protectedDeletePaths = { root, deleteParent };
                rejected = false;
                try { Files.Delete(new[] { deleteFile, unselected }, allowedDeleteParents, protectedDeletePaths, true, IntPtr.Zero); } catch (IOException) { rejected = true; }
                Require(rejected && File.Exists(deleteFile) && File.ReadAllText(unselected) == "unselected", "invalid batch deletes nothing");
                foreach (string invalid in new[] { deleteParent, root, Path.Combine(deleteParent, "*.txt"), "::{645FF040-5081-101B-9F08-00AA002F954E}" })
                {
                    rejected = false;
                    try { Files.DeletionPaths(new[] { invalid }, allowedDeleteParents, protectedDeletePaths); } catch (IOException) { rejected = true; }
                    Require(rejected, "protected deletion path " + invalid);
                }
                checks.Add("删除范围保护与无效批次不删除");
                string shortcut = Path.Combine(deleteParent, "selected-link.lnk");
                object wscript = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object shortcutObject = null;
                try { dynamic shell = wscript; shortcutObject = shell.CreateShortcut(shortcut); dynamic link = shortcutObject; link.TargetPath = unselected; link.Save(); }
                finally { if (shortcutObject != null) Marshal.FinalReleaseComObject(shortcutObject); Marshal.FinalReleaseComObject(wscript); }
                Files.Delete(new[] { shortcut }, allowedDeleteParents, protectedDeletePaths, true, IntPtr.Zero);
                Require(!File.Exists(shortcut) && File.ReadAllText(unselected) == "unselected", "deleting shortcut preserves target");
                checks.Add("删除快捷方式不删除目标");
                string deleteTree = Path.Combine(deleteParent, "selected-folder"); Directory.CreateDirectory(Path.Combine(deleteTree, "nested"));
                File.WriteAllText(Path.Combine(deleteTree, "nested", "content.txt"), "nested content");
                Files.Delete(new[] { deleteFile, deleteTree }, allowedDeleteParents, protectedDeletePaths, true, IntPtr.Zero);
                Require(!File.Exists(deleteFile) && !Directory.Exists(deleteTree) && File.ReadAllText(unselected) == "unselected", "permanent batch deletion");
                checks.Add("文件与文件夹批量永久删除");
                File.WriteAllText(output, "PASS\n" + String.Join("\n", checks), Encoding.UTF8); return 0;
            }
            catch (Exception ex) { File.WriteAllText(output, "FAIL\n" + ex, Encoding.UTF8); return 1; }
            finally { if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("DesktopDrawer-check-", StringComparison.Ordinal)) Directory.Delete(root, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
