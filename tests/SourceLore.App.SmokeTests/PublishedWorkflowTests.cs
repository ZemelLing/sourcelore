using System.Diagnostics;
using LoreVcs.Types.Args;
using SourceLore.Infrastructure.Lore;
using SourceLore.Lore.IntegrationTests;

namespace SourceLore.App.SmokeTests;

public sealed class PublishedNativeRuntimeFixture : IDisposable
{
    public LoreRuntime Runtime { get; } = new();
    public void Dispose() => Runtime.Dispose();
}

public class PublishedWorkflowTests(PublishedNativeRuntimeFixture native) : IClassFixture<PublishedNativeRuntimeFixture>
{
    [Fact]
    public async Task NativeAotSevenCapabilitiesRunThroughTwoClonesAndControlledServer()
    {
        var executable = Environment.GetEnvironmentVariable("SOURCELORE_PUBLISHED_EXE") ?? throw new InvalidOperationException("Set SOURCELORE_PUBLISHED_EXE");
        await using var server = new ControlledServer(); await server.StartAsync();
        var source = Path.Combine(server.Root, "seed"); Directory.CreateDirectory(source);
        using var globals = new LoreGlobalArgs { RepositoryPath = source, WorkingDirectory = source };
        using var create = new LoreRepositoryCreateArgs { RepositoryUrl = server.Url + "/gui-workflow" };
        await global::LoreVcs.Lore.RepositoryCreate(globals, create).WaitAsync();
        var backend = new LoreWorkspaceBackend(native.Runtime);
        var workspace = await backend.OpenAsync(source);
        await File.WriteAllTextAsync(Path.Combine(source, "程序.txt"), "服务器初始版本\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, "保留.txt"), "服务器保留原文\n", TestContext.Current.CancellationToken);
        await backend.StageAsync(workspace, ["程序.txt", "保留.txt"], true); await backend.CommitAsync(workspace, "服务器初始提交");
        await backend.PushAsync(workspace, await backend.ReadTargetAsync(workspace));
        using var release = new LoreRepositoryReleaseArgs();
        await global::LoreVcs.Lore.RepositoryRelease(globals, release).WaitAsync();
        var allowed = Path.Combine(Path.GetTempPath(), "SourceLore.OnlineSmokeTests");
        var root = Path.Combine(allowed, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var report = Path.Combine(root, "result.txt");
        try
        {
            var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))! };
            foreach (var argument in new[] { "--smoke-online", server.Url + "/gui-workflow", Path.Combine(root, "第一 工作目录"), Path.Combine(root, "第二 工作目录"), report }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch AOT program");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(120));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(TestContext.Current.CancellationToken); throw; }
            var result = File.Exists(report) ? await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken) : "No report";
            Assert.True(process.ExitCode == 0, result); Assert.Contains("seven capabilities", result);
            TestContext.Current.TestOutputHelper!.WriteLine(result);
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(allowed) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid online smoke cleanup path");
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptNativeLibraryShowsActionableWindow(bool corrupt)
    {
        var executable = Environment.GetEnvironmentVariable("SOURCELORE_PUBLISHED_EXE")
            ?? throw new InvalidOperationException("Set SOURCELORE_PUBLISHED_EXE before running smoke tests");
        var testRoot = Path.Combine(Path.GetTempPath(), "SourceLore.DependencyTests");
        var directory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(executable))!))
                if (Path.GetExtension(file) == ".exe" || (Path.GetExtension(file) == ".dll" && Path.GetFileName(file) != "lorelib.dll"))
                    File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            if (corrupt) await File.WriteAllTextAsync(Path.Combine(directory, "lorelib.dll"), "invalid PE image", TestContext.Current.CancellationToken);
            var report = Path.Combine(directory, "result.txt");
            var start = new ProcessStartInfo(Path.Combine(directory, "SourceLore.App.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = directory
            };
            start.ArgumentList.Add("--smoke-dependency-error");
            start.ArgumentList.Add(report);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch AOT program");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                throw;
            }
            var result = File.Exists(report) ? await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken) : "No report";
            Assert.True(process.ExitCode == 0, result);
            Assert.Contains("dependency diagnostics", result);
        }
        finally
        {
            var prefix = Path.GetFullPath(testRoot) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid dependency fixture cleanup path");
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(directory, recursive: true); break; }
                catch (IOException) when (attempt < 9) { await Task.Delay(200, TestContext.Current.CancellationToken); }
            }
        }
    }

    [Fact]
    public async Task NativeAotWindowUsesRealNativeStatusDiffStagingAndCommit()
    {
        var executable = Environment.GetEnvironmentVariable("SOURCELORE_PUBLISHED_EXE");
        if (string.IsNullOrEmpty(executable))
            throw new InvalidOperationException("Set SOURCELORE_PUBLISHED_EXE to the actual NativeAOT publish before running smoke tests");
        Assert.True(File.Exists(executable), "Published executable is missing");
        var runtime = native.Runtime;
        await using var repository = new LoreFixture(runtime);
        await repository.InitializeAsync();
        await repository.WriteAsync("程序.txt", string.Join("\n", Enumerable.Range(1, 300).Select(line => "第一版 " + line)) + "\n");
        await repository.WriteAsync("保留.txt", "保留原文\n");
        await repository.Backend.StageAsync(repository.Workspace, ["程序.txt", "保留.txt"], true);
        await repository.Backend.CommitAsync(repository.Workspace, "初始版本");
        await repository.WriteAsync("程序.txt", string.Join("\n", Enumerable.Range(1, 300).Select(line => "暂存内容 " + line)) + "\n");
        await repository.WriteAsync("保留.txt", "未选的修改\n");
        using (var globals = new LoreGlobalArgs { RepositoryPath = repository.Path, Offline = true })
        using (var args = new LoreRepositoryReleaseArgs())
            await global::LoreVcs.Lore.RepositoryRelease(globals, args).WaitAsync();
        var report = Path.Combine(Path.GetTempPath(), "SourceLore.Smoke-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
            };
            start.ArgumentList.Add("--smoke-workspace");
            start.ArgumentList.Add(repository.Path);
            start.ArgumentList.Add(report);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch AOT program");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // Only this test's own child process may be stopped; no fixture is touched while it is running.
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                throw;
            }
            var text = File.Exists(report) ? await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken) : "No smoke report";
            Assert.True(process.ExitCode == 0, text);
            Assert.StartsWith("PASS: NativeAOT", text);
            TestContext.Current.TestOutputHelper!.WriteLine(text);
        }
        finally { File.Delete(report); }
    }
}
