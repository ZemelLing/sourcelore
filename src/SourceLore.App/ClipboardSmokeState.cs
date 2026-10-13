using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SourceLore.App;

// Diagnostic oracle uses GetClipboardData, independently of the editor's write path.
// Duplicate every available format before writing; an unsupported format aborts before any overwrite.
internal sealed partial class ClipboardSmokeState : IDisposable
{
    private readonly List<(uint Format, nint Handle)> saved = [];
    private readonly nint owner;
    private ClipboardSmokeState(nint owner) => this.owner = owner;

    private static async Task OpenAsync(nint owner)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner)) return;
            await Task.Delay(50);
        }
        throw new Win32Exception(Marshal.GetLastPInvokeError(), "Diagnostic clipboard lock failed");
    }

    public static async Task<ClipboardSmokeState> CaptureAsync(nint owner)
    {
        var state = new ClipboardSmokeState(owner);
        await OpenAsync(owner);
        try
        {
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                if (format is 0x80 or >= 0x200 and <= 0x3ff)
                    throw new InvalidOperationException("Clipboard uses an owner/private format; diagnostic will not overwrite it.");
                var original = GetClipboardData(format);
                var copy = original == 0 ? 0 : OleDuplicateData(original, checked((ushort)format), 2);
                if (copy == 0) throw new InvalidOperationException($"Cannot preserve clipboard format {format}; diagnostic has not overwritten it.");
                state.saved.Add((format, copy));
            }
            if (Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Clipboard format enumeration failed before overwrite");
            return state;
        }
        catch { state.Dispose(); throw; }
        finally { CloseClipboard(); }
    }

    public async Task<string?> ReadTextAsync()
    {
        await OpenAsync(owner);
        try
        {
            var handle = GetClipboardData(13);
            if (handle == 0) return null;
            var pointer = GlobalLock(handle);
            if (pointer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var capacity = checked((int)(GlobalSize(handle) / 2));
                var value = Marshal.PtrToStringUni(pointer, capacity) ?? "";
                var end = value.IndexOf('\0');
                return end >= 0 ? value[..end] : value;
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    public async Task RestoreAsync()
    {
        await OpenAsync(owner);
        try
        {
            if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastPInvokeError());
            for (var index = 0; index < saved.Count; index++)
            {
                var entry = saved[index];
                if (SetClipboardData(entry.Format, entry.Handle) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                saved[index] = (entry.Format, 0); // Windows owns this handle now.
            }
        }
        finally { CloseClipboard(); }
    }

    public void Dispose()
    {
        foreach (var entry in saved.Where(entry => entry.Handle != 0))
        {
            if (entry.Format is 2 or 9 or 0x82) DeleteObject(entry.Handle);
            else if (entry.Format is 14 or 0x8e) DeleteEnhMetaFile(entry.Handle);
            else if (entry.Format is 3 or 0x83)
            {
                var pointer = GlobalLock(entry.Handle);
                if (pointer != 0) { DeleteMetaFile(Marshal.ReadIntPtr(pointer, 16)); GlobalUnlock(entry.Handle); }
                GlobalFree(entry.Handle);
            }
            else GlobalFree(entry.Handle);
        }
        saved.Clear();
    }

    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll", SetLastError = true)] private static partial uint EnumClipboardFormats(uint format);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint GetClipboardData(uint format);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("ole32.dll", SetLastError = true)] private static partial nint OleDuplicateData(nint source, ushort format, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nuint GlobalSize(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalFree(nint memory);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint handle);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteEnhMetaFile(nint handle);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteMetaFile(nint handle);
}
