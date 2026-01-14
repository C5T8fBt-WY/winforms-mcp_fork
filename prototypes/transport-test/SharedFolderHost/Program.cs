using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Host-side shared folder transport for MCP communication with Windows Sandbox.
///
/// Protocol:
/// 1. Host writes request-{id}.json to shared folder
/// 2. Sandbox polls for request files, processes them
/// 3. Sandbox writes response-{id}.json
/// 4. Host polls for response, reads it, deletes both files
///
/// This is the fallback transport when named pipes don't work across VM boundary.
/// </summary>
class Program
{
    const int NumTestRequests = 50;
    const int PollIntervalMs = 10;
    const int ResponseTimeoutMs = 30000;

    static string SharedFolder = @"C:\TransportTest\Shared";

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Shared Folder Transport Test - HOST ===");
        Console.WriteLine($"Shared folder: {SharedFolder}");
        Console.WriteLine($"Test requests: {NumTestRequests}");
        Console.WriteLine($"Poll interval: {PollIntervalMs}ms");
        Console.WriteLine();

        // Allow custom shared folder path
        if (args.Length > 0)
        {
            SharedFolder = args[0];
            Console.WriteLine($"Using custom shared folder: {SharedFolder}");
        }

        // Ensure shared folder exists and is clean
        if (!Directory.Exists(SharedFolder))
        {
            Directory.CreateDirectory(SharedFolder);
        }
        CleanupOldFiles();

        Console.WriteLine("Waiting for sandbox client to start...");
        Console.WriteLine("(Launch Windows Sandbox with SharedFolderClient now)");
        Console.WriteLine();

        // Wait for client ready signal
        var readyFile = Path.Combine(SharedFolder, "client-ready.signal");
        var readyTimeout = DateTime.UtcNow.AddSeconds(120);

        while (!File.Exists(readyFile) && DateTime.UtcNow < readyTimeout)
        {
            await Task.Delay(500);
            Console.Write(".");
        }
        Console.WriteLine();

        if (!File.Exists(readyFile))
        {
            Console.WriteLine("ERROR: Timeout waiting for client. Is the sandbox running?");
            return;
        }

        File.Delete(readyFile);
        Console.WriteLine("Client ready! Starting transport test...");
        Console.WriteLine();

        var latencies = new List<double>();
        var errors = 0;

