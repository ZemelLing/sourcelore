using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.Infrastructure.Lore.Persistence;

public sealed class PendingOperationStore(string directory) : IPendingOperationStore
{
    private string FilePath(string instanceId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId))) + ".json");

    public async Task<PendingOperation?> ReadAsync(WorkspaceIdentity workspace)
    {
        var path = FilePath(workspace.InstanceId);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        if (stream.Length > 32 * 1024) throw new InvalidDataException("操作恢复记录超出预期大小。");
        return await JsonSerializer.DeserializeAsync(stream, PersistenceJsonContext.Default.PendingOperation).ConfigureAwait(false)
            ?? throw new InvalidDataException("操作恢复记录无效。");
    }

    public async Task WriteAsync(PendingOperation operation)
    {
        Directory.CreateDirectory(directory);
        var path = FilePath(operation.InstanceId);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, operation, PersistenceJsonContext.Default.PendingOperation).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    public Task ClearAsync(WorkspaceIdentity workspace)
    {
        File.Delete(FilePath(workspace.InstanceId));
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(PendingOperation))]
[JsonSerializable(typeof(RecentWorkspace[]))]
internal partial class PersistenceJsonContext : JsonSerializerContext;
