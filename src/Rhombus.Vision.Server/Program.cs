using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rhombus.Vision.Server;

class Program
{
    private static int _port = 5001;
    private static string _modelsPath = @"C:\Temp\RhombusVision\models";
    private static ModelSessionManager _sessionManager = new();
    private const int INPUT_SIZE = 640;
    private const float CONF_THRESHOLD = 0.25f;
    private const float IOU_THRESHOLD = 0.45f;

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Rhombus Vision Server starting ===");
        Console.WriteLine($"OnnxRuntime version: {OrtEnv.Instance().GetVersionString()}");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length) _port = int.Parse(args[++i]);
            if (args[i] == "--models" && i + 1 < args.Length) _modelsPath = args[++i];
        }

        Console.WriteLine($"Models path: {_modelsPath}");
        _sessionManager.SetModelsPath(_modelsPath);

        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Console.WriteLine($"Listening on port {_port}...");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        Console.WriteLine("Client connected.");

        try
        {
            while (client.Connected)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) break;

                var request = JObject.Parse(line);
                var method = request["method"]?.ToString();
                var id = request["id"];

                Console.WriteLine($"Method: {method}");

                object result = method switch
                {
                    "detect_elements" => await DetectElements(request["params"]),
                    "map_prompt_to_coords" => await MapPrompt(request["params"]),
                    "ping" => "pong",
                    _ => new { error = "Unknown method" }
                };

                var response = new
                {
                    jsonrpc = "2.0",
                    result = result,
                    id = id
                };

                await writer.WriteLineAsync(JsonConvert.SerializeObject(response));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in client loop: {ex.Message}");
        }
        finally
        {
            client.Close();
            Console.WriteLine("Client disconnected.");
        }
    }

    private static async Task<object> DetectElements(JToken? paramsToken)
    {
        if (paramsToken == null) return new { error = "Missing parameters" };

        var screenshotB64 = paramsToken["screenshot"]?.ToString()
                         ?? paramsToken["image_base64"]?.ToString();
        if (string.IsNullOrEmpty(screenshotB64))
            return new { error = "Missing screenshot or image_base64" };

        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Decode image
            var imageBytes = Convert.FromBase64String(screenshotB64);
            using var ms = new MemoryStream(imageBytes);
            using var originalBitmap = new Bitmap(ms);

            int originalWidth = originalBitmap.Width;
            int originalHeight = originalBitmap.Height;
            Console.WriteLine($"Image size: {originalWidth}x{originalHeight}");

            // 2. Load model (with VRAM management)
            var session = _sessionManager.GetSession(
                "omniparser_detect",
                "omniparser_detect/onnx/model.onnx"
            );

            // Get input info
            var inputMeta = session.InputMetadata.First();
            var inputName = inputMeta.Key;
            var inputShape = inputMeta.Value.Dimensions;
            Console.WriteLine($"Input: {inputName}, shape: [{string.Join(", ", inputShape)}]");

            // 3. Preprocess: resize with letterbox padding
            var (inputTensor, scale, padX, padY) = PreprocessImage(originalBitmap, INPUT_SIZE);

            // 4. Run inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            Console.WriteLine("Running inference...");
            var inferenceStart = sw.ElapsedMilliseconds;
            using var results = session.Run(inputs);
            var inferenceTime = sw.ElapsedMilliseconds - inferenceStart;
            Console.WriteLine($"Inference time: {inferenceTime}ms");

            // 5. Postprocess
            var outputTensor = results.First().AsTensor<float>();
            var detections = PostprocessDetections(
                outputTensor,
                scale, padX, padY,
                originalWidth, originalHeight
            );

            sw.Stop();
            Console.WriteLine($"Total time: {sw.ElapsedMilliseconds}ms, found {detections.Count} elements");

            return new
            {
                elements = detections,
                timing = new
                {
                    inference_ms = inferenceTime,
                    total_ms = sw.ElapsedMilliseconds
                },
                image_size = new { width = originalWidth, height = originalHeight }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Detection failed: {ex}");
            return new { error = $"Detection failed: {ex.Message}" };
        }
    }

    private static (DenseTensor<float> tensor, float scale, int padX, int padY) PreprocessImage(
        Bitmap original, int targetSize)
    {
        // Calculate scale to fit in targetSize while maintaining aspect ratio
        float scale = Math.Min(
            (float)targetSize / original.Width,
            (float)targetSize / original.Height
        );

        int newWidth = (int)(original.Width * scale);
        int newHeight = (int)(original.Height * scale);

        // Letterbox padding (center the image)
        int padX = (targetSize - newWidth) / 2;
        int padY = (targetSize - newHeight) / 2;

        // Create padded image
        using var resized = new Bitmap(targetSize, targetSize, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.FromArgb(114, 114, 114)); // YOLO gray padding
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(original, padX, padY, newWidth, newHeight);
        }

        // Convert to CHW tensor, normalized to [0, 1]
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });

        var bmpData = resized.LockBits(
            new Rectangle(0, 0, targetSize, targetSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb
        );

        try
        {
            int stride = bmpData.Stride;
            IntPtr scan0 = bmpData.Scan0;
            byte[] pixels = new byte[stride * targetSize];
            Marshal.Copy(scan0, pixels, 0, pixels.Length);

            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    int idx = y * stride + x * 3;
                    // BGR to RGB, normalize to [0, 1]
                    tensor[0, 0, y, x] = pixels[idx + 2] / 255f; // R
                    tensor[0, 1, y, x] = pixels[idx + 1] / 255f; // G
                    tensor[0, 2, y, x] = pixels[idx + 0] / 255f; // B
                }
            }
        }
        finally
        {
            resized.UnlockBits(bmpData);
        }

        return (tensor, scale, padX, padY);
    }

    private static List<object> PostprocessDetections(
        Tensor<float> output,
        float scale, int padX, int padY,
        int originalWidth, int originalHeight)
    {
        // Output shape varies by model. Common YOLO formats:
        // (1, num_classes+4, num_boxes) or (1, num_boxes, num_classes+4)
        var dims = output.Dimensions.ToArray();
        Console.WriteLine($"Output shape: [{string.Join(", ", dims)}]");

        var detections = new List<Detection>();

        if (dims.Length == 3)
        {
            int dim1 = dims[1];
            int dim2 = dims[2];

            // Determine orientation: features x boxes or boxes x features
            bool transposed = dim1 < dim2; // More boxes than features typically

            int numBoxes = transposed ? dim2 : dim1;
            int numFeatures = transposed ? dim1 : dim2;

            Console.WriteLine($"Parsing {numBoxes} boxes with {numFeatures} features (transposed={transposed})");

            for (int i = 0; i < numBoxes; i++)
            {
                // Extract features for this box
                float[] features = new float[numFeatures];
                for (int j = 0; j < numFeatures; j++)
                {
                    features[j] = transposed
                        ? output[0, j, i]
                        : output[0, i, j];
                }

                // Format: x_center, y_center, width, height, [class_scores...]
                if (numFeatures >= 5)
                {
                    float xc = features[0];
                    float yc = features[1];
                    float w = features[2];
                    float h = features[3];

                    // Get confidence (max class score for YOLO, or direct conf)
                    float conf;
                    int classId = 0;

                    if (numFeatures == 5)
                    {
                        // Single class or objectness score
                        conf = features[4];
                    }
                    else
                    {
                        // Multi-class: find max
                        conf = features[4];
                        for (int c = 5; c < numFeatures; c++)
                        {
                            if (features[c] > conf)
                            {
                                conf = features[c];
                                classId = c - 4;
                            }
                        }
                    }

                    if (conf < CONF_THRESHOLD) continue;

                    // Convert from input coords to original image coords
                    float x1 = (xc - w / 2 - padX) / scale;
                    float y1 = (yc - h / 2 - padY) / scale;
                    float x2 = (xc + w / 2 - padX) / scale;
                    float y2 = (yc + h / 2 - padY) / scale;

                    // Clip to image bounds
                    x1 = Math.Max(0, Math.Min(x1, originalWidth));
                    y1 = Math.Max(0, Math.Min(y1, originalHeight));
                    x2 = Math.Max(0, Math.Min(x2, originalWidth));
                    y2 = Math.Max(0, Math.Min(y2, originalHeight));

                    if (x2 > x1 && y2 > y1)
                    {
                        detections.Add(new Detection
                        {
                            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                            Confidence = conf,
                            ClassId = classId
                        });
                    }
                }
            }
        }

        // Apply NMS
        var nmsResult = ApplyNMS(detections, IOU_THRESHOLD);

        // Convert to output format
        return nmsResult
            .OrderByDescending(d => d.Confidence)
            .Take(100)
            .Select(d => (object)new
            {
                bounds = new[] { (int)d.X1, (int)d.Y1, (int)d.X2, (int)d.Y2 },
                confidence = Math.Round(d.Confidence, 3),
                class_id = d.ClassId
            })
            .ToList();
    }

    private static List<Detection> ApplyNMS(List<Detection> detections, float iouThreshold)
    {
        var result = new List<Detection>();
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            sorted = sorted.Where(d => ComputeIoU(best, d) < iouThreshold).ToList();
        }

        return result;
    }

    private static float ComputeIoU(Detection a, Detection b)
    {
        float x1 = Math.Max(a.X1, b.X1);
        float y1 = Math.Max(a.Y1, b.Y1);
        float x2 = Math.Min(a.X2, b.X2);
        float y2 = Math.Min(a.Y2, b.Y2);

        float intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float areaA = (a.X2 - a.X1) * (a.Y2 - a.Y1);
        float areaB = (b.X2 - b.X1) * (b.Y2 - b.Y1);
        float union = areaA + areaB - intersection;

        return union > 0 ? intersection / union : 0;
    }

    private static async Task<object> MapPrompt(JToken? paramsToken)
    {
        // YOLO-World not yet available as ONNX
        return new {
            x = 320,
            y = 240,
            confidence = 0.0,
            error = "YOLO-World ONNX model not available. Use detect_elements instead."
        };
    }

    private class Detection
    {
        public float X1, Y1, X2, Y2;
        public float Confidence;
        public int ClassId;
    }
}

