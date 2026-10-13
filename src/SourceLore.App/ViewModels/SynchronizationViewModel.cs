using System.Collections.ObjectModel;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private RemoteTarget? currentRemoteTarget, plannedTarget;
    private RemoteAnchor? plannedAnchor;
    private WorkspaceIdentity? planOwner;
    private OperationKind? planKind;
    private string plannedMergedRevision = "", remotePlanLabel = "", syncTargetLabel = "尚未核对远端目标。", backupPath = "";
    private FileChange? selectedConflict;
    private readonly Dictionary<string, RemoteAnchor> remoteAnchors = [];
    private readonly HashSet<string> failedRemoteChecks = [];
    public AsyncCommand VerifyRemote { get; }
    public AsyncCommand PreparePush { get; }
    public AsyncCommand PrepareSync { get; }
    public AsyncCommand PrepareAbort { get; }
    public AsyncCommand ConfirmRemoteOperation { get; }
    public AsyncCommand CancelRemotePlan { get; }
    public AsyncCommand MarkConflictResolved { get; }
    public AsyncCommand OpenConflictFile { get; }
    public AsyncCommand ContinueMerge { get; }
    public ObservableCollection<FileChange> Conflicts { get; } = [];
    public bool HasMerge => snapshot?.HasMerge == true;
    public bool HasRemotePlan => planKind is not null && planOwner == workspace;
    public string RemotePlanLabel { get => remotePlanLabel; private set => Set(ref remotePlanLabel, value); }
    public string SyncTargetLabel { get => syncTargetLabel; private set => Set(ref syncTargetLabel, value); }
    public string BackupPath { get => backupPath; private set => Set(ref backupPath, value); }
    public string MergeStateLabel => HasMerge ? $"未完成合并：{Conflicts.Count(file => file.Unresolved)} 个未解决冲突。外部编辑后显式标记解决；继续操作将本地提交当前所有已暂存文件。" : "当前没有未完成合并。";
    public FileChange? SelectedConflict
    {
        get => selectedConflict;
        set { if (Set(ref selectedConflict, value) && value is not null) SelectedFile = value; RefreshCommands(); }
    }
    private string AnchorKey(RemoteTarget target) => workspace!.InstanceId + ":" + target.RepositoryId + ":" + target.BranchId;
    public string RemoteStateLabel
    {
        get
        {
            if (workspace is null || currentRemoteTarget is null) return "远端锚点未知，未推送数量未知。";
            var key = AnchorKey(currentRemoteTarget);
            if (!remoteAnchors.TryGetValue(key, out var anchor)) return $"{currentRemoteTarget.BranchName}：尚未核实远端，未推送数量未知。";
            var prefix = $"{anchor.Target.BranchName} 最后核实：{anchor.VerifiedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}；远端版本 {anchor.Revision}。";
            if (failedRemoteChecks.Contains(key)) return prefix + " 连接核实失败，信息过期；未推送数量未知。";
            if (snapshot?.Revision != anchor.Target.LocalRevision || currentRemoteTarget.PushRevision != anchor.Target.PushRevision) return prefix + " 核实后本地版本已变化；未推送数量未知。";
            if (!anchor.BranchExists) return prefix + " 远端分支尚不存在；本地提交未发布，数量未知。";
            if (anchor.LocalAhead) return prefix + " 有未推送本地提交，数量未知；" + (anchor.RemoteAhead ? "远端也已前进，请同步。" : "可明确推送。");
            if (anchor.Revision != anchor.Target.PushRevision) return prefix + " 本地与远端版本不同；未推送数量未知。" + (anchor.RemoteAhead ? "远端有更新。" : "请核对历史。");
            return prefix + " 基于该锚点未推送数量为 0；" + (anchor.RemoteAhead ? "远端有更新。" : "不代表实时远端状态。");
        }
    }
    private async Task RefreshRemoteTargetAsync()
    {
        if (workspace is null || backend is not ISynchronizationBackend sync) { currentRemoteTarget = null; Raise(nameof(RemoteStateLabel)); return; }
        try
        {
            currentRemoteTarget = await sync.ReadTargetAsync(workspace);
            SyncTargetLabel = $"工作目录：{workspace.RootPath}\n远端：{currentRemoteTarget.DisplayAddress}\n仓库：{currentRemoteTarget.RepositoryId}；分支：{currentRemoteTarget.BranchName}";
            if (plannedTarget is not null && (currentRemoteTarget.Address != plannedTarget.Address || currentRemoteTarget.BranchId != plannedTarget.BranchId || currentRemoteTarget.LocalRevision != plannedTarget.LocalRevision || currentRemoteTarget.PushRevision != plannedTarget.PushRevision)) ClearRemotePlan();
        }
        catch (Exception error) { currentRemoteTarget = null; SyncTargetLabel = "远端目标未核实：" + describe(error); ClearRemotePlan(); }
        Raise(nameof(RemoteStateLabel));
    }
    private void StoreAnchor(RemoteAnchor anchor)
    {
        currentRemoteTarget = anchor.Target;
        remoteAnchors[AnchorKey(anchor.Target)] = anchor;
        failedRemoteChecks.Remove(AnchorKey(anchor.Target));
        Raise(nameof(RemoteStateLabel));
    }
    private async Task VerifyRemoteStateAsync()
    {
        try { StoreAnchor(await ((ISynchronizationBackend)backend!).VerifyRemoteAsync(workspace!)); Status = "远端锚点已核实；未自动执行推送或同步。"; }
        catch
        {
            if (currentRemoteTarget is not null) failedRemoteChecks.Add(AnchorKey(currentRemoteTarget));
            Raise(nameof(RemoteStateLabel)); throw;
        }
    }
    private void ClearRemotePlan()
    {
        planKind = null; plannedTarget = null; plannedAnchor = null; planOwner = null;
        RemotePlanLabel = ""; Raise(nameof(HasRemotePlan)); RefreshCommands();
    }
    private async Task PrepareRemoteOperationAsync(OperationKind kind)
    {
        EnsureMutationAllowed(); ClearRemotePlan();
        await RefreshAsync(true);
        if (kind == OperationKind.Abort)
        {
            if (!HasMerge) throw new InvalidOperationException("没有可中止的未完成合并。");
            plannedMergedRevision = snapshot!.MergedRevision;
            plannedTarget = new("", "", "", snapshot.BranchName, snapshot.Revision);
            RemotePlanLabel = $"中止 {workspace!.RootPath} 的当前合并，将恢复本地版本 {snapshot.Revision} 对应的工作文件，人工合并编辑可能被覆盖。本地已提交版本保留。执行前备份当前变更文件（每文件最多 8 MiB，总计 128 MiB），备份失败则不调用中止；不保证恢复所有后端外部数据。确认后执行。";
        }
        else
        {
            if (HasMerge || snapshot!.HasConflicts) throw new InvalidOperationException("请先处理未完成合并，不能普通推送或同步。");
            if (kind == OperationKind.Sync && snapshot.Files.Count > 0) throw new InvalidOperationException($"存在 {snapshot.Files.Count} 项本地修改或未跟踪内容，请先处理。未执行同步，文件保持原样。");
            if (kind == OperationKind.Sync)
            {
                plannedAnchor = await ((ISynchronizationBackend)backend!).VerifyRemoteAsync(workspace!);
                StoreAnchor(plannedAnchor); plannedTarget = plannedAnchor.Target;
                if (!plannedAnchor.BranchExists) throw new InvalidOperationException("远端分支尚不存在，没有可同步的目标。");
            }
            else plannedTarget = await ((ISynchronizationBackend)backend!).ReadTargetAsync(workspace!);
            RemotePlanLabel = $"工作目录：{workspace!.RootPath}\n远端：{plannedTarget.DisplayAddress}；仓库：{plannedTarget.RepositoryId}；分支：{plannedTarget.BranchName}\n本地版本：{plannedTarget.LocalRevision}\n" +
                (kind == OperationKind.Push ? $"将推送分支本地最新版本：{plannedTarget.PushRevision}；{snapshot.Files.Count} 项未提交变更不会自动提交，不强制覆盖远端。" : $"最后核实远端版本：{plannedAnchor!.Revision}。同步该分支可能读取其后更新的远端版本。本地未推送提交保留；存在未跟踪/忽略的物理文件或覆盖风险则拒绝，分歧可能进入冲突流程。不会自动提交工作文件或强制重置。");
        }
        planOwner = workspace; planKind = kind; Raise(nameof(HasRemotePlan)); RefreshCommands();
    }
    private async Task ConfirmRemoteOperationAsync()
    {
        EnsureMutationAllowed();
        var kind = planKind; var owner = planOwner; var target = plannedTarget; var anchor = plannedAnchor; var merged = plannedMergedRevision;
        if (owner != workspace || kind is null || target is null) throw new InvalidOperationException("请先核对操作目标。");
        ClearRemotePlan();
        var sync = (ISynchronizationBackend)backend!;
        var result = await TrackAsync(kind.Value, async () =>
        {
            if (kind == OperationKind.Push) StoreAnchor(await sync.PushAsync(owner!, target, CreateProgress()));
            else if (kind == OperationKind.Sync) await sync.SyncAsync(owner!, anchor!, CreateProgress());
            else if (kind == OperationKind.Abort) BackupPath = "中止前文件备份：" + (await sync.AbortMergeAsync(owner!, target.LocalRevision, merged)).BackupPath;
        });
        await RefreshAsync(true);
        if (!result.Succeeded)
        {
            if (currentRemoteTarget is not null) failedRemoteChecks.Add(AnchorKey(currentRemoteTarget));
            Feedback = Status = "操作结果待核实，未自动重试。远端前进时请明确核对并同步；认证失效时登录后再明确重试。" + result.Error;
        }
        else Status = kind == OperationKind.Sync && HasMerge ? "同步进入冲突流程，尚未完成合并。请外部编辑、标记解决并继续。" : "操作已完成并核对本地状态。";
        Raise(nameof(RemoteStateLabel));
    }
    private async Task MarkConflictResolvedAsync()
    {
        EnsureMutationAllowed(); var path = SelectedConflict!.Path;
        var result = await TrackAsync(OperationKind.Resolve, async () => await ((ISynchronizationBackend)backend!).ResolveAsync(workspace!, path));
        await RefreshAsync(true);
        if (!result.Succeeded) Feedback = Status = "标记解决的结果待核实，请检查实际冲突状态。" + result.Error;
    }
    private async Task ContinueMergeOperationAsync()
    {
        EnsureMutationAllowed();
        if (string.IsNullOrWhiteSpace(Message)) throw new InvalidOperationException("请填写合并提交说明。");
        var result = await TrackAsync(OperationKind.Commit, async () => await ((ISynchronizationBackend)backend!).ContinueMergeAsync(workspace!, Message));
        if (result.Succeeded) Message = "";
        await RefreshAsync(true);
        if (!result.Succeeded) Feedback = Status = "合并提交结果待核实，说明与本地文件保留，不自动重放。" + result.Error;
        else Status = "合并已本地提交；需明确推送，不自动上传。";
    }
    private void UpdateConflictState(WorkspaceSnapshot state)
    {
        var previous = SelectedConflict?.Path;
        Conflicts.Clear();
        foreach (var file in state.Files.Where(file => file.Conflict && !file.IsDirectory)) Conflicts.Add(file);
        SelectedConflict = Conflicts.FirstOrDefault(file => file.Path == previous) ?? Conflicts.FirstOrDefault();
        if (plannedTarget is not null && (state.Revision != plannedTarget.LocalRevision || planKind != OperationKind.Abort && HasMerge)) ClearRemotePlan();
        Raise(nameof(HasMerge)); Raise(nameof(MergeStateLabel)); Raise(nameof(RemoteStateLabel)); RefreshCommands();
    }
}
