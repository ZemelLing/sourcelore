using SourceLore.Core.Workspaces;

namespace SourceLore.Infrastructure.Lore;

internal static class TextPreview
{
    internal static string WorkingPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidOperationException("请选择工作目录内的相对文件路径。");
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("文件路径不在当前工作目录中。");
        return fullPath;
    }
    internal static async Task<byte[]> ReadBytesAsync(string fullPath)
    {
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, useAsync: true);
        if (stream.Length > TextContent.MaxBytes) throw new InvalidDataException("文件超过 8 MiB 预览上限，请使用外部工具查看。");
        using var bytes = new MemoryStream((int)stream.Length);
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) break;
            if (bytes.Length + count > TextContent.MaxBytes) throw new InvalidDataException("文件超过 8 MiB 预览上限，请使用外部工具查看。");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
}
