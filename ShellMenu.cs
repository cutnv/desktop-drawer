using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopDrawer
{
    // ponytail: ask the Shell for its actual menu, including installed extensions and file-type verbs.
    internal sealed class ShellMenu : IDisposable
    {
        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            void ParseDisplayName(IntPtr owner, IntPtr context, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr item, ref uint attributes);
            void EnumObjects(IntPtr owner, uint flags, out IntPtr enumerator);
            void BindToObject(IntPtr item, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
            void BindToStorage(IntPtr item, IntPtr context, ref Guid iid, out IntPtr value);
            [PreserveSig] int CompareIDs(IntPtr flags, IntPtr first, IntPtr second);
            void CreateViewObject(IntPtr owner, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
            void GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] items, ref uint attributes);
            void GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] items, ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object value);
            void GetDisplayNameOf(IntPtr item, uint flags, IntPtr value);
            void SetNameOf(IntPtr owner, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr renamed);
        }
        [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumIDList
        {
            [PreserveSig] int Next(uint count, out IntPtr item, out uint fetched);
            void Skip(uint count);
            void Reset();
            void Clone(out IEnumIDList copy);
        }
        [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref CommandInfo info);
            [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr value, uint count);
        }
        [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref CommandInfo info);
            [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr value, uint count);
            [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        }
        [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
            void InvokeCommand(ref CommandInfo info);
            [PreserveSig] int GetCommandString(UIntPtr offset, uint flags, IntPtr reserved, IntPtr value, uint count);
            [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
            [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct CommandInfo
        {
            public uint Size, Mask;
            public IntPtr Owner, Verb, Parameters, Directory;
            public int Show;
            public uint HotKey;
            public IntPtr Icon, Title, VerbW, ParametersW, DirectoryW, TitleW;
            public Native.Point Point;
        }
        [DllImport("shell32.dll")] private static extern int SHGetDesktopFolder(out IShellFolder folder);
        [DllImport("shell32.dll")] private static extern int SHBindToParent(IntPtr item, ref Guid iid, out IShellFolder folder, out IntPtr child);
        [DllImport("shlwapi.dll", EntryPoint = "StrRetToStrW", ExactSpelling = true)] private static extern int StrRetToStr(IntPtr value, IntPtr item, out IntPtr text);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int index);
        [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint id, uint flags);
        private object context;
        private readonly IntPtr owner;
        internal IntPtr Handle { get; private set; }

        internal ShellMenu(IntPtr owner, string directory, string[] paths)
        {
            this.owner = owner;
            IShellFolder desktopFolder = null, folder = null;
            var ids = new List<IntPtr>();
            try
            {
                Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out desktopFolder));
                bool desktop = paths.Length > 0 && (paths.Any(path => path.StartsWith("::{", StringComparison.Ordinal)) ||
                    paths.Select(System.IO.Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
                folder = desktopFolder;
                if (!desktop)
                {
                    IntPtr id; uint eaten = 0, attributes = 0;
                    string parent = paths.Length == 0 ? directory : System.IO.Path.GetDirectoryName(paths[0]);
                    desktopFolder.ParseDisplayName(owner, IntPtr.Zero, parent, ref eaten, out id, ref attributes); ids.Add(id);
                    Guid folderId = typeof(IShellFolder).GUID; object value;
                    desktopFolder.BindToObject(id, IntPtr.Zero, ref folderId, out value); folder = (IShellFolder)value;
                }
                Guid menuId = typeof(IContextMenu).GUID;
                if (paths.Length == 0) folder.CreateViewObject(owner, ref menuId, out context);
                else
                {
                    var children = new List<IntPtr>();
                    if (desktop) children.AddRange(DesktopChildren(desktopFolder, owner, paths, ids));
                    else foreach (string path in paths)
                    {
                        IntPtr id; uint eaten = 0, attributes = 0;
                        folder.ParseDisplayName(owner, IntPtr.Zero, System.IO.Path.GetFileName(path), ref eaten, out id, ref attributes);
                        ids.Add(id); children.Add(id);
                    }
                    folder.GetUIObjectOf(owner, (uint)children.Count, children.ToArray(), ref menuId, IntPtr.Zero, out context);
                }
                Handle = CreatePopupMenu();
                if (Handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                uint flags = 0x10; // CMF_CANRENAME; the host handles the rename verb.
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) flags |= 0x100;
                Marshal.ThrowExceptionForHR(((IContextMenu)context).QueryContextMenu(Handle, 0, 1, 0x6fff, flags));
            }
            catch { Dispose(); throw; }
            finally
            {
                foreach (IntPtr id in ids) Marshal.FreeCoTaskMem(id);
                if (folder != null && !Object.ReferenceEquals(folder, desktopFolder)) Marshal.ReleaseComObject(folder);
                if (desktopFolder != null) Marshal.ReleaseComObject(desktopFolder);
            }
        }
        private static IntPtr[] DesktopChildren(IShellFolder folder, IntPtr owner, string[] paths, List<IntPtr> allocated)
        {
            // Desktop children must come from the desktop namespace, not absolute filesystem PIDLs.
            var found = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            IEnumIDList items = null; IntPtr raw = IntPtr.Zero, value = Marshal.AllocCoTaskMem(272);
            try
            {
                folder.EnumObjects(owner, 0x20 | 0x40 | 0x80, out raw);
                if (raw != IntPtr.Zero) { items = (IEnumIDList)Marshal.GetObjectForIUnknown(raw); Marshal.Release(raw); raw = IntPtr.Zero; }
                IntPtr id; uint fetched;
                while (items != null && items.Next(1, out id, out fetched) == 0 && fetched == 1)
                {
                    bool keep = false;
                    try
                    {
                        folder.GetDisplayNameOf(id, 0x8000, value);
                        IntPtr text; Marshal.ThrowExceptionForHR(StrRetToStr(value, id, out text));
                        string name; try { name = Marshal.PtrToStringUni(text); } finally { Marshal.FreeCoTaskMem(text); }
                        if (wanted.Contains(name) && !found.ContainsKey(name)) { found[name] = id; allocated.Add(id); keep = true; }
                    }
                    finally { if (!keep) Marshal.FreeCoTaskMem(id); }
                    if (found.Count == wanted.Count) break;
                }
                if (found.Count != wanted.Count) throw new System.IO.IOException("部分桌面项目已改变，请刷新后重试。");
                return paths.Select(path => found[path]).ToArray();
            }
            finally { Marshal.FreeCoTaskMem(value); if (raw != IntPtr.Zero) Marshal.Release(raw); if (items != null) Marshal.ReleaseComObject(items); }
        }
        internal void Add(uint id, string text, bool enabled = true)
        {
            AppendMenu(Handle, enabled ? 0u : 1u, new UIntPtr(id), text);
        }
        internal void Separator() { AppendMenu(Handle, 0x800, UIntPtr.Zero, null); }
        internal void AddSubmenu(string text, IEnumerable<KeyValuePair<uint, string>> items)
        {
            IntPtr submenu = CreatePopupMenu();
            foreach (var item in items) AppendMenu(submenu, 0, new UIntPtr(item.Key), item.Value);
            AppendMenu(Handle, 0x10, new UIntPtr((ulong)submenu.ToInt64()), text);
        }
        internal uint Show(Native.Point point)
        {
            SetForegroundWindow(owner);
            return TrackPopupMenuEx(Handle, 0x100 | 0x2, point.X, point.Y, owner, IntPtr.Zero);
        }
        internal uint FindCommand(string verb)
        {
            for (int i = 0; i < GetMenuItemCount(Handle); i++)
            {
                uint id = GetMenuItemID(Handle, i);
                if (id > 0 && id <= 0x6fff && (GetMenuState(Handle, id, 0) & 3) == 0 && String.Equals(Verb(id), verb, StringComparison.OrdinalIgnoreCase)) return id;
            }
            return 0;
        }
        internal static string Rename(IntPtr owner, string path, string name)
        {
            bool virtualItem = path.StartsWith("::{", StringComparison.Ordinal);
            string device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name == "." || name == ".." || name.EndsWith(".") || name.EndsWith(" ") ||
                name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                Enumerable.Range(1, 9).Any(i => device == "COM" + i || device == "LPT" + i)) throw new System.IO.IOException("文件名无效，请使用普通文件名。");
            string target = virtualItem ? path : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), name);
            if (!virtualItem && !Files.Same(path, target) && (System.IO.File.Exists(target) || System.IO.Directory.Exists(target))) throw new System.IO.IOException("已存在同名项目，未覆盖：" + name);
            IntPtr id = IntPtr.Zero, renamed = IntPtr.Zero; IShellFolder folder = null;
            try
            {
                uint attributes;
                Marshal.ThrowExceptionForHR(Native.SHParseDisplayName(path, IntPtr.Zero, out id, 0, out attributes));
                Guid folderId = typeof(IShellFolder).GUID; IntPtr child;
                Marshal.ThrowExceptionForHR(SHBindToParent(id, ref folderId, out folder, out child));
                folder.SetNameOf(owner, child, name, 0x8000, out renamed);
                return target;
            }
            finally
            {
                if (renamed != IntPtr.Zero) Marshal.FreeCoTaskMem(renamed);
                if (folder != null) Marshal.ReleaseComObject(folder);
                if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id);
            }
        }
        internal string Verb(uint command)
        {
            if (command == 0 || command > 0x6fff) return "";
            IntPtr buffer = Marshal.AllocHGlobal(520);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                if (((IContextMenu)context).GetCommandString(new UIntPtr(command - 1), 4, IntPtr.Zero, buffer, 260) >= 0) return Marshal.PtrToStringUni(buffer) ?? "";
                return "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal void Invoke(uint command, Native.Point point)
        {
            IntPtr offset = new IntPtr(command - 1);
            Invoke(offset, offset, point);
        }
        internal void Invoke(string verb)
        {
            IntPtr ansi = Marshal.StringToHGlobalAnsi(verb), unicode = Marshal.StringToHGlobalUni(verb);
            try { Native.Point point; Native.GetCursorPos(out point); Invoke(ansi, unicode, point); }
            finally { Marshal.FreeHGlobal(ansi); Marshal.FreeHGlobal(unicode); }
        }
        private void Invoke(IntPtr verb, IntPtr unicode, Native.Point point)
        {
            uint flags = 0x4000 | 0x20000000;
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) flags |= 0x10000000;
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) flags |= 0x40000000;
            var info = new CommandInfo { Size = (uint)Marshal.SizeOf(typeof(CommandInfo)), Mask = flags, Owner = owner, Verb = verb, VerbW = unicode, Show = 1, Point = point };
            ((IContextMenu)context).InvokeCommand(ref info);
        }
        internal IntPtr HandleMessage(int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x117 && message != 0x2b && message != 0x2c && message != 0x120) return IntPtr.Zero;
            IntPtr result;
            var third = context as IContextMenu3;
            if (third != null && third.HandleMenuMsg2((uint)message, wParam, lParam, out result) == 0) { handled = true; return result; }
            var second = context as IContextMenu2;
            if (message != 0x120 && second != null && second.HandleMenuMsg((uint)message, wParam, lParam) == 0) handled = true;
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { DestroyMenu(Handle); Handle = IntPtr.Zero; }
            if (context != null) { Marshal.ReleaseComObject(context); context = null; }
        }
    }
}
