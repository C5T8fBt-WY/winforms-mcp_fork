using System.Text.Json;

/// <summary>
/// Sandbox-side shared folder transport client.
///
/// Runs inside Windows Sandbox, polls for request files from host,
/// processes them, and writes response files.
///
/// Protocol:
/// 1. Client signals ready by creating client-ready.signal
/// 2. Client polls for request-{id}.json files
/// 3. Client processes request, writes response-{id}.json
/// 4. Client stops when host-done.signal appears
/// </summary>
class Program
{
    const int PollIntervalMs = 10;

    // Inside sandbox, the shared folder is mapped to C:\Shared
    static string SharedFolder = @"C:\Shared";

    static async Task Main(string[] args)
    {
        // Write startup log to help debug sandbox issues
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "client-startup.log");
            File.WriteAllText(logPath, $"Client started at {DateTime.Now}\nArgs: {string.Join(" ", args)}\n");
        }
        catch { }

        try
        {
            await RunClient(args);
        }
        catch (Exception ex)
        {
            // Log any unhandled exception
            try
            {
                var errorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "client-error.log");
                File.WriteAllText(errorPath, $"EXCEPTION at {DateTime.Now}:\n{ex}\n");
            }
            catch { }
            throw;
        }
    }

    static async Task RunClient(string[] args)
    {
        Console.WriteLine("=== Shared Folder Transport Test - SANDBOX CLIENT ===");
        Console.WriteLine($"Shared folder: {SharedFolder}");
        Console.WriteLine($"Poll interval: {PollIntervalMs}ms");
        Console.WriteLine();

        // Allow custom shared folder path (for local testing)
        if (args.Length > 0)
        {
            SharedFolder = args[0];
            Console.WriteLine($"Using custom shared folder: {SharedFolder}");
        }

        if (!Directory.Exists(SharedFolder))
        {
            Console.WriteLine($"ERROR: Shared folder does not exist: {SharedFolder}");
            Console.WriteLine("Make sure the sandbox .wsb file maps the shared folder correctly.");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }

        // Signal that we're ready
        var readyFile = Path.Combine(SharedFolder, "client-ready.signal");
        await File.WriteAllTextAsync(readyFile, DateTime.UtcNow.ToString("o"));
        Console.WriteLine("Signaled ready to host.");
        Console.WriteLine("Waiting for requests...");
        Console.WriteLine();

        var doneFile = Path.Combine(SharedFolder, "host-done.signal");
        int requestsProcessed = 0;

        while (!File.Exists(doneFile))
        {
            // Look for request files
            var requestFiles = Directory.GetFiles(SharedFolder, "request-*.json")
                .Where(f => !f.EndsWith(".tmp"))
                .OrderBy(f => f)
                .ToList();

            foreach (var requestFile in requestFiles)
            {
                try
                {
                    await ProcessRequest(requestFile);
                    requestsProcessed++;

                    if (requestsProcessed % 10 == 0)
                    {
                        Console.WriteLine($"Processed {requestsProcessed} requests...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {Path.GetFileName(requestFile)}: {ex.Message}");
                }
            }

            if (requestFiles.Count == 0)
            {
                await Task.Delay(PollIntervalMs);
            }
        }

        // Cleanup done signal
        try { File.Delete(doneFile); } catch { }

        Console.WriteLine();
        Console.WriteLine($"=== TEST COMPLETE ===");
        Console.WriteLine($"Total requests processed: {requestsProcessed}");
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static async Task ProcessRequest(string requestFile)
    {
        // Read request
        string requestJson;
        try
        {
            requestJson = await File.ReadAllTextAsync(requestFile);
        }
        catch (IOException)
        {
            // File might still be locked by host, skip and retry next poll
            return;
        }

        // Parse request
        using var doc = JsonDocument.Parse(requestJson);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        var method = doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() : "unknown";

        // Create response
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = new
            {
                success = true,
                method = method,
                message = $"Processed by sandbox: {method}",
                timestamp = DateTime.UtcNow.ToString("o"),
                hostname = Environment.MachineName
            }
        };

        var responseJson = JsonSerializer.Serialize(response);

        // Write response atomically
        var responseFile = Path.Combine(SharedFolder, $"response-{id}.json");
        var tempFile = responseFile + ".tmp";
        await File.WriteAllTextAsync(tempFile, responseJson);
        File.Move(tempFile, responseFile, overwrite: true);

        // Delete request file (host might also try to delete it, that's fine)
        try { File.Delete(requestFile); } catch { }
    }
}
