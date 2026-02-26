using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace C5T8fBtWY.WinFormsMcp.Server.Sandbox;

/// <summary>
/// Manages Windows Sandbox lifecycle including launch, communication, and shutdown.
///
/// Communication uses shared folder polling transport:
/// - Host writes request-{id}.json to shared folder
/// - Sandbox MCP server polls for requests, writes response-{id}.json
/// - Host polls for responses
///
/// This transport is used because named pipes don't cross the sandbox VM boundary.
/// </summary>
public class SandboxManager : IDisposable
{
    private Process? _sandboxProcess;
    private string? _sharedFolderPath;
    private string? _wsbConfigPath;
    private int _nextRequestId = 1;
    private bool _disposed;

    /// <summary>
    /// Timeout for waiting for sandbox to boot and MCP server to signal ready.
    /// </summary>
    public int BootTimeoutMs { get; set; } = 60000; // 60 seconds default

    /// <summary>
    /// Interval between polls when waiting for sandbox ready signal.
    /// </summary>
    public int BootPollIntervalMs { get; set; } = 500;

    /// <summary>
    /// Timeout for individual request/response operations.
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 30000; // 30 seconds default

    /// <summary>
    /// Interval between polls when waiting for response.
    /// </summary>
    public int RequestPollIntervalMs { get; set; } = 10;

    /// <summary>
    /// Whether a sandbox is currently running.
    /// </summary>
    public bool IsRunning => _sandboxProcess != null && !_sandboxProcess.HasExited;

    /// <summary>
    /// The shared folder path used for communication.
    /// </summary>
    public string? SharedFolderPath => _sharedFolderPath;

    /// <summary>
    /// Check if Windows Sandbox is available on this system.
    /// Requires Windows 10/11 Pro, Enterprise, or Education with sandbox feature enabled.
    /// </summary>
    public bool IsSandboxAvailable() => FindWindowsSandboxExe() != null;

    /// <summary>
    /// Launch Windows Sandbox with the given configuration.
    /// </summary>
    /// <param name="wsbConfigPath">Path to the .wsb configuration file</param>
    /// <param name="sharedFolderPath">Path to the shared folder for communication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sandbox launched and MCP server is ready</returns>
    /// <exception cref="InvalidOperationException">If sandbox is already running</exception>
    /// <exception cref="FileNotFoundException">If WindowsSandbox.exe not found</exception>
    public async Task<SandboxLaunchResult> LaunchSandboxAsync(
        string wsbConfigPath,
        string sharedFolderPath,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Sandbox is already running. Call CloseSandbox first.");

        // Validate inputs
        if (!File.Exists(wsbConfigPath))
            throw new FileNotFoundException("WSB configuration file not found", wsbConfigPath);

        if (!Directory.Exists(sharedFolderPath))
            throw new DirectoryNotFoundException($"Shared folder not found: {sharedFolderPath}");

        // Find WindowsSandbox.exe
        var sandboxExe = FindWindowsSandboxExe();
        if (sandboxExe == null)
        {
            return new SandboxLaunchResult
            {
                Success = false,
                Error = "Windows Sandbox is not installed. Requires Windows 10/11 Pro, Enterprise, or Education.",
                SandboxAvailable = false
            };
        }

        // Clean up any old signal files
        CleanupSignalFiles(sharedFolderPath);

        // Launch sandbox - requires elevation (admin privileges)
        var startInfo = new ProcessStartInfo
        {
            FileName = sandboxExe,
            Arguments = $"\"{wsbConfigPath}\"",
            UseShellExecute = true, // Required for WindowsSandbox.exe and elevation
            Verb = "runas", // Elevate to administrator - required for Windows Sandbox
            WindowStyle = ProcessWindowStyle.Normal
        };

        try
        {
            _sandboxProcess = Process.Start(startInfo);
            if (_sandboxProcess == null)
            {
                return new SandboxLaunchResult
                {
                    Success = false,
                    Error = "Failed to start WindowsSandbox.exe process"
                };
            }

            _wsbConfigPath = wsbConfigPath;
            _sharedFolderPath = sharedFolderPath;

            // Wait for MCP server to signal ready
            var readySignalPath = Path.Combine(sharedFolderPath, "mcp-ready.signal");
            var waitResult = await WaitForFileAsync(readySignalPath, BootTimeoutMs, BootPollIntervalMs, cancellationToken);

            if (!waitResult)
            {
                // Sandbox process might have exited
                if (_sandboxProcess.HasExited)
                {
                    return new SandboxLaunchResult
                    {
                        Success = false,
                        Error = $"Sandbox process exited unexpectedly with code {_sandboxProcess.ExitCode}"
                    };
                }

                return new SandboxLaunchResult
                {
                    Success = false,
                    Error = $"Timeout waiting for MCP server to start (waited {BootTimeoutMs}ms). " +
                            "Check that the LogonCommand in the .wsb file is correct."
                };
            }

            // Read and delete the ready signal
            string readyContent = "";
            try
            {
                readyContent = await File.ReadAllTextAsync(readySignalPath, cancellationToken);
                File.Delete(readySignalPath);
            }
            catch { }

            return new SandboxLaunchResult
            {
                Success = true,
                ProcessId = _sandboxProcess.Id,
                SharedFolderPath = sharedFolderPath,
                SandboxAvailable = true,
                ReadySignalContent = readyContent
            };
        }
        catch (Exception ex)
        {
            _sandboxProcess?.Dispose();
            _sandboxProcess = null;
            _sharedFolderPath = null;
            _wsbConfigPath = null;

            return new SandboxLaunchResult
            {
                Success = false,
                Error = $"Failed to launch sandbox: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Send a JSON-RPC request to the MCP server in the sandbox.
    /// </summary>
    /// <param name="method">Method name</param>
    /// <param name="parameters">Method parameters (will be serialized to JSON)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-RPC response</returns>
    public async Task<JsonDocument> SendRequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _sharedFolderPath == null)
            throw new InvalidOperationException("Sandbox is not running");

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var requestFile = Path.Combine(_sharedFolderPath, $"request-{requestId}.json");
        var responseFile = Path.Combine(_sharedFolderPath, $"response-{requestId}.json");

        // Build JSON-RPC request
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = method,
            @params = parameters
        };

        var requestJson = JsonSerializer.Serialize(request);

        // Write request atomically
        var tempFile = requestFile + ".tmp";
        await File.WriteAllTextAsync(tempFile, requestJson, cancellationToken);
        File.Move(tempFile, requestFile, overwrite: true);

        // Wait for response
        var responseReceived = await WaitForFileAsync(responseFile, RequestTimeoutMs, RequestPollIntervalMs, cancellationToken);

        if (!responseReceived)
        {
            // Clean up request file
            try { File.Delete(requestFile); } catch { }

            throw new TimeoutException($"No response from sandbox MCP server after {RequestTimeoutMs}ms");
        }

        // Read response
        string responseJson;
        try
        {
            // Small delay to ensure file is fully written
            await Task.Delay(5, cancellationToken);
            responseJson = await File.ReadAllTextAsync(responseFile, cancellationToken);
        }
        catch (IOException)
        {
            // File might still be locked, retry once
            await Task.Delay(50, cancellationToken);
            responseJson = await File.ReadAllTextAsync(responseFile, cancellationToken);
        }

        // Clean up files
        try { File.Delete(requestFile); } catch { }
        try { File.Delete(responseFile); } catch { }

        return JsonDocument.Parse(responseJson);
    }

