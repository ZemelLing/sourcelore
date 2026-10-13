using SourceLore.App.ViewModels;
using SourceLore.Core.Workspaces;
using SourceLore.Core.Operations;
using SourceLore.Infrastructure.Lore.Diagnostics;

namespace SourceLore.App.SmokeTests;

public class AuthenticationTests
{
    [Fact]
    public async Task CancelledLoginKeepsNativeCallAliveAndDoesNotAcceptLateSuccessOrReplay()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend, openBrowser: _ => throw new InvalidOperationException("No browser should open implicitly"));
        await model.OpenPathAsync("workspace");
        model.Message = "本地草稿";
        model.RemoteUrl = "lore://protected.example";
        model.CloneDestination = "fixture";
        model.Clone.Execute(null);
        await model.WaitForIdleAsync();
        model.Login.Execute(null);
        Assert.True(model.Busy);
        model.CancelLogin.Execute(null);
        Assert.True(model.Busy);
        Assert.Contains("底层调用", model.AuthenticationStatus);
        backend.Link!.Report(new("https://example.test/login?token=synthetic"));
        backend.Done.SetResult();
        await model.WaitForIdleAsync();
        Assert.False(model.OpenLoginBrowser.CanExecute(null));
        Assert.Contains("取消", model.AuthenticationStatus);
        Assert.Equal("本地草稿", model.Message);
        Assert.Equal(1, backend.RemoteWrites);
    }

    [Fact]
    public async Task FailedAuthenticationRedactsCredentialAndRequiresExplicitRetry()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend, SafeDiagnostics.Describe);
        model.RemoteUrl = "lore://protected.example";
        model.CloneDestination = "fixture";
        model.Clone.Execute(null);
        await model.WaitForIdleAsync();
        model.Login.Execute(null);
        backend.Done.SetException(new IOException("Authorization: Bearer secret-credential expired"));
        await model.WaitForIdleAsync();
        Assert.DoesNotContain("secret-credential", model.AuthenticationStatus);
        Assert.Contains("明确重试", model.AuthenticationStatus);
        Assert.Equal(1, backend.RemoteWrites);
        Assert.True(model.Login.CanExecute(null));
    }

    [Fact]
    public async Task SuccessStillDoesNotReplayRemoteOperation()
    {
        var backend = new Backend();
        var model = new MainWindowViewModel(backend);
        model.RemoteUrl = "lore://protected.example";
        model.CloneDestination = "fixture";
        model.Clone.Execute(null);
        await model.WaitForIdleAsync();
        model.Login.Execute(null);
        backend.Done.SetResult();
        await model.WaitForIdleAsync();
        Assert.Contains("登录完成", model.AuthenticationStatus);
        Assert.Equal(1, backend.RemoteWrites);
        model.Clone.Execute(null);
        await model.WaitForIdleAsync();
        Assert.Equal(2, backend.RemoteWrites);
    }

    private sealed class Backend : IWorkspaceBackend, IAuthenticationBackend, IRemoteWorkspaceBackend
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<AuthenticationLink>? Link { get; private set; }
        public int RemoteWrites { get; private set; }
        public Task<WorkspaceIdentity> CloneAsync(string remoteUrl, string destination, IProgress<OperationProgress>? progress = null) { RemoteWrites++; throw new IOException("authentication expired"); }
        public Task LoginAsync(string remoteUrl, IProgress<AuthenticationLink> link) { Link = link; return Done.Task; }
        public Task<WorkspaceIdentity> OpenAsync(string path) => Task.FromResult(new WorkspaceIdentity(path, path, path, ""));
        public Task<WorkspaceSnapshot> StatusAsync(WorkspaceIdentity workspace, bool scan, IProgress<OperationProgress>? progress = null) => Task.FromResult(new WorkspaceSnapshot("revision", "main", "", [], DateTimeOffset.UtcNow));
        public Task<string> DiffAsync(WorkspaceIdentity workspace, string path) => throw new NotSupportedException();
        public Task StageAsync(WorkspaceIdentity workspace, IReadOnlyList<string> paths, bool staged) => throw new NotSupportedException();
        public Task CommitAsync(WorkspaceIdentity workspace, string message) => throw new NotSupportedException();
    }
}
