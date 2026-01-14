using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

/// <summary>
/// Host-side named pipe server for testing transport across Windows Sandbox boundary.
///
/// Purpose: Determine if named pipes can communicate across the VM boundary
/// between the host OS and Windows Sandbox.
///
/// Usage:
/// 1. Run this on the host: dotnet run
/// 2. Launch Windows Sandbox with NamedPipeClientTest
/// 3. Observe if connection is established and latency measurements
/// </summary>
class Program
{
    const string PipeName = "mcp-sandbox-test";
    const int BufferSize = 4096;

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Named Pipe Transport Test - HOST SERVER ===");
        Console.WriteLine($"Pipe name: \\\\.\\pipe\\{PipeName}");
        Console.WriteLine();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nShutting down...");
        };

        try
        {
            await RunServer(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Server stopped.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static async Task RunServer(CancellationToken ct)
    {
        int requestCount = 0;
        var latencies = new List<double>();

        while (!ct.IsCancellationRequested)
        {
            Console.WriteLine("Creating named pipe server...");

            // Create pipe with security settings that allow connections from other users
            // (sandbox runs as WDAGUtilityAccount, different from host user)
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                "Everyone",
                PipeAccessRights.ReadWrite,
                System.Security.AccessControl.AccessControlType.Allow));

            using var pipeServer = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                1, // maxNumberOfServerInstances
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous,
                BufferSize,
                BufferSize,
                pipeSecurity);

            Console.WriteLine("Waiting for client connection...");
            Console.WriteLine("(Start Windows Sandbox with NamedPipeClientTest now)");
            Console.WriteLine();

            try
            {
                await pipeServer.WaitForConnectionAsync(ct);
                Console.WriteLine("CLIENT CONNECTED!");
                Console.WriteLine();

                // Handle messages until client disconnects
                while (pipeServer.IsConnected && !ct.IsCancellationRequested)
                {
                    var sw = Stopwatch.StartNew();

                    // Read request
                    var buffer = new byte[BufferSize];
                    int bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine("Client disconnected (0 bytes read).");
                        break;
                    }

                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    requestCount++;

                    Console.WriteLine($"[Request #{requestCount}]");
                    Console.WriteLine($"  Received: {request}");

                    // Parse JSON-RPC request and create response
                    JsonDocument? requestDoc = null;
                    string response;
                    try
                    {
                        requestDoc = JsonDocument.Parse(request);
                        var id = requestDoc.RootElement.GetProperty("id").GetInt32();
                        var method = requestDoc.RootElement.GetProperty("method").GetString();

                        // Create JSON-RPC response
                        var responseObj = new
                        {
                            jsonrpc = "2.0",
                            id = id,
                            result = new
                            {
                                success = true,
                                method = method,
                                message = $"Echo from host: {method}",
                                timestamp = DateTime.UtcNow.ToString("o"),
                                request_number = requestCount
                            }
                        };
                        response = JsonSerializer.Serialize(responseObj);
                    }
                    catch (Exception ex)
                    {
                        response = JsonSerializer.Serialize(new
                        {
                            jsonrpc = "2.0",
                            id = (int?)null,
                            error = new { code = -32700, message = $"Parse error: {ex.Message}" }
                        });
                    }
                    finally
                    {
                        requestDoc?.Dispose();
                    }

                    // Send response
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await pipeServer.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                    await pipeServer.FlushAsync(ct);

                    sw.Stop();
                    double latencyMs = sw.Elapsed.TotalMilliseconds;
                    latencies.Add(latencyMs);

                    Console.WriteLine($"  Sent: {response}");
                    Console.WriteLine($"  Latency: {latencyMs:F2}ms");
                    Console.WriteLine();

                    // Print stats every 10 requests
                    if (requestCount % 10 == 0)
                    {
                        PrintStats(latencies);
                    }
                }
            }
            catch (IOException ex) when (ex.Message.Contains("pipe has been ended"))
            {
                Console.WriteLine("Client disconnected (pipe ended).");
            }

            Console.WriteLine();
            if (latencies.Count > 0)
            {
                PrintStats(latencies);
            }
        }
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
}
