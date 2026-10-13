using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace SourceLore.Lore.IntegrationTests;

internal sealed class ControlledServer : IAsyncDisposable
{
    internal const string ArchiveSha256 = "E137BB4CFAA1E40D26C8421B22AC26EE95C925B13E044299778BAEF85FD74DFA";
    internal const string ExecutableSha256 = "F6310FA7D6B4F20A055C47B0CB51008932D7E7943401AF2007E5E4951D43B80A";
    private readonly string root = Path.Combine(Path.GetTempPath(), "SourceLore.ServerTests", Guid.NewGuid().ToString("N"));
    private Process? process;
    private Task<string>? output, errors;
    public string Root => root;
    public string Url { get; private set; } = "";

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task StartAsync()
    {
        var binary = Environment.GetEnvironmentVariable("SOURCELORE_SERVER_EXE") ?? throw new InvalidOperationException("Set SOURCELORE_SERVER_EXE to pinned loreserver 0.10.0; no server is downloaded implicitly.");
        await using (var stream = File.OpenRead(binary))
            if (Convert.ToHexString(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken)) != ExecutableSha256)
                throw new InvalidOperationException("Controlled server binary does not match pinned v0.10.0 release SHA-256.");
        var grpc = FreePort();
        var http = FreePort();
        Url = $"lore://127.0.0.1:{grpc}";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "temp"));
        var store = Path.Combine(root, "store").Replace('\\', '/');
        await File.WriteAllTextAsync(Path.Combine(root, "local.toml"), $$"""
            [server.quic]
            host = "127.0.0.1"
            port = {{grpc}}
            [server.grpc]
            host = "127.0.0.1"
            port = {{grpc}}
            [server.http]
            host = "127.0.0.1"
            port = {{http}}
            [immutable_store.local]
            path = "{{store}}"
            [mutable_store.local]
            path = "{{store}}"
            [lock_store.local]
            path = "{{store}}"
            """, TestContext.Current.CancellationToken);
        var start = new ProcessStartInfo(Path.GetFullPath(binary)) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--config"); start.ArgumentList.Add(root);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("LORE__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["TEMP"] = Path.Combine(root, "temp");
        start.Environment["TMP"] = Path.Combine(root, "temp");
        process = Process.Start(start) ?? throw new InvalidOperationException("Server did not start");
        output = process.StandardOutput.ReadToEndAsync(); errors = process.StandardError.ReadToEndAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (process.HasExited) throw new InvalidOperationException("Server exited: " + await output + await errors);
            try { if ((await client.GetAsync($"http://127.0.0.1:{http}/health_check", TestContext.Current.CancellationToken)).IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { }
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("Controlled server did not become healthy");
    }

    public async ValueTask DisposeAsync()
    {
        if (process is not null)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            if (output is not null) await output;
            if (errors is not null) await errors;
            process.Dispose();
        }
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SourceLore.ServerTests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid server cleanup path");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
