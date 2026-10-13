using System.Collections.ObjectModel;
using SourceLore.Core.Operations;
using SourceLore.Core.Workspaces;

namespace SourceLore.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly QueryGeneration historyDetailsGeneration = new(), historyDiffGeneration = new();
    private RevisionEntry? selectedHistory;
    private RevisionChoice? selectedHistoryBase;
    private RevisionFileChange? selectedHistoryFile;
    private string historyDetails = "选择历史版本查看详情。", historyPath = "", historyPatch = "", historyLeft = "", historyRight = "", historyComparisonLabel = "", historyFormat = "";
    public ObservableCollection<RevisionChoice> HistoryBases { get; } = [];
    public ObservableCollection<RevisionFileChange> HistoryFiles { get; } = [];
    public AsyncCommand CompareHistoryPath { get; }
    public AsyncCommand OpenHistoryLeft { get; }
    public AsyncCommand OpenHistoryRight { get; }
    public AsyncCommand FetchHistoryContent { get; }
    private readonly List<RevisionExport> externalSnapshots = [];
    public void ReleaseExternalSnapshots()
    {
        foreach (var snapshot in externalSnapshots)
        {
            try { snapshot.Dispose(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Feedback = "外部工具仍占用版本快照，文件保留在 " + snapshot.Path + "：" + describe(error); }
        }
        externalSnapshots.Clear();
    }
    private async Task OpenHistorySnapshotAsync(bool left)
    {
        var revision = left ? SelectedHistoryBase!.Revision : SelectedHistory!.Revision;
        var export = await ((IRevisionExportBackend)backend!).ExportRevisionAsync(workspace!, HistoryPath, revision);
        try { openFile!(export.Path); externalSnapshots.Add(export); }
        catch { export.Dispose(); throw; }
        Status = "已外部打开只读版本快照；工作文件保持原样。临时快照在客户端正常退出时清理。";
    }
    public string HistoryDetails { get => historyDetails; private set => Set(ref historyDetails, value); }
    public string HistoryPath
    {
        get => historyPath;
        set
        {
            if (Set(ref historyPath, value))
            {
                historyDiffGeneration.Advance();
                HistoryPatch = HistoryLeft = HistoryRight = HistoryComparisonLabel = HistoryFormat = "";
            }
            RefreshCommands();
        }
    }
    public string HistoryPatch { get => historyPatch; private set => Set(ref historyPatch, value); }
    public string HistoryLeft { get => historyLeft; private set => Set(ref historyLeft, value); }
    public string HistoryRight { get => historyRight; private set => Set(ref historyRight, value); }
    public string HistoryComparisonLabel { get => historyComparisonLabel; private set => Set(ref historyComparisonLabel, value); }
    public string HistoryFormat { get => historyFormat; private set => Set(ref historyFormat, value); }
    public RevisionEntry? SelectedHistory
    {
        get => selectedHistory;
        set
        {
            if (!Set(ref selectedHistory, value)) return;
            historyDetailsGeneration.Advance(); historyDiffGeneration.Advance();
            SelectedHistoryFile = null; HistoryFiles.Clear(); HistoryBases.Clear();
            selectedHistoryBase = null; Raise(nameof(SelectedHistoryBase));
            HistoryPatch = HistoryLeft = HistoryRight = HistoryComparisonLabel = HistoryFormat = "";
            if (value is null) { HistoryDetails = "选择历史版本查看详情。"; return; }
            HistoryDetails = $"版本 #{value.Number}：{value.Revision}\n作者：{value.Author}\n时间：{value.TimeLabel}\n{value.Message}\n父版本：{string.Join(", ", value.Parents)}";
            if (value.Parents.Count == 0) HistoryBases.Add(new("", "无父版本（空内容）"));
            for (var i = 0; i < value.Parents.Count; i++) HistoryBases.Add(new(value.Parents[i], (i == 0 ? "主父：" : "其他父：") + value.Parents[i]));
            foreach (var entry in History)
                if (!HistoryBases.Any(choice => choice.Revision == entry.Revision)) HistoryBases.Add(new(entry.Revision, "历史：" + entry.Revision));
            SelectedHistoryBase = HistoryBases[0];
            RefreshCommands();
        }
    }
    public RevisionChoice? SelectedHistoryBase
    {
        get => selectedHistoryBase;
        set
        {
            if (!Set(ref selectedHistoryBase, value)) return;
            historyDiffGeneration.Advance();
            HistoryPatch = HistoryLeft = HistoryRight = HistoryComparisonLabel = HistoryFormat = "";
            StartHistoryQuery(LoadHistoryFilesAsync());
            RefreshCommands();
        }
    }
    public RevisionFileChange? SelectedHistoryFile
    {
        get => selectedHistoryFile;
        set
        {
            if (!Set(ref selectedHistoryFile, value)) return;
            historyDiffGeneration.Advance();
            if (value is null) return;
            HistoryPath = value.Path;
            StartHistoryQuery(LoadHistoryDiffAsync());
        }
    }
    private void ClearHistoryDetails()
    {
        historyDetailsGeneration.Advance(); historyDiffGeneration.Advance();
        SelectedHistory = null; selectedHistoryBase = null; Raise(nameof(SelectedHistoryBase));
        HistoryFiles.Clear(); HistoryBases.Clear();
        HistoryPath = HistoryPatch = HistoryLeft = HistoryRight = HistoryComparisonLabel = HistoryFormat = "";
    }
    private void StartHistoryQuery(Task operation)
    {
        lock (diffOperations) diffOperations.Add(operation);
        _ = ObserveDiffAsync(operation);
    }
    private async Task LoadHistoryFilesAsync()
    {
        var generation = historyDetailsGeneration.Advance();
        var owner = workspace; var target = SelectedHistory; var source = SelectedHistoryBase;
        SelectedHistoryFile = null; HistoryFiles.Clear();
        if (owner is null || target is null || source is null || backend is not IRevisionDetailBackend details) return;
        try
        {
            var files = await details.ChangedFilesAsync(owner, target.Revision, source.Revision);
            if (!historyDetailsGeneration.IsCurrent(generation) || workspace != owner) return;
            foreach (var file in files) HistoryFiles.Add(file);
            SelectedHistoryFile = HistoryFiles.FirstOrDefault(file => file.IsFile);
            if (files.Count == 0) HistoryPatch = "这两个版本之间没有文件变更；也可输入路径查看相同内容。";
        }
        catch (Exception error)
        {
            if (historyDetailsGeneration.IsCurrent(generation) && workspace == owner) HistoryFormat = HistoryPatch = describe(error);
        }
    }
    private async Task LoadHistoryDiffAsync(bool online = false)
    {
        var generation = historyDiffGeneration.Advance();
        var owner = workspace; var target = SelectedHistory; var source = SelectedHistoryBase; var path = HistoryPath;
        HistoryLeft = HistoryRight = HistoryComparisonLabel = HistoryFormat = "";
        if (owner is null || target is null || source is null || backend is not ITextComparisonBackend comparisons) return;
        HistoryPatch = "正在读取历史内容…";
        try
        {
            var comparison = online ? await ((IHistoryContentBackend)backend!).CompareOnlineAsync(owner, path, source.Revision, target.Revision)
                : await comparisons.CompareAsync(owner, path, source.Revision, target.Revision);
            if (!historyDiffGeneration.IsCurrent(generation) || workspace != owner) return;
            HistoryPatch = comparison.Unified; HistoryLeft = comparison.LeftView; HistoryRight = comparison.RightView;
            HistoryComparisonLabel = comparison.SourceLabel; HistoryFormat = comparison.FormatLabel;
        }
        catch (Exception error)
        {
            if (historyDiffGeneration.IsCurrent(generation) && workspace == owner) HistoryFormat = HistoryPatch = describe(error);
        }
    }
}
