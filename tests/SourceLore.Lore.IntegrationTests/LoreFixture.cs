using LoreVcs.Types.Args;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Lore.IntegrationTests;

internal sealed class LoreFixture : IAsyncDisposable
{
    private static readonly string TestRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SourceLore.Tests");
    public string Path { get; } = System.IO.Path.Combine(TestRoot, "工作 目录 " + Guid.NewGuid().ToString("N"));
    public LoreWorkspaceBackend Backend { get; }
    public WorkspaceIdentity Workspace { get; private set; } = null!;

    public LoreFixture(LoreRuntime runtime) => Backend = new LoreWorkspaceBackend(runtime);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path);
        using var globals = new LoreGlobalArgs { RepositoryPath = Path, WorkingDirectory = Path, Offline = true };
        using var args = new LoreRepositoryCreateArgs { RepositoryUrl = "SourceLoreTest" + Guid.NewGuid().ToString("N") };
        await Sdk.RepositoryCreate(globals, args).WaitAsync();
        Workspace = await Backend.OpenAsync(Path);
    }

    public Task WriteAsync(string relative, string content) => File.WriteAllTextAsync(System.IO.Path.Combine(Path, relative), content);

    public async ValueTask DisposeAsync()
    {
        using var globals = new LoreGlobalArgs { RepositoryPath = Path, Offline = true };
        using var args = new LoreRepositoryReleaseArgs();
        await Sdk.RepositoryRelease(globals, args).WaitAsync();
        var resolved = System.IO.Path.GetFullPath(Path);
        var prefix = System.IO.Path.GetFullPath(TestRoot) + System.IO.Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(resolved).StartsWith("工作 目录 ", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing fixture cleanup outside the isolated test root");
        Directory.Delete(resolved, recursive: true);
    }
}
