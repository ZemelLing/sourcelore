using LoreVcs.Types.Args;
using LoreVcs.Types.Enums;
using LoreVcs.Types.Events;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;
using SourceLore.Infrastructure.Lore.Diagnostics;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Infrastructure.Lore;

public sealed partial class LoreWorkspaceBackend : ISynchronizationBackend
{
    private static LoreGlobalArgs OnlineGlobals(string root) => new() { RepositoryPath = root, WorkingDirectory = root, Cache = true, EventIntervalMs = 100 };
    public Task<RemoteTarget> ReadTargetAsync(WorkspaceIdentity workspace) => Run(workspace, () => ReadTargetCoreAsync(workspace));
    private static async Task<RemoteTarget> ReadTargetCoreAsync(WorkspaceIdentity workspace)
    {
        using var globals = Globals(workspace.RootPath);
        using var config = new LoreRepositoryConfigGetArgs { Key = "remote_url" };
        string? address = null; var failed = false;
        await Sdk.RepositoryConfigGet(globals, config).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.REPOSITORY_CONFIG_GET) address = evt.GetData<LoreRepositoryConfigGetEventDataFFI>().Value;
        }).WaitAsync().ConfigureAwait(false);
        if (failed || string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("lore" or "lores" or "grpc" or "grpcs" or "urc" or "urcs"))
            throw new InvalidOperationException("工作目录未配置有效 Lore 远端；请通过克隆连接仓库，不能猜测推送目标。");
        using var status = new LoreRepositoryStatusArgs { RevisionOnly = true };
        RemoteTarget? target = null;
        await Sdk.RepositoryStatus(globals, status).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.REPOSITORY_STATUS_REVISION)
            {
                var data = evt.GetData<LoreRepositoryStatusRevisionEventDataFFI>();
                target = new(address, SafeDiagnostics.Redact(address), Convert.ToHexString(data.Branch.Data), data.BranchName, Convert.ToHexString(data.Revision.Data), Convert.ToHexString(data.Repository.Data), Convert.ToHexString(data.RevisionLocal.Data));
            }
        }).WaitAsync().ConfigureAwait(false);
        return !failed && target is not null ? target : throw new InvalidOperationException("当前分支目标核对失败。");
    }
    public Task<RemoteAnchor> VerifyRemoteAsync(WorkspaceIdentity workspace) => Run(workspace, () => VerifyRemoteCoreAsync(workspace));
    private static async Task<RemoteAnchor> VerifyRemoteCoreAsync(WorkspaceIdentity workspace)
    {
        var target = await ReadTargetCoreAsync(workspace).ConfigureAwait(false);
        using var globals = OnlineGlobals(workspace.RootPath);
        using var args = new LoreRepositoryStatusArgs { RevisionOnly = true };
        RemoteAnchor? anchor = null; var failed = false;
        await Sdk.RepositoryStatus(globals, args).Callback((evt, _) =>
        {
            if (evt.Tag == LoreEventTag.ERROR) failed = true;
            if (evt.Tag == LoreEventTag.REPOSITORY_STATUS_REVISION)
            {
                var data = evt.GetData<LoreRepositoryStatusRevisionEventDataFFI>();
                if (data.RemoteAvailable && data.RemoteAuthorized && Convert.ToHexString(data.Branch.Data) == target.BranchId)
                    anchor = new(target with { LocalRevision = Convert.ToHexString(data.Revision.Data), LocalHeadRevision = Convert.ToHexString(data.RevisionLocal.Data) }, Convert.ToHexString(data.RevisionRemote.Data), data.RemoteBranchExist, data.IsLocalAhead, data.IsRemoteAhead, DateTimeOffset.UtcNow);
            }
        }).WaitAsync().ConfigureAwait(false);
        if (failed || anchor is null) throw new InvalidOperationException("远端状态未得到授权核实；可能断网、未登录或权限不足。未推送数量未知，保留上次锚点。");
        return anchor;
    }
    private static async Task CheckTargetAsync(WorkspaceIdentity workspace, RemoteTarget expected)
    {
        var actual = await ReadTargetCoreAsync(workspace).ConfigureAwait(false);
        if (actual.Address != expected.Address || actual.RepositoryId != expected.RepositoryId || actual.BranchId != expected.BranchId || actual.LocalRevision != expected.LocalRevision || actual.PushRevision != expected.PushRevision)
            throw new InvalidOperationException("工作目录远端、分支或本地版本已变化，请重新核对目标后确认。");
    }
    public Task<RemoteAnchor> PushAsync(WorkspaceIdentity workspace, RemoteTarget target, IProgress<OperationProgress>? progress = null) => Run(workspace, async () =>
    {
        await CheckTargetAsync(workspace, target).ConfigureAwait(false);
        var status = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (status.HasConflicts || status.HasMerge) throw new InvalidOperationException("存在未完成合并，请先完成或中止冲突流程。");
        using var globals = OnlineGlobals(workspace.RootPath);
        using var args = new LoreBranchPushArgs { Branch = target.BranchName, FastForwardMerge = false };
        var failed = false; var success = false;
        var updates = new CoalescedProgress(progress, TimeSpan.FromMilliseconds(100));
        try
        {
            await Sdk.BranchPush(globals, args).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.BRANCH_PUSH_FRAGMENT_PROGRESS) updates.Report(new("正在上传已有本地提交的内容…"));
            }).WaitAsync().ConfigureAwait(false);
            if (failed) throw new InvalidOperationException("推送包含后端错误；请核对远端，不自动强制推送或重放。");
            var anchor = await VerifyRemoteCoreAsync(workspace).ConfigureAwait(false);
            if (anchor.LocalAhead) throw new InvalidOperationException("推送后仍有未推送状态，结果待核实，请明确刷新远端。");
            success = true;
            return anchor;
        }
        finally { await updates.CompleteAsync(new(success ? "推送已核实完成" : "推送未确认完成", true)).ConfigureAwait(false); }
    });
    public Task<WorkspaceSnapshot> SyncAsync(WorkspaceIdentity workspace, RemoteAnchor target, IProgress<OperationProgress>? progress = null) => Run(workspace, async () =>
    {
        await CheckTargetAsync(workspace, target.Target).ConfigureAwait(false);
        if (!target.BranchExists || !RevisionHistoryPager.IsRevision(target.Revision)) throw new InvalidOperationException("远端分支尚不存在，没有可同步的已核实版本。");
        var status = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
        if (status.Files.Count > 0 || status.HasConflicts || status.HasMerge)
            throw new InvalidOperationException("同步前请处理本地修改、暂存或未完成合并；当前内容保持原样。");
        await RequireTrackedWorkingFilesAsync(workspace, status.Revision).ConfigureAwait(false);
        await CheckIncomingPathsAsync(workspace, status.Revision, target.Revision).ConfigureAwait(false);
        // 0.10's explicit Revision is a historical checkout: it never marks the target as
        // remote and leaves branch LATEST unchanged. Normal sync must resolve the branch.
        using var globals = OnlineGlobals(workspace.RootPath);
        using var args = new LoreRevisionSyncArgs { Reset = false, ForwardChanges = false };
        var failed = false; var success = false;
        var updates = new CoalescedProgress(progress, TimeSpan.FromMilliseconds(100));
        try
        {
            await Sdk.RevisionSync(globals, args).Callback((evt, _) =>
            {
                if (evt.Tag == LoreEventTag.ERROR) failed = true;
                if (evt.Tag == LoreEventTag.REVISION_SYNC_PROGRESS) updates.Report(new("正在取得所选远端版本并保留本地提交…"));
            }).WaitAsync().ConfigureAwait(false);
            if (failed) throw new InvalidOperationException("同步包含后端错误，请核对工作目录和冲突状态。");
            var actual = await ReadStatusAsync(workspace, true).ConfigureAwait(false);
            success = true;
            return actual;
        }
        finally { await updates.CompleteAsync(new(success ? "同步调用已完成，请核对冲突状态" : "同步未确认完成", true)).ConfigureAwait(false); }
    });
}
