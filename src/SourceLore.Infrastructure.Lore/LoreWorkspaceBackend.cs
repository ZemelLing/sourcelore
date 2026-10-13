using System.Collections.Concurrent;
using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend(LoreRuntime runtime) : IWorkspaceBackend, IHistoryBackend, IRemoteWorkspaceBackend, IBranchBackend, IDirtyWorkspaceBackend, IAuthenticationBackend, ITextComparisonBackend
{
    private readonly ConcurrentDictionary<string, WorkspaceScheduler> schedulers = new(StringComparer.OrdinalIgnoreCase);
    private readonly WorkspaceScheduler authentication = new();

    public Task LoginAsync(string remoteUrl, IProgress<AuthenticationLink> link) => authentication.RunAsync(() => runtime.ExecuteAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) throw new ArgumentException("请填写 Lore 远端地址。");
        using var globals = new LoreGlobalArgs();
        using var args = new LoreAuthLoginInteractiveArgs { RemoteUrl = remoteUrl, NoBrowser = true };
        await Sdk.AuthLoginInteractive(globals, args).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.AUTH_URL) link.Report(new(evt.GetData<LoreAuthUrlEventDataFFI>().Url));
        }).WaitAsync().ConfigureAwait(false);
        return true;
    }));

    private Task<T> Run<T>(WorkspaceIdentity workspace, Func<Task<T>> operation) =>
        schedulers.GetOrAdd(workspace.InstanceId, _ => new()).RunAsync(() => runtime.ExecuteAsync(operation));

    private Task Run(WorkspaceIdentity workspace, Func<Task> operation) =>
        Run(workspace, async () => { await operation().ConfigureAwait(false); return true; });

    private static LoreGlobalArgs Globals(string path) => new()
    {
        RepositoryPath = path,
        WorkingDirectory = path,
        Offline = true,
        Local = true,
        EventIntervalMs = 100,
    };

    public Task<WorkspaceIdentity> CloneAsync(string remoteUrl, string destination, IProgress<OperationProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) throw new ArgumentException("请填写 Lore 远端仓库地址。");
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("请填写克隆目标目录。");
        var root = Path.GetFullPath(destination);
        return schedulers.GetOrAdd("clone:" + root, _ => new()).RunAsync(async () =>
        {
            if (File.Exists(root) || (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()))
                throw new InvalidOperationException("克隆目标必须不存在或为空，已有内容保留。");
            Directory.CreateDirectory(root);
            var updates = new CoalescedProgress(progress, TimeSpan.FromMilliseconds(100));
            var success = false;
            try
            {
                await runtime.ExecuteAsync(async () =>
                {
                    using var globals = new LoreGlobalArgs { RepositoryPath = root, WorkingDirectory = root, Cache = true, EventIntervalMs = 100 };
                    using var args = new LoreRepositoryCloneArgs { RepositoryUrl = remoteUrl };
                    await Sdk.RepositoryClone(globals, args).Callback((evt, _) =>
                    {
                        if (evt.Tag == LoreEventTag.REPOSITORY_CLONE_PROGRESS) updates.Report(new("正在克隆仓库内容…"));
                    }).WaitAsync().ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                var workspace = await OpenAsync(root).ConfigureAwait(false);
                await StatusAsync(workspace, true).ConfigureAwait(false);
                success = true;
                return workspace;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"克隆未完成，残留目录保留在 {root}。检查目录后明确重试：" + Diagnostics.SafeDiagnostics.Describe(error), error);
            }
            finally { await updates.CompleteAsync(new(success ? "克隆完成" : "克隆失败", true)).ConfigureAwait(false); }
        });
    }

    public Task<WorkspaceIdentity> OpenAsync(string path) => runtime.ExecuteAsync(async () =>
    {
        _ = runtime.Version;
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("工作目录不存在。");
        // The SDK expects a working-tree root. Locate a marker, then let Lore validate its contents.
        // No internal store files are read or interpreted here.
        for (var candidate = new DirectoryInfo(fullPath); candidate is not null; candidate = candidate.Parent)
        {
            if (!Directory.Exists(Path.Combine(candidate.FullName, ".lore")) &&
                !Directory.Exists(Path.Combine(candidate.FullName, ".urc"))) continue;
            fullPath = candidate.FullName;
            break;
        }
        using var globals = Globals(fullPath);
        using var args = new LoreRepositoryInfoArgs();
        string? instanceId = null;
        string name = "", remote = "";
        try
        {
            await Sdk.RepositoryInfo(globals, args).Callback((evt, _) =>
            {
                if (evt.Tag != LoreEventTag.REPOSITORY_DATA) return;
                var data = evt.GetData<LoreRepositoryDataEventDataFFI>();
                instanceId = Convert.ToHexString(data.InstanceId.Data);
                name = data.Name;
                remote = data.RemoteUrl;
            }).WaitAsync().ConfigureAwait(false);
        }
        catch (LoreVcs.LoreError error) when (error.ReturnCode == 80)
        {
            // 0.10.0 clone does not persist repository description metadata. Resolve the
            // working instance with the public SDK instead; subsequent status validates it.
            name = new DirectoryInfo(fullPath).Name;
        }
        string? root = null;
        using var listArgs = new LoreRepositoryInstanceListArgs();
        await Sdk.RepositoryInstanceList(globals, listArgs).Callback((evt, _) =>
        {
            if (evt.Tag != LoreEventTag.REPOSITORY_INSTANCE) return;
            var data = evt.GetData<LoreRepositoryInstanceEventDataFFI>();
            if (!data.Stale && (instanceId is not null ? Convert.ToHexString(data.InstanceId.Data) == instanceId :
                    string.Equals(Path.GetFullPath(data.Path).TrimEnd(Path.DirectorySeparatorChar), fullPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            {
                root = data.Path;
                instanceId = Convert.ToHexString(data.InstanceId.Data);
            }
        }).WaitAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("Lore 未返回当前工作目录的根路径。");
        if (instanceId is null) throw new InvalidOperationException("Lore 未返回有效工作目录实例。");
        return new WorkspaceIdentity(instanceId, Path.GetFullPath(root), name, remote);
    });

    public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Run(workspace, () => ReadStatusAsync(workspace, scan, progress));

    private async Task<WorkspaceSnapshot> ReadStatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null)
    {
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreRepositoryStatusArgs { Staged = true, Scan = scan, CheckDirty = true };
        var files = new ConcurrentQueue<FileChange>();
        var updates = new CoalescedProgress(progress, TimeSpan.FromMilliseconds(100));
        var errors = new ConcurrentQueue<string>();
        var count = 0;
        var succeeded = false;
        string? revision = null;
        string branch = "", merged = "", staged = "";
        try
        {
        await Sdk.RepositoryStatus(globals, args).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR)
            {
                var error = evt.GetData<LoreErrorEventDataFFI>();
                errors.Enqueue(error.ErrorInner);
            }
            if (evt.Tag == LoreEventTag.REPOSITORY_STATUS_REVISION)
            {
                var data = evt.GetData<LoreRepositoryStatusRevisionEventDataFFI>();
                revision = Convert.ToHexString(data.Revision.Data);
                branch = data.BranchName;
                merged = Convert.ToHexString(data.RevisionMerged.Data);
                staged = Convert.ToHexString(data.RevisionStaged.Data);
            }
            else if (evt.Tag == LoreEventTag.REPOSITORY_STATUS_FILE)
            {
                var data = evt.GetData<LoreRepositoryStatusFileEventDataFFI>();
                files.Enqueue(new FileChange(data.Path, data.Size,
                    data.Action == LoreFileAction.KEEP ? "MODIFY" : data.Action.ToString(),
                    data.Type == LoreNodeType.DIRECTORY, data.FlagDirty, data.FlagStaged,
                    data.FlagConflict, data.FlagConflictUnresolved, data.FromPath));
                updates.Report(new($"已发现 {Interlocked.Increment(ref count)} 项变更；仍在核对…"));
            }
        }).WaitAsync().ConfigureAwait(false);
        if (!errors.IsEmpty) throw new InvalidOperationException("状态核对包含错误，不能确认工作区完整：" + string.Join("；", errors));
        if (revision is null) throw new InvalidOperationException("Lore 状态查询缺少版本信息，状态需要重新核对。");
        succeeded = true;
        return new WorkspaceSnapshot(revision, branch, merged, files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray(), DateTimeOffset.UtcNow, staged);
        }
        finally { await updates.CompleteAsync(new(succeeded ? "状态核对完成" : "状态核对失败", true)).ConfigureAwait(false); }
    }

    public Task<IReadOnlyList<BranchEntry>> BranchesAsync(WorkspaceIdentity workspace) => Run<IReadOnlyList<BranchEntry>>(workspace, async () =>
    {
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreBranchListArgs();
        var branches = new ConcurrentQueue<BranchEntry>();
        var errors = new ConcurrentQueue<string>();
        await Sdk.BranchList(globals, args).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) errors.Enqueue(evt.GetData<LoreErrorEventDataFFI>().ErrorInner);
            if (evt.Tag != LoreEventTag.BRANCH_LIST_ENTRY) return;
            var data = evt.GetData<LoreBranchListEntryEventDataFFI>();
            branches.Enqueue(new(Convert.ToHexString(data.Id.Data), data.Name, Convert.ToHexString(data.Latest.Data), data.IsCurrent));
        }).WaitAsync().ConfigureAwait(false);
        if (!errors.IsEmpty) throw new InvalidOperationException("分支核对失败：" + string.Join("；", errors));
        return branches.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    });

    private async Task RequireCleanBranchOperationAsync(WorkspaceIdentity workspace)
    {
        var status = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (status.HasConflicts || status.HasMerge)
            throw new InvalidOperationException("存在未完成合并，请先处理冲突流程。");
        if (status.Files.Count > 0)
            throw new InvalidOperationException("目录有本地变更或暂存选择；请先提交或自行处理，再切换分支。文件保留，未执行重置。");
    }

    public Task CreateBranchAsync(WorkspaceIdentity workspace, string name) => Run(workspace, async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请填写新分支名称。");
        await RequireCleanBranchOperationAsync(workspace).ConfigureAwait(false);
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreBranchCreateArgs { Branch = name };
        await Sdk.BranchCreate(globals, args).WaitAsync().ConfigureAwait(false);
    });

    public Task SwitchBranchAsync(WorkspaceIdentity workspace, string name) => Run(workspace, async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请选择已有分支。");
        await RequireCleanBranchOperationAsync(workspace).ConfigureAwait(false);
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreBranchSwitchArgs { Branch = name, Reset = false };
        await Sdk.BranchSwitch(globals, args).WaitAsync().ConfigureAwait(false);
    });

    public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => Run(workspace, async () =>
    {
        if (paths.Count == 0) return;
        using var globals = Globals(workspace.RootPath);
        var errors = new ConcurrentQueue<string>();
        void Observe(LoreEventFFI evt)
        {
            if (evt.Tag == LoreEventTag.ERROR) errors.Enqueue(evt.GetData<LoreErrorEventDataFFI>().ErrorInner);
        }
        if (staged)
        {
            using var args = new LoreFileStageArgs { Paths = paths.ToArray() };
            await Sdk.FileStage(globals, args).Callback((evt, _) => Observe(evt)).WaitAsync().ConfigureAwait(false);
        }
        else
        {
            using var args = new LoreFileUnstageArgs { Paths = paths.ToArray() };
            await Sdk.FileUnstage(globals, args).Callback((evt, _) => Observe(evt)).WaitAsync().ConfigureAwait(false);
        }
        if (!errors.IsEmpty) throw new InvalidOperationException("部分路径的暂存操作失败，请核对实际状态：" + string.Join("；", errors));
        var actual = await ReadStatusAsync(workspace, false).ConfigureAwait(false);
        var incomplete = paths.Where(path => staged
            ? !actual.Files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase) && file.Staged)
            : actual.Files.Any(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase) && file.Staged)).ToArray();
        if (incomplete.Length > 0) throw new InvalidOperationException("部分路径未达到请求的暂存状态；请核对实际结果：" + string.Join("、", incomplete));
    });

    public Task UpdateDirtyAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths) => Run(workspace, async () =>
    {
        if (paths.Count == 0) return;
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreFileDirtyArgs { Paths = paths.ToArray() };
        await Sdk.FileDirty(globals, args).WaitAsync().ConfigureAwait(false);
    });

    public Task CommitAsync(WorkspaceIdentity workspace, string message) => Run(workspace, async () =>
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("提交说明不能为空。", nameof(message));
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreRevisionCommitArgs { Message = message };
        await Sdk.RevisionCommit(globals, args).WaitAsync().ConfigureAwait(false);
    });

    public Task<RevisionEntry> RevisionAsync(WorkspaceIdentity workspace, string revision) => Run(workspace, async () =>
    {
        using var globals = Globals(workspace.RootPath);
        using var args = new LoreRevisionInfoArgs { Revision = revision, Metadata = true };
        string? actual = null;
        ulong number = 0;
        string[] parents = [];
        var metadata = new ConcurrentDictionary<string, string>();
        ulong? timestamp = null;
        var errors = new ConcurrentQueue<string>();
        await Sdk.RevisionInfo(globals, args).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) errors.Enqueue(evt.GetData<LoreErrorEventDataFFI>().ErrorInner);
            if (evt.Tag == LoreEventTag.REVISION_INFO)
            {
                var data = evt.GetData<LoreRevisionInfoEventDataFFI>();
                actual = Convert.ToHexString(data.Revision.Data);
                number = data.RevisionNumber;
                parents = data.Parent.Select(Convert.ToHexString).Where(RevisionHistoryPager.IsRevision).ToArray();
            }
            if (evt.Tag == LoreEventTag.METADATA)
            {
                var data = evt.GetData<LoreMetadataEventDataFFI>();
                var value = data.Value;
                if (value.Tag == LoreMetadataType.STRING) metadata[data.Key] = value.String;
                if (data.Key == "timestamp" && value.Tag == LoreMetadataType.NUMERIC) timestamp = value.Numeric;
            }
        }).WaitAsync().ConfigureAwait(false);
        if (actual is null || !errors.IsEmpty) throw new InvalidOperationException("版本 " + revision + " 的本地历史数据不可用；已加载列表保留。" + string.Join("；", errors));
        DateTimeOffset? date = timestamp is { } milliseconds && milliseconds <= 253402300799999UL
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds) : null;
        return new RevisionEntry(actual, number, parents, metadata.GetValueOrDefault("message", ""),
            metadata.GetValueOrDefault("created-by", ""), date);
    });

    public async Task<string> DiffAsync(WorkspaceIdentity workspace, string path)
    {
        var status = await StatusAsync(workspace, false).ConfigureAwait(false);
        return (await CompareAsync(workspace, path, status.Revision, null).ConfigureAwait(false)).Unified;
    }
}
