using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceBackend? backend;
    private readonly Func<Exception, string> describe;
    private readonly IPendingOperationStore? journal;
    private readonly OperationTracker? tracker;
    private readonly IRecentWorkspaceStore? recentStore;
    private readonly WorkspaceMonitorFactory? monitorFactory;
    private readonly Dictionary<string, IWorkspaceMonitor> monitors = new(StringComparer.OrdinalIgnoreCase);
    private bool monitoringStopped;
    private bool stale = true;
    private readonly WorkspaceSessions sessions = new();
    private WorkspaceSession? session;
    private RecentWorkspace? selectedRecent;
    private PendingOperation? pending;
    private string recovery = "";
    private string feedback = "";
    private readonly QueryGeneration selection = new();
    private readonly QueryGeneration operations = new();
    private long activeOperation;
    private WorkspaceIdentity? workspace;
    private WorkspaceSnapshot? snapshot;
    private string path = "", message = "", patch = "", status = "打开 Lore 工作目录，查看变更与版本历史。", identity = "未打开工作目录";
    private string leftPatch = "", rightPatch = "", comparisonLabel = "", formatLabel = "";
    private bool sideBySide;
    private readonly Action<string>? openFile;
    private FileChange? selectedFile;
    private FileChoice? selectedChoice;
    private bool busy;
    private int selectedPage;
    private string historyStatus = "打开工作目录后加载历史；远端推送状态未知。";
    private string remoteUrl = "", cloneDestination = "";
    private string newBranchName = "", branchStatus = "尚未读取本地分支，远端状态未知。";
    private BranchEntry? selectedBranch;
    private readonly Action<string>? openBrowser;
    private string authenticationStatus = "尚未登录；凭据由 Lore 管理。";
    private string? loginUrl;
    private bool loginRunning, loginCancelled;
    private long loginGeneration;
    private Task currentOperation = Task.CompletedTask;
    private readonly HashSet<Task> diffOperations = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(IWorkspaceBackend? backend = null, Func<Exception, string>? describe = null, string? startupError = null,
        IPendingOperationStore? journal = null, IRecentWorkspaceStore? recentStore = null, WorkspaceMonitorFactory? monitorFactory = null, Action<string>? openBrowser = null, Action<string>? openFile = null)
    {
        this.backend = backend;
        this.describe = describe ?? (error => error.Message);
        this.journal = journal;
        this.recentStore = recentStore;
        this.monitorFactory = monitorFactory;
        this.openBrowser = openBrowser;
        this.openFile = openFile;
        CompareHistoryPath = new(() => { StartHistoryQuery(LoadHistoryDiffAsync()); return Task.CompletedTask; }, () => workspace is not null && SelectedHistory is not null && SelectedHistoryBase is not null && backend is ITextComparisonBackend && !string.IsNullOrWhiteSpace(HistoryPath), ShowError);
        OpenHistoryLeft = new(() => RunAsync(() => OpenHistorySnapshotAsync(true)), () => !Busy && workspace is not null && SelectedHistoryBase is not null && RevisionHistoryPager.IsRevision(SelectedHistoryBase.Revision) && backend is IRevisionExportBackend && openFile is not null && !string.IsNullOrWhiteSpace(HistoryPath), ShowError);
        OpenHistoryRight = new(() => RunAsync(() => OpenHistorySnapshotAsync(false)), () => !Busy && workspace is not null && SelectedHistory is not null && backend is IRevisionExportBackend && openFile is not null && !string.IsNullOrWhiteSpace(HistoryPath), ShowError);
        FetchHistoryContent = new(() => RunAsync(() => LoadHistoryDiffAsync(true)), () => !Busy && workspace is not null && SelectedHistory is not null && SelectedHistoryBase is not null && backend is IHistoryContentBackend && !string.IsNullOrWhiteSpace(HistoryPath), ShowError);
        FetchWorkingContent = new(() => RunAsync(() => LoadDiffAsync(true)), () => !Busy && workspace is not null && SelectedFile is { IsDirectory: false } && backend is IHistoryContentBackend, ShowError);
        VerifyRemote = new(() => RunAsync(VerifyRemoteStateAsync), () => !Busy && workspace is not null && backend is ISynchronizationBackend, ShowError);
        PreparePush = new(() => RunAsync(() => PrepareRemoteOperationAsync(OperationKind.Push)), () => !Busy && pending is null && workspace is not null && backend is ISynchronizationBackend, ShowError);
        PrepareSync = new(() => RunAsync(() => PrepareRemoteOperationAsync(OperationKind.Sync)), () => !Busy && pending is null && workspace is not null && backend is ISynchronizationBackend, ShowError);
        PrepareAbort = new(() => RunAsync(() => PrepareRemoteOperationAsync(OperationKind.Abort)), () => !Busy && pending is null && HasMerge && backend is ISynchronizationBackend, ShowError);
        ConfirmRemoteOperation = new(() => RunAsync(ConfirmRemoteOperationAsync), () => !Busy && pending is null && HasRemotePlan, ShowError);
        CancelRemotePlan = new(() => { ClearRemotePlan(); return Task.CompletedTask; }, () => !Busy && HasRemotePlan, ShowError);
        MarkConflictResolved = new(() => RunAsync(MarkConflictResolvedAsync), () => !Busy && pending is null && HasMerge && SelectedConflict is not null && backend is ISynchronizationBackend, ShowError);
        OpenConflictFile = new(() => { SelectedFile = SelectedConflict; OpenWorkingFile!.Execute(null); return Task.CompletedTask; }, () => !Busy && SelectedConflict is not null && openFile is not null, ShowError);
        ContinueMerge = new(() => RunAsync(ContinueMergeOperationAsync), () => !Busy && pending is null && HasMerge && snapshot?.HasUnresolvedConflicts == false && backend is ISynchronizationBackend, ShowError);
        if (journal is not null) tracker = new(journal, this.describe);
        if (startupError is not null) status = startupError;
        Open = new(() => RunAsync(OpenAsync), () => !Busy && backend is not null, ShowError);
        Refresh = new(() => RunAsync(() => RefreshAsync(true)), () => !Busy && workspace is not null, ShowError);
        Stage = new(() => RunAsync(() => StageAsync(true)), () => !Busy && pending is null && SelectedFile is { IsDirectory: false }, ShowError);
        Unstage = new(() => RunAsync(() => StageAsync(false)), () => !Busy && pending is null && SelectedFile is { IsDirectory: false, Staged: true }, ShowError);
        StageChecked = new(() => RunAsync(() => StageCheckedAsync(true)), () => !Busy && pending is null && Choices.Any(row => row.IsChecked && row.IsFile), ShowError);
        UnstageChecked = new(() => RunAsync(() => StageCheckedAsync(false)), () => !Busy && pending is null && Choices.Any(row => row.IsChecked && row.IsFile), ShowError);
        Commit = new(() => RunAsync(CommitAsync), () => !Busy && pending is null && workspace is not null, ShowError);
        ConfirmReconciliation = new(() => RunAsync(ConfirmReconciliationAsync), () => !Busy && pending is not null && snapshot is not null, ShowError);
        OpenRecent = new(() => OpenPathAsync(SelectedRecent!.RootPath), () => !Busy && backend is not null && SelectedRecent is not null, ShowError);
        RemoveRecent = new(() => RunAsync(RemoveRecentAsync), () => !Busy && SelectedRecent is not null, ShowError);
        RefreshHistory = new(() => RunAsync(() => LoadHistoryAsync(true)), () => !Busy && workspace is not null && backend is IHistoryBackend, ShowError);
        MoreHistory = new(() => RunAsync(() => LoadHistoryAsync(false)), () => !Busy && session?.HistoryPager?.HasMore == true, ShowError);
        Clone = new(() => RunAsync(CloneAsync), () => !Busy && backend is IRemoteWorkspaceBackend, ShowError);
        RefreshBranches = new(() => RunAsync(LoadBranchesAsync), () => !Busy && workspace is not null && backend is IBranchBackend, ShowError);
        CreateBranch = new(() => RunAsync(() => ChangeBranchAsync(true)), () => !Busy && pending is null && workspace is not null && backend is IBranchBackend, ShowError);
        SwitchBranch = new(() => RunAsync(() => ChangeBranchAsync(false)), () => !Busy && pending is null && SelectedBranch is not null && backend is IBranchBackend, ShowError);
        Login = new(() => RunAsync(LoginAsync), () => !Busy && backend is IAuthenticationBackend, ShowError);
        OpenLoginBrowser = new(() => { openBrowser!(loginUrl!); return Task.CompletedTask; }, () => loginRunning && !loginCancelled && loginUrl is not null && openBrowser is not null, ShowError);
        CancelLogin = new(() => { loginCancelled = true; loginUrl = null; AuthenticationStatus = "已取消浏览器等待；底层调用仍在结束，不会重试远端操作。"; RefreshCommands(); return Task.CompletedTask; }, () => loginRunning && !loginCancelled, ShowError);
        OpenWorkingFile = new(() =>
        {
            var root = System.IO.Path.GetFullPath(workspace!.RootPath).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, SelectedFile!.Path));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new InvalidOperationException("工作文件不存在或已删除，无法外部打开。");
            openFile!(full);
            return Task.CompletedTask;
        }, () => !Busy && workspace is not null && SelectedFile is { IsDirectory: false } && openFile is not null, ShowError);
    }

    public AsyncCommand OpenWorkingFile { get; }
    public AsyncCommand FetchWorkingContent { get; }
    public bool SideBySide { get => sideBySide; set { if (Set(ref sideBySide, value)) PropertyChanged?.Invoke(this, new(nameof(UnifiedVisible))); } }
    public bool UnifiedVisible => !SideBySide;
    public string LeftPatch { get => leftPatch; private set => Set(ref leftPatch, value); }
    public string RightPatch { get => rightPatch; private set => Set(ref rightPatch, value); }
    public string ComparisonLabel { get => comparisonLabel; private set => Set(ref comparisonLabel, value); }
    public string FormatLabel { get => formatLabel; private set => Set(ref formatLabel, value); }

    public string Title => "SourceLore";
    public string Status { get => status; private set => Set(ref status, value); }
    public string WorkspaceStateLabel => stale ? "工作区状态可能过期；请扫描核对。" : $"工作区已核对：{snapshot?.VerifiedAt.ToLocalTime():HH:mm:ss}。";
    public string Identity { get => identity; private set => Set(ref identity, value); }
    public string Recovery { get => recovery; private set => Set(ref recovery, value); }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public string Path { get => path; set => Set(ref path, value); }
    public string Message { get => message; set { Set(ref message, value); if (session is not null) session.CommitDraft = value; } }
    public string Patch { get => patch; private set => Set(ref patch, value); }
    public bool Busy { get => busy; private set { Set(ref busy, value); Raise(nameof(NotBusy)); RefreshCommands(); } }
    public bool NotBusy => !Busy;
    public int SelectedPage { get => selectedPage; set { Set(ref selectedPage, value); if (session is not null) session.SelectedPage = value; } }
    public string HistoryStatus { get => historyStatus; private set => Set(ref historyStatus, value); }
    public string RemoteUrl { get => remoteUrl; set => Set(ref remoteUrl, value); }
    public string CloneDestination { get => cloneDestination; set => Set(ref cloneDestination, value); }
    public string NewBranchName { get => newBranchName; set => Set(ref newBranchName, value); }
    public string BranchStatus { get => branchStatus; private set => Set(ref branchStatus, value); }
    public string AuthenticationStatus { get => authenticationStatus; private set => Set(ref authenticationStatus, value); }
    public BranchEntry? SelectedBranch { get => selectedBranch; set { Set(ref selectedBranch, value); RefreshCommands(); } }
    public string BranchCreateHint => $"从当前版本 {snapshot?.Revision ?? "未知"} 创建，并立即切换到新分支。";
    public AsyncCommand Open { get; }
    public AsyncCommand Refresh { get; }
    public AsyncCommand Stage { get; }
    public AsyncCommand Unstage { get; }
    public AsyncCommand StageChecked { get; }
    public AsyncCommand UnstageChecked { get; }
    public AsyncCommand Commit { get; }
    public AsyncCommand ConfirmReconciliation { get; }
    public AsyncCommand OpenRecent { get; }
    public AsyncCommand RemoveRecent { get; }
    public AsyncCommand RefreshHistory { get; }
    public AsyncCommand MoreHistory { get; }
    public AsyncCommand Clone { get; }
    public AsyncCommand RefreshBranches { get; }
    public AsyncCommand CreateBranch { get; }
    public AsyncCommand SwitchBranch { get; }
    public AsyncCommand Login { get; }
    public AsyncCommand OpenLoginBrowser { get; }
    public AsyncCommand CancelLogin { get; }
    public ObservableCollection<BranchEntry> Branches { get; } = [];
    public ObservableCollection<RevisionEntry> History { get; } = [];
    public ObservableCollection<RecentWorkspace> Recent { get; } = [];
    public RecentWorkspace? SelectedRecent { get => selectedRecent; set { Set(ref selectedRecent, value); RefreshCommands(); } }
    public ObservableCollection<FileChange> Files { get; } = [];
    public ObservableCollection<FileChoice> Choices { get; } = [];
    public FileChoice? SelectedChoice { get => selectedChoice; set { if (Set(ref selectedChoice, value)) SelectedFile = value?.File; } }

    public FileChange? SelectedFile
    {
        get => selectedFile;
        set
        {
            if (!Set(ref selectedFile, value)) return;
            Set(ref selectedChoice, Choices.FirstOrDefault(row => row.File == value), nameof(SelectedChoice));
            if (session is not null) session.SelectedPath = value?.Path;
            RefreshCommands();
            var operation = LoadDiffAsync();
            lock (diffOperations) diffOperations.Add(operation);
            _ = ObserveDiffAsync(operation);
        }
    }

    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            var active = currentOperation;
            await active;
            Task[] reads;
            lock (diffOperations) reads = diffOperations.ToArray();
            await Task.WhenAll(reads);
            if (active == currentOperation && !Busy) return;
        }
    }
    public async Task StopMonitoringAsync()
    {
        monitoringStopped = true;
        foreach (var monitor in monitors.Values) await monitor.DisposeAsync();
        monitors.Clear();
    }
    public Task RecoverFocusAsync() => workspace is null || Busy || monitoringStopped ? Task.CompletedTask : RunAsync(() => RefreshAsync(true), background: true);
    private async Task ObserveDiffAsync(Task operation)
    {
        try { await operation; }
        finally { lock (diffOperations) diffOperations.Remove(operation); }
    }
    public Task OpenPathAsync(string value) { Path = value; return RunAsync(OpenAsync); }
    public Task InitializeRecentAsync() => RunAsync(async () =>
    {
        if (recentStore is null) return;
        var loaded = await recentStore.ReadAsync();
        Recent.Clear();
        foreach (var entry in loaded.DistinctBy(entry => entry.RootPath, StringComparer.OrdinalIgnoreCase).Take(30)) Recent.Add(entry);
    });
    internal Task StageSelectedAsync(bool staged) => RunAsync(() => StageAsync(staged));
    internal Task CommitStagedAsync() => RunAsync(CommitAsync);

    private Task RunAsync(Func<Task> operation, bool background = false)
    {
        if (Busy) return currentOperation;
        if (!background) Feedback = "";
        Busy = true;
        activeOperation = operations.Advance();
        currentOperation = ExecuteAsync(operation);
        return currentOperation;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception error) { ShowError(error); }
        finally { Busy = false; }
    }

    private async Task OpenAsync()
    {
        selection.Advance();
        Status = "正在验证工作目录并扫描…";
        var opened = await backend!.OpenAsync(Path);
        var first = await backend.StatusAsync(opened, true, CreateProgress());
        workspace = opened;
        ClearHistoryDetails();
        ClearRemotePlan(); currentRemoteTarget = null;
        if (!monitoringStopped && monitorFactory is not null && backend is IDirtyWorkspaceBackend && !monitors.ContainsKey(opened.InstanceId))
            monitors.Add(opened.InstanceId, monitorFactory(opened, batch => HandleFileEventsAsync(opened, batch)));
        session = sessions.GetOrAdd(opened);
        Message = session.CommitDraft;
        SelectedPage = session.SelectedPage;
        snapshot = first;
        ApplySnapshot(first, session.SelectedPath);
        History.Clear();
        foreach (var entry in session.History) History.Add(entry);
        Branches.Clear(); SelectedBranch = null;
        await LoadBranchesAsync();
        await RefreshRemoteTargetAsync();
        UpdateHistoryStatus();
        Path = opened.RootPath;
        await ReadRecoveryAsync();
        for (var index = Recent.Count - 1; index >= 0; index--)
            if (string.Equals(Recent[index].RootPath, opened.RootPath, StringComparison.OrdinalIgnoreCase)) Recent.RemoveAt(index);
        Recent.Insert(0, new(opened.RootPath, opened.Name));
        while (Recent.Count > 30) Recent.RemoveAt(Recent.Count - 1);
        if (recentStore is not null) await recentStore.WriteAsync(Recent.ToArray());
    }

    private async Task CloneAsync()
    {
        Status = "正在克隆到指定目录；失败后保留残留目录…";
        var cloned = await ((IRemoteWorkspaceBackend)backend!).CloneAsync(RemoteUrl, CloneDestination, CreateProgress());
        Path = cloned.RootPath;
        await OpenAsync();
    }

    private async Task LoginAsync()
    {
        var generation = ++loginGeneration;
        loginRunning = true; loginCancelled = false; loginUrl = null;
        AuthenticationStatus = "正在联系认证服务…"; RefreshCommands();
        var progress = new Progress<AuthenticationLink>(link =>
        {
            if (generation != loginGeneration || !loginRunning || loginCancelled) return;
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            { AuthenticationStatus = "认证服务返回了不支持的浏览器地址。"; return; }
            loginUrl = url.AbsoluteUri;
            AuthenticationStatus = "登录地址已就绪，请点击打开浏览器并完成登录。";
            RefreshCommands();
        });
        try
        {
            await ((IAuthenticationBackend)backend!).LoginAsync(RemoteUrl, progress);
            AuthenticationStatus = loginCancelled ? "已取消接收登录结果；原生调用已结束。凭据由 Lore 管理，原操作未重试。" : "Lore 登录完成；请明确重试原远端操作，不会自动执行。";
        }
        catch (Exception error) { AuthenticationStatus = "认证未完成：" + describe(error) + " 本地工作保留；重新登录后需明确重试。"; }
        finally { loginRunning = false; loginUrl = null; RefreshCommands(); }
    }

    private async Task RefreshAsync(bool scan)
    {
        if (workspace is null) return;
        stale = true; Raise(nameof(WorkspaceStateLabel));
        Status = scan ? "正在扫描；此前列表可能过期…" : "正在核对状态…";
        var next = await backend!.StatusAsync(workspace, scan, CreateProgress());
        snapshot = next;
        ApplySnapshot(next);
        await ReadRecoveryAsync();
        await LoadBranchesAsync();
        await RefreshRemoteTargetAsync();
    }

    private void ApplySnapshot(WorkspaceSnapshot state, string? restoredPath = null)
    {
        stale = false; Raise(nameof(WorkspaceStateLabel));
        if (session is not null) session.Snapshot = state;
        var previousPath = restoredPath ?? session?.SelectedPath;
        selection.Advance();
        SelectedFile = null;
        Files.Clear();
        Choices.Clear();
        var owner = session;
        owner?.CheckedPaths.IntersectWith(state.Files.Select(file => file.Path));
        foreach (var file in state.Files)
        {
            Files.Add(file);
            Choices.Add(new(file, owner?.CheckedPaths.Contains(file.Path) == true, value =>
            {
                if (value) owner?.CheckedPaths.Add(file.Path); else owner?.CheckedPaths.Remove(file.Path);
                RefreshCommands();
            }));
        }
        SelectedFile = Files.FirstOrDefault(file => file.Path == previousPath) ?? Files.FirstOrDefault();
        if (SelectedFile is null) Patch = "";
        Identity = $"{workspace!.Name}  ·  {state.BranchName}  ·  {state.Revision}";
        Status = $"已核对：{state.Files.Count} 项变更，{state.Files.Count(file => file.Staged)} 项已暂存。";
        UpdateHistoryStatus();
        Raise(nameof(BranchCreateHint));
        UpdateConflictState(state);
    }

    private async Task HandleFileEventsAsync(WorkspaceIdentity owner, WorkspaceInvalidation batch)
    {
        if (workspace?.InstanceId != owner.InstanceId || monitoringStopped) return;
        stale = true; Raise(nameof(WorkspaceStateLabel));
        await WaitForIdleAsync();
        if (workspace?.InstanceId != owner.InstanceId || monitoringStopped) return;
        await RunAsync(async () =>
        {
            if (!batch.RequiresScan) await ((IDirtyWorkspaceBackend)backend!).UpdateDirtyAsync(owner, batch.Paths);
            await RefreshAsync(batch.RequiresScan);
        }, background: true);
    }

    private async Task LoadBranchesAsync()
    {
        if (backend is not IBranchBackend branchesBackend || workspace is null) return;
        var selected = SelectedBranch?.Id;
        try
        {
            var entries = await branchesBackend.BranchesAsync(workspace);
            Branches.Clear();
            foreach (var entry in entries) Branches.Add(entry with { IsCurrent = entry.Name == snapshot?.BranchName });
            SelectedBranch = Branches.FirstOrDefault(entry => entry.Id == selected) ?? Branches.FirstOrDefault(entry => entry.IsCurrent);
            BranchStatus = $"本地分支已读取（{DateTimeOffset.Now:HH:mm:ss}）；离线缓存，远端状态未核实。";
            UpdateHistoryStatus();
        }
        catch (Exception error) { BranchStatus = "分支信息过期，保留已知列表：" + describe(error); }
    }

    private async Task ChangeBranchAsync(bool create)
    {
        if (workspace is null || backend is not IBranchBackend branchesBackend) return;
        EnsureMutationAllowed();
        var target = create ? NewBranchName : SelectedBranch?.Name;
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("请填写或选择分支名称。");
        await RefreshAsync(true);
        if (snapshot!.HasConflicts || snapshot.HasMerge) throw new InvalidOperationException("请先完成合并流程。");
        if (snapshot.Files.Count > 0) throw new InvalidOperationException("存在本地修改或暂存选择，请先处理。文件保留，未切换分支。");
        var result = await TrackAsync(create ? OperationKind.CreateBranch : OperationKind.SwitchBranch,
            () => create ? branchesBackend.CreateBranchAsync(workspace, target) : branchesBackend.SwitchBranchAsync(workspace, target));
        await RefreshAsync(true);
        await LoadBranchesAsync();
        if (result.Succeeded) { NewBranchName = ""; SelectedBranch = Branches.FirstOrDefault(entry => entry.IsCurrent); await LoadHistoryAsync(true); }
        else Feedback = Status = "分支操作结果待核实，当前标识来自实际状态：" + result.Error;
    }

    private void UpdateHistoryStatus()
    {
        var knownTip = session?.HistoryBranchId is { } id ? Branches.FirstOrDefault(entry => entry.Id == id)?.Latest : snapshot?.Revision;
        HistoryStatus = session?.HistoryPager is { } pager
            ? $"起点：{pager.Tip}；已加载 {History.Count} 项。" + (knownTip is not null && knownTip != pager.Tip ? "分支版本已改变，请刷新历史。" : "") + "远端推送状态未知。"
            : "尚未加载历史；远端推送状态未知。";
    }

    private async Task LoadHistoryAsync(bool refresh)
    {
        if (backend is not IHistoryBackend historyBackend || workspace is null || session is null || snapshot is null) return;
        var owner = session;
        var tip = SelectedBranch?.Latest ?? snapshot.Revision;
        var rebuild = refresh || owner.HistoryPager is null;
        var branchId = SelectedBranch?.Id;
        var pager = rebuild ? new RevisionHistoryPager(historyBackend, workspace, tip) : owner.HistoryPager!;
        try
        {
            HistoryStatus = "正在读取本地历史；已有列表保留…";
            var page = await pager.NextAsync();
            if (session != owner) return;
            if (refresh) { ClearHistoryDetails(); owner.History.Clear(); History.Clear(); }
            owner.HistoryPager = pager;
            if (rebuild) owner.HistoryBranchId = branchId;
            owner.History.AddRange(page);
            foreach (var entry in page) History.Add(entry);
            UpdateHistoryStatus();
        }
        catch (Exception error)
        {
            if (session == owner) HistoryStatus = "历史加载失败，已有列表保留：" + describe(error);
        }
    }

    private async Task StageAsync(bool staged)
    {
        if (workspace is null || SelectedFile is null) return;
        EnsureMutationAllowed();
        var path = SelectedFile.Path;
        Status = staged ? "正在暂存文件…" : "正在取消暂存…";
        var result = await TrackAsync(staged ? OperationKind.Stage : OperationKind.Unstage, () => backend!.StageAsync(workspace, [path], staged));
        await RefreshAsync(false);
        if (!result.Succeeded) Feedback = Status = "操作结果待核实；请检查当前状态。" + result.Error;
    }

    private async Task StageCheckedAsync(bool staged)
    {
        if (workspace is null) return;
        EnsureMutationAllowed();
        var paths = Choices.Where(row => row.IsChecked && row.IsFile).Select(row => row.File.Path).ToArray();
        if (paths.Length == 0) return;
        var result = await TrackAsync(staged ? OperationKind.Stage : OperationKind.Unstage, () => backend!.StageAsync(workspace, paths, staged));
        await RefreshAsync(false);
        if (!result.Succeeded) Feedback = Status = "批量操作可能部分完成；列表来自后端实际状态，请核对每个文件：" + result.Error;
    }

    private async Task CommitAsync()
    {
        if (workspace is null) return;
        EnsureMutationAllowed();
        if (string.IsNullOrWhiteSpace(Message)) throw new InvalidOperationException("请填写提交说明。");
        await RefreshAsync(true);
        if (snapshot!.HasConflicts) throw new InvalidOperationException("请先完成冲突流程。");
        if (!snapshot.Files.Any(file => file.Staged && !file.IsDirectory)) throw new InvalidOperationException("没有可提交的已暂存文件。");
        Status = "正在本地提交…";
        var result = await TrackAsync(OperationKind.Commit, () => backend!.CommitAsync(workspace, Message));
        if (result.Succeeded) Message = "";
        await RefreshAsync(false);
        if (!result.Succeeded) Feedback = Status = "提交结果待核实；已保留说明，请检查当前版本和暂存状态。" + result.Error;
    }

    private void EnsureMutationAllowed()
    {
        if (pending is not null) throw new InvalidOperationException("请先核对并确认上次操作的实际状态。");
    }

    private async Task<OperationResult> TrackAsync(OperationKind kind, Func<Task> operation)
    {
        if (tracker is not null) return await tracker.RunAsync(workspace!, kind, snapshot?.Revision ?? "", operation);
        await operation();
        return new(true, false, null);
    }

    private async Task ReadRecoveryAsync()
    {
        if (workspace is null || journal is null) return;
        pending = await journal.ReadAsync(workspace);
        Recovery = pending is null ? "" : $"上次 {pending.Kind} 的结果待核实。开始版本：{pending.StartingRevision}；实际当前版本：{snapshot?.Revision}。检查文件与版本后确认；不会自动重放。";
        RefreshCommands();
    }

    private async Task ConfirmReconciliationAsync()
    {
        if (workspace is null || journal is null || pending is null) return;
        await RefreshAsync(true);
        if (pending?.Kind == OperationKind.Push && backend is ISynchronizationBackend) await VerifyRemoteStateAsync();
        await journal.ClearAsync(workspace);
        await ReadRecoveryAsync();
        Status = "已确认核对当前状态。原操作不会重放，后续操作需重新明确发起。";
    }

    private async Task RemoveRecentAsync()
    {
        if (SelectedRecent is null) return;
        Recent.Remove(SelectedRecent);
        SelectedRecent = null;
        if (recentStore is not null) await recentStore.WriteAsync(Recent.ToArray());
        Status = "已移除最近记录，磁盘内容保留。";
    }

    private async Task LoadDiffAsync(bool online = false)
    {
        var generation = selection.Advance();
        var file = SelectedFile;
        var owner = workspace;
        LeftPatch = RightPatch = ComparisonLabel = FormatLabel = "";
        if (file is null || owner is null) { Patch = ""; return; }
        if (file.IsDirectory) { Patch = "请选择文本文件。"; return; }
        Patch = "正在加载差异…";
        try
        {
            if (backend is ITextComparisonBackend comparisons)
            {
                var diff = online ? await ((IHistoryContentBackend)backend!).CompareOnlineAsync(owner, file.Path, snapshot?.Revision ?? "", null)
                    : await comparisons.CompareAsync(owner, file.Path, snapshot?.Revision ?? "", null);
                if (selection.IsCurrent(generation) && workspace == owner)
                {
                    Patch = diff.Unified; LeftPatch = diff.LeftView; RightPatch = diff.RightView;
                    ComparisonLabel = diff.SourceLabel; FormatLabel = diff.FormatLabel;
                }
            }
            else
            {
                var diff = await backend!.DiffAsync(owner, file.Path);
                if (selection.IsCurrent(generation) && workspace == owner) Patch = diff;
            }
        }
        catch (Exception error)
        {
            if (selection.IsCurrent(generation) && workspace == owner) FormatLabel = Patch = describe(error);
        }
    }

    private void ShowError(Exception error) => Feedback = Status = "操作失败；状态需要重新核对。" + describe(error);
    private IProgress<OperationProgress> CreateProgress()
    {
        var generation = activeOperation;
        return new Progress<OperationProgress>(update =>
        {
            if (Busy && operations.IsCurrent(generation) && !update.IsFinal) Status = update.Message;
        });
    }
    private void RefreshCommands()
    {
        CompareHistoryPath?.Refresh();
        OpenHistoryLeft?.Refresh(); OpenHistoryRight?.Refresh();
        FetchHistoryContent?.Refresh();
        VerifyRemote?.Refresh(); PreparePush?.Refresh(); PrepareSync?.Refresh(); PrepareAbort?.Refresh();
        ConfirmRemoteOperation?.Refresh(); CancelRemotePlan?.Refresh(); MarkConflictResolved?.Refresh(); ContinueMerge?.Refresh();
        OpenConflictFile?.Refresh();
        OpenWorkingFile?.Refresh();
        FetchWorkingContent?.Refresh();
        Open?.Refresh(); Refresh?.Refresh(); Stage?.Refresh(); Unstage?.Refresh(); Commit?.Refresh(); ConfirmReconciliation?.Refresh();
        StageChecked?.Refresh(); UnstageChecked?.Refresh();
        OpenRecent?.Refresh(); RemoveRecent?.Refresh();
        RefreshHistory?.Refresh(); MoreHistory?.Refresh();
        Clone?.Refresh();
        RefreshBranches?.Refresh(); CreateBranch?.Refresh(); SwitchBranch?.Refresh();
        Login?.Refresh(); OpenLoginBrowser?.Refresh(); CancelLogin?.Refresh();
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
    private void Raise(string? name) => PropertyChanged?.Invoke(this, new(name));
}
