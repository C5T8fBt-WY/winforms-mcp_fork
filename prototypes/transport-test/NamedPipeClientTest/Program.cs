using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

/// <summary>
/// Sandbox-side named pipe client for testing transport across Windows Sandbox boundary.
///
/// Purpose: Connect to host named pipe server and test JSON-RPC communication.
///
/// Usage:
/// 1. Start NamedPipeHostServer on the host
/// 2. Launch this in Windows Sandbox via .wsb file
/// 3. Observe if connection succeeds and latency measurements
///
/// Connection strategies tested:
/// 1. "." (local machine) - standard for local pipes
/// 2. Host machine name - for cross-VM scenarios
/// 3. "localhost" - alternative local reference
/// </summary>
class Program
{
    const string PipeName = "mcp-sandbox-test";
    const int BufferSize = 4096;
    const int NumTestRequests = 50;
    const int ConnectionTimeoutMs = 30000;

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Named Pipe Transport Test - SANDBOX CLIENT ===");
        Console.WriteLine($"Pipe name: {PipeName}");
        Console.WriteLine($"Test requests: {NumTestRequests}");
        Console.WriteLine();

        // Try multiple connection strategies
        string[] serverNames = { ".", "localhost", Environment.MachineName };

        foreach (var serverName in serverNames)
        {
            Console.WriteLine($"--- Attempting connection to server: '{serverName}' ---");

            try
            {
                var result = await TestConnection(serverName);
                if (result.Success)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== CONNECTION SUCCESSFUL ===");
                    Console.WriteLine($"Server name: {serverName}");
                    PrintStats(result.Latencies);

                    // Write result to file for host to read
                    await WriteResultFile(serverName, result);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine();
        }

        // All connection strategies failed
        Console.WriteLine("=== ALL CONNECTION ATTEMPTS FAILED ===");
        Console.WriteLine();
        Console.WriteLine("CONCLUSION: Named pipes likely do NOT work across Windows Sandbox boundary.");
        Console.WriteLine();
        Console.WriteLine("The sandbox VM runs in isolation. Named pipes are:");
        Console.WriteLine("  1. Local to the VM's namespace");
        Console.WriteLine("  2. Not automatically bridged to the host");
        Console.WriteLine();
        Console.WriteLine("RECOMMENDATION: Use shared folder polling transport instead.");

        await WriteFailureReport();

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static async Task<ConnectionResult> TestConnection(string serverName)
    {
        var result = new ConnectionResult();

        using var pipeClient = new NamedPipeClientStream(
            serverName,
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Console.WriteLine($"  Connecting (timeout: {ConnectionTimeoutMs}ms)...");

        var connectTask = pipeClient.ConnectAsync(ConnectionTimeoutMs);
        var timeoutTask = Task.Delay(ConnectionTimeoutMs);

        if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
        {
            throw new TimeoutException($"Connection timed out after {ConnectionTimeoutMs}ms");
        }

        await connectTask; // Will throw if connection failed

        Console.WriteLine("  Connected! Running latency tests...");
        Console.WriteLine();

        result.Success = true;

        // Run test requests
        for (int i = 1; i <= NumTestRequests; i++)
        {
            var sw = Stopwatch.StartNew();

            // Create JSON-RPC request
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = i,
                method = "test_echo",
                @params = new { request_number = i, timestamp = DateTime.UtcNow.ToString("o") }
            });

            // Send request
            byte[] requestBytes = Encoding.UTF8.GetBytes(request);
            await pipeClient.WriteAsync(requestBytes, 0, requestBytes.Length);
            await pipeClient.FlushAsync();

            // Read response
            var buffer = new byte[BufferSize];
            int bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length);

            sw.Stop();

            if (bytesRead == 0)
            {
                throw new IOException("Server disconnected (0 bytes read)");
            }

            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            double latencyMs = sw.Elapsed.TotalMilliseconds;
            result.Latencies.Add(latencyMs);

            // Log progress every 10 requests
            if (i % 10 == 0 || i == 1)
            {
                Console.WriteLine($"  Request {i}/{NumTestRequests}: {latencyMs:F2}ms");
            }
        }

        return result;
    }

    static void PrintStats(List<double> latencies)
    {
        if (latencies.Count == 0) return;

        var sorted = latencies.OrderBy(x => x).ToList();
        double avg = latencies.Average();
        double min = sorted[0];
        double max = sorted[^1];
        double p50 = sorted[sorted.Count / 2];
        double p95 = sorted[(int)(sorted.Count * 0.95)];
        double p99 = sorted[(int)(sorted.Count * 0.99)];

        Console.WriteLine("=== LATENCY STATISTICS ===");
        Console.WriteLine($"  Requests: {latencies.Count}");
        Console.WriteLine($"  Min:      {min:F2}ms");
        Console.WriteLine($"  Avg:      {avg:F2}ms");
        Console.WriteLine($"  P50:      {p50:F2}ms");
        Console.WriteLine($"  P95:      {p95:F2}ms");
        Console.WriteLine($"  P99:      {p99:F2}ms");
        Console.WriteLine($"  Max:      {max:F2}ms");
        Console.WriteLine($"  Target:   <100ms");
        Console.WriteLine($"  Result:   {(p95 < 100 ? "PASS" : "FAIL")}");
        Console.WriteLine();
    }

    static async Task WriteResultFile(string serverName, ConnectionResult result)
    {
        var sorted = result.Latencies.OrderBy(x => x).ToList();

        var report = new
        {
            test = "named_pipe_transport",
            timestamp = DateTime.UtcNow.ToString("o"),
            connection_successful = true,
            server_name = serverName,
            requests_completed = result.Latencies.Count,
            latency_ms = new
            {
                min = sorted[0],
                avg = result.Latencies.Average(),
                p50 = sorted[sorted.Count / 2],
                p95 = sorted[(int)(sorted.Count * 0.95)],
                p99 = sorted[(int)(sorted.Count * 0.99)],
                max = sorted[^1]
            },
            target_latency_ms = 100,
            pass = sorted[(int)(sorted.Count * 0.95)] < 100,
            conclusion = "Named pipe transport works across Windows Sandbox boundary"
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        // Write to Output folder (mapped from host)
        string outputPath = @"C:\Output\named-pipe-test-result.json";
        try
        {
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Result written to: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not write to {outputPath}: {ex.Message}");
            // Write to local folder as fallback
            await File.WriteAllTextAsync("named-pipe-test-result.json", json);
            Console.WriteLine("Result written to: named-pipe-test-result.json (local)");
        }
    }

    static async Task WriteFailureReport()
    {
        var report = new
        {
            test = "named_pipe_transport",
            timestamp = DateTime.UtcNow.ToString("o"),
            connection_successful = false,
            server_names_tried = new[] { ".", "localhost", Environment.MachineName },
            conclusion = "Named pipes do NOT work across Windows Sandbox VM boundary",
            recommendation = "Use shared folder polling transport instead"
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        string outputPath = @"C:\Output\named-pipe-test-result.json";
        try
        {
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"Failure report written to: {outputPath}");
        }
        catch
        {
            await File.WriteAllTextAsync("named-pipe-test-result.json", json);
            Console.WriteLine("Failure report written to: named-pipe-test-result.json (local)");
        }
    }

    class ConnectionResult
    {
        public bool Success { get; set; }
        public List<double> Latencies { get; } = new();
    }
}
