using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;

namespace DesktopDrawer
{
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IntPtr created);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, IntPtr created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    internal static class ShellClipboard
    {
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint flags, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int result);
        }
        [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOperation
        {
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOperationFlags(uint flags);
            void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetProgressDialog(IntPtr dialog);
            void SetProperties(IntPtr properties);
            void SetOwnerWindow(IntPtr owner);
            void ApplyPropertiesToItem(IShellItem item);
            void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
            void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
            void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
            void DeleteItem(IShellItem item, IntPtr sink);
            void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
            void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
            void PerformOperations();
            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        private sealed class CreatedItemSink : IFileOperationProgressSink
        {
            internal string Path;
            internal int Result;
            public int StartOperations() { return 0; }
            public int FinishOperations(int result) { if (result < 0) Result = result; return 0; }
            public int PreRenameItem(uint flags, IntPtr item, string name) { return 0; }
            public int PostRenameItem(uint flags, IntPtr item, string name, int result, IntPtr created) { return 0; }
            public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, string name) { return 0; }
            public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, string name, int result, IntPtr created) { return 0; }
            public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, string name) { return 0; }
            public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, string name, int result, IntPtr created) { return 0; }
            public int PreDeleteItem(uint flags, IntPtr item) { return 0; }
            public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created) { return 0; }
            public int PreNewItem(uint flags, IntPtr destination, string name) { return 0; }
            public int PostNewItem(uint flags, IntPtr destination, string name, string template, uint attributes, int result, IntPtr created)
            {
                Result = result;
                if (result < 0 || created == IntPtr.Zero) return 0;
                object item = null; IntPtr text = IntPtr.Zero;
                try { item = Marshal.GetObjectForIUnknown(created); ((IShellItem)item).GetDisplayName(0x80058000, out text); Path = Marshal.PtrToStringUni(text); }
                catch (Exception ex) { Result = Marshal.GetHRForException(ex); }
                finally { if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text); if (item != null) Marshal.ReleaseComObject(item); }
                return 0;
            }
            public int UpdateProgress(uint total, uint completed) { return 0; }
            public int ResetTimer() { return 0; }
            public int PauseTimer() { return 0; }
            public int ResumeTimer() { return 0; }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);
        [DllImport("ole32.dll")] private static extern int OleGetClipboard(out System.Runtime.InteropServices.ComTypes.IDataObject data);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("shell32.dll")] private static extern int SHMultiFileProperties(System.Runtime.InteropServices.ComTypes.IDataObject data, uint flags);
        [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr item);
        private static System.Windows.Forms.DataObject FileData(string[] paths)
        {
            var names = new System.Collections.Specialized.StringCollection();
            foreach (string path in paths)
            {
                if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("项目已被移动或删除：" + Path.GetFileName(path));
                names.Add(Files.Full(path));
            }
            var data = new System.Windows.Forms.DataObject(); data.SetFileDropList(names);
            var ids = new System.Collections.Generic.List<byte[]>();
            foreach (string path in names)
            {
                IntPtr id = IntPtr.Zero;
                try
                {
                    uint attributes; Marshal.ThrowExceptionForHR(Native.SHParseDisplayName(path, IntPtr.Zero, out id, 0, out attributes));
                    var bytes = new byte[ILGetSize(id)]; Marshal.Copy(id, bytes, 0, bytes.Length); ids.Add(bytes);
                }
                finally { if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id); }
            }
            // CIDA uses the desktop's empty PIDL as parent, so children can refer to files in different folders.
            var shellIds = new MemoryStream();
            var writer = new BinaryWriter(shellIds);
            writer.Write((uint)ids.Count);
            uint offset = (uint)(4 + (ids.Count + 1) * 4); writer.Write(offset); offset += 2;
            foreach (byte[] id in ids) { writer.Write(offset); offset += (uint)id.Length; }
            writer.Write((ushort)0); foreach (byte[] id in ids) writer.Write(id);
            shellIds.Position = 0; data.SetData("Shell IDList Array", shellIds);
            return data;
        }
        internal static void Copy(string[] paths, bool cut)
        {
            var data = FileData(paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            System.Windows.Forms.Clipboard.SetDataObject(data, true);
        }
        internal static void Properties(string[] paths)
        {
            Marshal.ThrowExceptionForHR(SHMultiFileProperties((System.Runtime.InteropServices.ComTypes.IDataObject)FileData(paths), 0));
        }
        internal static bool CanPaste()
        {
            return Clipboard.ContainsFileDropList() || Clipboard.ContainsData("Shell IDList Array");
        }
        internal static string CreateFolder(IntPtr owner, string directory)
        {
            if (!Directory.Exists(directory)) throw new IOException("目标文件夹已不存在。");
            directory = Files.Full(directory);
            IShellItem destination = null; IFileOperation operation = null;
            var progress = new CreatedItemSink(); IntPtr sink = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(directory, IntPtr.Zero, ref iid, out destination));
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                operation.SetOwnerWindow(owner); operation.SetOperationFlags(0x40 | 0x200 | 0x8 | 0x20000000);
                sink = Marshal.GetComInterfaceForObject(progress, typeof(IFileOperationProgressSink));
                operation.NewItem(destination, 0x10, "新建文件夹", null, sink);
                operation.PerformOperations();
                bool aborted; operation.GetAnyOperationsAborted(out aborted);
                if (aborted) throw new OperationCanceledException();
                Marshal.ThrowExceptionForHR(progress.Result);
                if (String.IsNullOrEmpty(progress.Path) || !Directory.Exists(progress.Path) || !Files.Same(System.IO.Path.GetDirectoryName(progress.Path), directory))
                    throw new IOException("Windows 未返回新建文件夹的位置，请刷新后查看。");
                return progress.Path;
            }
            finally
            {
                if (operation != null) Marshal.ReleaseComObject(operation);
                if (sink != IntPtr.Zero) Marshal.Release(sink);
                if (destination != null) Marshal.ReleaseComObject(destination);
                GC.KeepAlive(progress);
            }
        }
        internal static void Paste(IntPtr owner, string directory)
        {
            if (!CanPaste()) return;
            if (!Directory.Exists(directory)) throw new IOException("目标分类已不存在。");
            bool move = false;
            uint clipboardSequence = GetClipboardSequenceNumber();
            var effect = Clipboard.GetData("Preferred DropEffect") as MemoryStream;
            if (effect != null) { byte[] bytes = effect.ToArray(); move = bytes.Length >= 4 && (BitConverter.ToUInt32(bytes, 0) & 2) != 0; }
            System.Runtime.InteropServices.ComTypes.IDataObject data = null;
            IShellItem destination = null; IFileOperation operation = null;
            try
            {
                Marshal.ThrowExceptionForHR(OleGetClipboard(out data));
                Guid iid = typeof(IShellItem).GUID;
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(directory, IntPtr.Zero, ref iid, out destination));
                operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                operation.SetOwnerWindow(owner); operation.SetOperationFlags(0x40 | 0x200);
                if (move) operation.MoveItems(data, destination); else operation.CopyItems(data, destination);
                operation.PerformOperations();
                bool aborted; operation.GetAnyOperationsAborted(out aborted);
                if (!aborted && move && GetClipboardSequenceNumber() == clipboardSequence) Clipboard.Clear();
            }
            finally
            {
                if (operation != null) Marshal.ReleaseComObject(operation);
                if (destination != null) Marshal.ReleaseComObject(destination);
                if (data != null) Marshal.ReleaseComObject(data);
            }
        }
    }
}