public class ModelSessionManager
{
    private string _modelsPath = "";
    private string _currentModelKey = "";
    private InferenceSession? _activeSession;

    public void SetModelsPath(string path) => _modelsPath = path;

    public InferenceSession GetSession(string key, string relativePath)
    {
        if (key == _currentModelKey && _activeSession != null)
        {
            return _activeSession;
        }

        // Unload previous session to free VRAM
        if (_activeSession != null)
        {
            Console.WriteLine($"Unloading model: {_currentModelKey}");
            _activeSession.Dispose();
            _activeSession = null;
            GC.Collect(); // Help reclaim VRAM
        }

        string fullPath = Path.Combine(_modelsPath, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Model not found: {fullPath}");
        }

        Console.WriteLine($"Loading model with DirectML: {fullPath}");

        var options = new SessionOptions();
        options.AppendExecutionProvider_DML(0); // AMD GPU Device 0

        _activeSession = new InferenceSession(fullPath, options);
        _currentModelKey = key;

        // Log input/output info
        foreach (var input in _activeSession.InputMetadata)
        {
            Console.WriteLine($"  Input: {input.Key} = [{string.Join(", ", input.Value.Dimensions)}]");
        }
        foreach (var output in _activeSession.OutputMetadata)
        {
            Console.WriteLine($"  Output: {output.Key} = [{string.Join(", ", output.Value.Dimensions)}]");
        }

        return _activeSession;
    }
}