        for (int i = 1; i <= NumTestRequests; i++)
        {
            try
            {
                var latency = await SendRequest(i);
                latencies.Add(latency);

                if (i % 10 == 0 || i == 1)
                {
                    Console.WriteLine($"Request {i}/{NumTestRequests}: {latency:F2}ms");
                }
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"Request {i} FAILED: {ex.Message}");
            }
        }

        // Signal client to stop
        await File.WriteAllTextAsync(Path.Combine(SharedFolder, "host-done.signal"), "done");

        Console.WriteLine();
        PrintStats(latencies, errors);
        await WriteResultFile(latencies, errors);
    }

    static async Task<double> SendRequest(int requestId)
    {
        var sw = Stopwatch.StartNew();
        var requestFile = Path.Combine(SharedFolder, $"request-{requestId}.json");
        var responseFile = Path.Combine(SharedFolder, $"response-{requestId}.json");

        // Create JSON-RPC request
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method = "test_echo",
            @params = new
            {
                request_number = requestId,
                timestamp = DateTime.UtcNow.ToString("o")
            }
        };

        var json = JsonSerializer.Serialize(request);

        // Write request file atomically (write to temp, then rename)
        var tempFile = requestFile + ".tmp";
        await File.WriteAllTextAsync(tempFile, json);
        File.Move(tempFile, requestFile, overwrite: true);

        // Poll for response
        var timeout = DateTime.UtcNow.AddMilliseconds(ResponseTimeoutMs);
        while (!File.Exists(responseFile) && DateTime.UtcNow < timeout)
        {
            await Task.Delay(PollIntervalMs);
        }

        if (!File.Exists(responseFile))
        {
            throw new TimeoutException($"No response after {ResponseTimeoutMs}ms");
        }

        // Read and validate response
        string responseJson;
        try
        {
            // Small delay to ensure file is fully written
            await Task.Delay(5);
            responseJson = await File.ReadAllTextAsync(responseFile);
        }
        catch (IOException)
        {
            // File might still be locked, retry once
            await Task.Delay(50);
            responseJson = await File.ReadAllTextAsync(responseFile);
        }

        sw.Stop();

        // Cleanup
        try { File.Delete(requestFile); } catch { }
        try { File.Delete(responseFile); } catch { }

        // Validate response
        using var doc = JsonDocument.Parse(responseJson);
        var responseId = doc.RootElement.GetProperty("id").GetInt32();
        if (responseId != requestId)
        {
            throw new InvalidOperationException($"Response ID mismatch: expected {requestId}, got {responseId}");
        }

        return sw.Elapsed.TotalMilliseconds;
    }

    static void CleanupOldFiles()
    {
        try
        {
            foreach (var file in Directory.GetFiles(SharedFolder, "*.json"))
            {
                File.Delete(file);
            }
            // Don't delete client-ready.signal - we need it!
            foreach (var file in Directory.GetFiles(SharedFolder, "*.signal"))
            {
                if (!file.EndsWith("client-ready.signal"))
                {
                    File.Delete(file);
                }
            }
        }
        catch { }
    }

    static void PrintStats(List<double> latencies, int errors)
    {
        Console.WriteLine("=== SHARED FOLDER TRANSPORT RESULTS ===");
        Console.WriteLine();

        if (latencies.Count == 0)
        {
            Console.WriteLine("No successful requests.");
            return;
        }

        var sorted = latencies.OrderBy(x => x).ToList();
        double avg = latencies.Average();
        double min = sorted[0];
        double max = sorted[^1];
        double p50 = sorted[sorted.Count / 2];
        double p95 = sorted[(int)(sorted.Count * 0.95)];
        double p99 = sorted[(int)(sorted.Count * 0.99)];

        Console.WriteLine($"Successful requests: {latencies.Count}");
        Console.WriteLine($"Failed requests:     {errors}");
        Console.WriteLine();
        Console.WriteLine("Latency Statistics:");
        Console.WriteLine($"  Min:  {min:F2}ms");
        Console.WriteLine($"  Avg:  {avg:F2}ms");
        Console.WriteLine($"  P50:  {p50:F2}ms");
        Console.WriteLine($"  P95:  {p95:F2}ms");
        Console.WriteLine($"  P99:  {p99:F2}ms");
        Console.WriteLine($"  Max:  {max:F2}ms");
        Console.WriteLine();
        Console.WriteLine($"Target: <500ms P95 (acceptable for file-based transport)");
        Console.WriteLine($"Result: {(p95 < 500 ? "PASS" : "FAIL")}");
        Console.WriteLine();

        // Compare to named pipe target
        if (p95 < 100)
        {
            Console.WriteLine("Note: Latency is low enough to match named pipe target (<100ms)!");
        }
        else
        {
            Console.WriteLine($"Note: {p95 - 100:F0}ms higher than named pipe target (100ms).");
            Console.WriteLine("      This is expected for file-based polling transport.");
        }
    }

    static async Task WriteResultFile(List<double> latencies, int errors)
    {
        if (latencies.Count == 0) return;

        var sorted = latencies.OrderBy(x => x).ToList();

        var report = new
        {
            test = "shared_folder_transport",
            timestamp = DateTime.UtcNow.ToString("o"),
            transport_type = "file_polling",
            poll_interval_ms = PollIntervalMs,
            requests_successful = latencies.Count,
            requests_failed = errors,
            latency_ms = new
            {
                min = sorted[0],
                avg = latencies.Average(),
                p50 = sorted[sorted.Count / 2],
                p95 = sorted[(int)(sorted.Count * 0.95)],
                p99 = sorted[(int)(sorted.Count * 0.99)],
                max = sorted[^1]
            },
            target_latency_ms = 500,
            pass = sorted[(int)(sorted.Count * 0.95)] < 500,
            conclusion = sorted[(int)(sorted.Count * 0.95)] < 500
                ? "Shared folder transport is viable for MCP communication"
                : "Shared folder transport has high latency, consider optimization"
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var outputPath = Path.Combine(SharedFolder, "shared-folder-test-result.json");
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Results written to: {outputPath}");
    }
}
