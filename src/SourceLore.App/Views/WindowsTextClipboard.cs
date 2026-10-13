using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceLore.App.Views;

// Windows x64 text path avoids delayed OLE callbacks into a disposed managed source.
internal static partial class WindowsTextClipboard
{
    public static async Task<string?> ReadAsync(nint owner)
    {
        var opened = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner)) { opened = true; break; }
            await Task.Delay(50);
        }
        if (!opened) throw new Win32Exception(Marshal.GetLastPInvokeError(), "剪贴板暂时被占用，请重试。");
        try
        {
            var memory = GetClipboardData(13);
            if (memory == 0) return null;
            var size = GlobalSize(memory);
            if (size > 32 * 1024 * 1024) throw new InvalidOperationException("剪贴板文本过大。");
            var pointer = GlobalLock(memory);
            if (pointer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var text = Marshal.PtrToStringUni(pointer, checked((int)(size / 2))) ?? "";
                var end = text.IndexOf('\0');
                return end < 0 ? text : text[..end];
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
    public static async Task SetAsync(nint owner, string text)
    {
        if (owner == 0) throw new InvalidOperationException("剪贴板窗口尚未就绪。");
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(2, (nuint)bytes.Length); // GMEM_MOVEABLE; transferred to Windows on success.
        if (memory == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            var opened = false;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (OpenClipboard(owner)) { opened = true; break; }
                await Task.Delay(50);
            }
            if (!opened) throw new Win32Exception(Marshal.GetLastPInvokeError(), "剪贴板暂时被占用，请重试。");
            try
            {
                if (!EmptyClipboard() || SetClipboardData(13, memory) == 0) // CF_UNICODETEXT
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                memory = 0;
            }
            finally { CloseClipboard(); }
        }
        finally { if (memory != 0) GlobalFree(memory); }
    }

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint GetClipboardData(uint format);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalAlloc(uint flags, nuint size);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalFree(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nuint GlobalSize(nint memory);
}
