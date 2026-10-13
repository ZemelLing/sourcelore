using LoreVcs.Types.Args;
using Sdk = LoreVcs.Lore;

namespace SourceLore.Lore.IntegrationTests;

// Compile-time contract for the pinned external SDK, not a production command dispatcher.
// Executors below are deliberately not executed: online and conflict semantics have separate integration fixtures.
internal static class SdkApiCompileContract
{
    private static void BindPlannedSurface(LoreGlobalArgs globals)
    {
        using var info = new LoreRepositoryInfoArgs();
        using var remoteConfig = new LoreRepositoryConfigGetArgs { Key = "remote_url" };
        using var instances = new LoreRepositoryInstanceListArgs();
        using var status = new LoreRepositoryStatusArgs { Staged = true, Scan = true, CheckDirty = true, SyncPoint = true };
        using var dirty = new LoreFileDirtyArgs { Paths = ["code.txt"] };
        using var stage = new LoreFileStageArgs { Paths = ["code.txt"] };
        using var unstage = new LoreFileUnstageArgs { Paths = ["code.txt"] };
        using var commit = new LoreRevisionCommitArgs { Message = "explicit commit" };
        using var fileInfo = new LoreFileInfoArgs { Paths = ["code.txt"], Revision = "" };
        using var fileDiff = new LoreFileDiffArgs { Paths = ["code.txt"], SourceRevision = "", TargetRevision = "", ContextLines = 3 };
        using var export = new LoreFileWriteArgs { Path = "code.txt", Revision = "", Output = "export.txt" };
        using var history = new LoreRevisionHistoryArgs { Revision = "", Length = 100 };
        using var revisionInfo = new LoreRevisionInfoArgs { Revision = "", Delta = true, Metadata = true };
        using var revisionDiff = new LoreRevisionDiffArgs { RevisionSource = "", RevisionTarget = "", Paths = ["code.txt"] };
        using var auth = new LoreAuthLoginInteractiveArgs { RemoteUrl = "lore://127.0.0.1", NoBrowser = true };
        using var clone = new LoreRepositoryCloneArgs { RepositoryUrl = "lore://127.0.0.1/repo" };
        using var push = new LoreBranchPushArgs { FastForwardMerge = false };
        using var sync = new LoreRevisionSyncArgs { Reset = false, ForwardChanges = false };
        using var merge = new LoreBranchMergeStartArgs { Branch = "main", NoCommit = true };
        using var resolve = new LoreBranchMergeResolveArgs { Paths = ["code.txt"] };
        using var restart = new LoreBranchMergeRestartArgs { Paths = ["code.txt"] };
        using var abort = new LoreBranchMergeAbortArgs();
        using var branches = new LoreBranchListArgs();
        using var create = new LoreBranchCreateArgs { Branch = "feature" };
        using var branchInfo = new LoreBranchInfoArgs { Branch = "main" };
        using var changeBranch = new LoreBranchSwitchArgs { Branch = "main", Reset = false };
        _ = Sdk.RepositoryInfo(globals, info);
        _ = Sdk.RepositoryConfigGet(globals, remoteConfig);
        _ = Sdk.RepositoryInstanceList(globals, instances);
        _ = Sdk.RepositoryStatus(globals, status);
        _ = Sdk.FileDirty(globals, dirty);
        _ = Sdk.FileStage(globals, stage);
        _ = Sdk.FileUnstage(globals, unstage);
        _ = Sdk.RevisionCommit(globals, commit);
        _ = Sdk.FileInfo(globals, fileInfo);
        _ = Sdk.FileDiff(globals, fileDiff);
        _ = Sdk.FileWrite(globals, export);
        _ = Sdk.RevisionHistory(globals, history);
        _ = Sdk.RevisionInfo(globals, revisionInfo);
        _ = Sdk.RevisionDiff(globals, revisionDiff);
        _ = Sdk.AuthLoginInteractive(globals, auth);
        _ = Sdk.RepositoryClone(globals, clone);
        _ = Sdk.BranchPush(globals, push);
        _ = Sdk.RevisionSync(globals, sync);
        _ = Sdk.BranchMergeStart(globals, merge);
        _ = Sdk.BranchMergeResolve(globals, resolve);
        _ = Sdk.BranchMergeRestart(globals, restart);
        _ = Sdk.BranchMergeAbort(globals, abort);
        _ = Sdk.BranchList(globals, branches);
        _ = Sdk.BranchCreate(globals, create);
        _ = Sdk.BranchInfo(globals, branchInfo);
        _ = Sdk.BranchSwitch(globals, changeBranch);
    }
}