    /// <summary>
    /// Close the sandbox gracefully.
    /// </summary>
    /// <param name="timeoutMs">Timeout to wait for graceful shutdown before force kill</param>
    public async Task CloseSandboxAsync(int timeoutMs = 10000)
    {
        if (_sandboxProcess == null)
            return;

        try
        {
            // Send shutdown signal via shared folder
            if (_sharedFolderPath != null && Directory.Exists(_sharedFolderPath))
            {
                var shutdownSignal = Path.Combine(_sharedFolderPath, "shutdown.signal");
                await File.WriteAllTextAsync(shutdownSignal, DateTime.UtcNow.ToString("o"));
            }

            // Wait for process to exit gracefully
            var exited = await WaitForExitAsync(_sandboxProcess, timeoutMs);

            if (!exited)
            {
                // Force kill
                try
                {
                    _sandboxProcess.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }
            }
        }
        finally
        {
            // Clean up signal files
            if (_sharedFolderPath != null)
            {
                CleanupSignalFiles(_sharedFolderPath);
            }

            _sandboxProcess?.Dispose();
            _sandboxProcess = null;
            _sharedFolderPath = null;
            _wsbConfigPath = null;
        }
    }

    /// <summary>
    /// Find the WindowsSandbox.exe path.
    /// </summary>
    private string? FindWindowsSandboxExe()
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var sandboxPath = Path.Combine(systemRoot, "System32", "WindowsSandbox.exe");

        return File.Exists(sandboxPath) ? sandboxPath : null;
    }

    /// <summary>
    /// Wait for a file to appear.
    /// </summary>
    private async Task<bool> WaitForFileAsync(
        string filePath,
        int timeoutMs,
        int pollIntervalMs,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(filePath))
                return true;

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Wait for a process to exit.
    /// </summary>
    private async Task<bool> WaitForExitAsync(Process process, int timeoutMs)
    {
        try
        {
            return await Task.Run(() => process.WaitForExit(timeoutMs));
        }
        catch
        {
            return process.HasExited;
        }
    }

    /// <summary>
    /// Clean up signal and request/response files from previous sessions.
    /// </summary>
    private void CleanupSignalFiles(string sharedFolderPath)
    {
        try
        {
            foreach (var file in Directory.GetFiles(sharedFolderPath, "*.signal"))
            {
                try { File.Delete(file); } catch { }
            }
            foreach (var file in Directory.GetFiles(sharedFolderPath, "request-*.json"))
            {
                try { File.Delete(file); } catch { }
            }
            foreach (var file in Directory.GetFiles(sharedFolderPath, "response-*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsRunning)
        {
            // Fire-and-forget close
            _ = CloseSandboxAsync(5000);
        }

        _sandboxProcess?.Dispose();
    }
}

/// <summary>
/// Result of attempting to launch a sandbox.
/// </summary>
public class SandboxLaunchResult
{
    /// <summary>
    /// Whether the sandbox launched successfully and MCP server is ready.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if launch failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Process ID of the WindowsSandbox.exe process.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// Path to the shared folder for communication.
    /// </summary>
    public string? SharedFolderPath { get; init; }

    /// <summary>
    /// Whether Windows Sandbox is available on this system.
    /// False means Windows Home edition or sandbox not enabled.
    /// </summary>
    public bool SandboxAvailable { get; init; } = true;

    /// <summary>
    /// Content from the MCP server's ready signal (for debugging).
    /// </summary>
    public string? ReadySignalContent { get; init; }
}
