using System.Text.Json;
using SourceLore.Core.Workspaces;

namespace SourceLore.Infrastructure.Lore.Persistence;

public sealed class RecentWorkspaceStore(string path) : IRecentWorkspaceStore
{
    public async Task<IReadOnlyList<RecentWorkspace>> ReadAsync()
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        if (stream.Length > 128 * 1024) throw new InvalidDataException("最近目录记录超出预期大小。");
        return await JsonSerializer.DeserializeAsync(stream, PersistenceJsonContext.Default.RecentWorkspaceArray).ConfigureAwait(false) ?? [];
    }

    public async Task WriteAsync(IReadOnlyList<RecentWorkspace> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, entries.Take(30).ToArray(), PersistenceJsonContext.Default.RecentWorkspaceArray).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}
