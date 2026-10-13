using System.Runtime.InteropServices;
using LoreVcs;
using LoreVcs.Interop;
using LoreVcs.Types;

namespace SourceLore.Infrastructure.Lore;

/// <summary>One owner per application, disposed after every workspace has drained its operations.</summary>
public sealed class LoreRuntime : IDisposable
{
    public const string PackageVersion = "0.10.0";
    public const string TestedNativeVersion = "0.10.0+1172-lore_v0.10.0__urc_main";
    private bool disposed;
    private readonly object gate = new();
    private int activeOperations;
    private TaskCompletionSource idle = CompletedIdle();
    public string Version { get; }

    public LoreRuntime()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("SourceLore 首版需要 Windows x64。");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "lorelib.dll")))
            throw new InvalidOperationException("缺少 lorelib.dll。请重新解压完整 Windows x64 发行目录，不能只复制 exe。");
        try
        {
            Version = Native.LoreVersion();
            if (Version != TestedNativeVersion)
                throw new InvalidOperationException($"lorelib.dll 版本不兼容：需要 {TestedNativeVersion}，实际为 {Version}。请重新解压完整发行目录。");
            // Do not enable native file logging: SDK messages can contain credentials or auth URLs.
            using var logging = new LoreLogConfig { File = false, Level = LoreVcs.Types.Enums.LoreLogLevel.NONE };
            global::LoreVcs.Lore.LogConfigure(logging);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException("无法加载配套的 lorelib.dll。请使用完整 Windows x64 发行目录；不能只复制 exe。", ex);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            if (activeOperations != 0) throw new InvalidOperationException("Lore 操作尚未结束，不能释放运行库。");
            disposed = true;
        }
        global::LoreVcs.Lore.Shutdown();
    }

    public Task WhenIdleAsync()
    {
        lock (gate) return idle.Task;
    }

    internal Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activeOperations++ == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return Task.Run(async () =>
        {
            try { return await operation().ConfigureAwait(false); }
            finally
            {
                lock (gate)
                    if (--activeOperations == 0) idle.TrySetResult();
            }
        });
    }

    private static TaskCompletionSource CompletedIdle()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
