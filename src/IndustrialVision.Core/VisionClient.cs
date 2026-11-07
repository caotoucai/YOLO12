using System.Diagnostics;
using System.Text.Json;

namespace IndustrialVision.Core;

/// <summary>Reusable .NET API. Requires Python + Ultralytics; coordinates are original-image pixels.</summary>
public sealed class VisionClient(string pythonExecutable, string workerPath)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public event Action<string>? Log;

    public Task<DetectionResult> DetectAsync(string model, string image, double confidence = .25, string device = "cpu", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(model)) throw new FileNotFoundException("请选择已下载或训练完成的模型", model);
        if (!File.Exists(image)) throw new FileNotFoundException("图片不存在", image);
        if (confidence <= 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        return RunAsync<DetectionResult>(new { command = "detect", model = Path.GetFullPath(model), image = Path.GetFullPath(image), confidence, device }, cancellationToken);
    }

    public async Task<InspectionResult> InspectAsync(string productModel, string defectModel, string image, double confidence = .25, string device = "cpu", CancellationToken cancellationToken = default)
    {
        var products = await DetectAsync(productModel, image, confidence, device, cancellationToken);
        var defects = await DetectAsync(defectModel, image, confidence, device, cancellationToken);
        return Associate(products, defects);
    }

    // Each defect belongs to the smallest product containing its center. Coordinates remain global.
    public static InspectionResult Associate(DetectionResult products, DetectionResult defects)
    {
        var items = products.Detections.Select(p => new ProductInspection(p, [])).ToList();
        var unassigned = new List<Detection>();
        foreach (var defect in defects.Detections)
        {
            var owner = items.Where(p => p.Product.Box.ContainsCenter(defect.Box)).OrderBy(p => p.Product.Box.Width * p.Product.Box.Height).FirstOrDefault();
            if (owner is null) unassigned.Add(defect); else owner.Defects.Add(defect);
        }
        return new(products, defects, items, unassigned);
    }

    public Task<TrainingResult> TrainAsync(TrainingOptions options, CancellationToken cancellationToken = default)
    {
        if (options.Epochs < 1 || options.ImageSize < 32 || options.ImageSize % 32 != 0 || options.Batch < 1) throw new ArgumentException("训练轮数、批量须为正数，图像尺寸须为32的正整数倍");
        if (!File.Exists(options.DatasetYaml)) throw new FileNotFoundException("数据集配置不存在", options.DatasetYaml);
        return RunAsync<TrainingResult>(new { command = "train", model = options.Model, data = Path.GetFullPath(options.DatasetYaml), output = Path.GetFullPath(options.OutputDirectory), options.Epochs, options.ImageSize, options.Batch, options.Device }, cancellationToken);
    }

    public Task<string> ExportOnnxAsync(string model, CancellationToken cancellationToken = default) => RunAsync<string>(new { command = "export", model = Path.GetFullPath(model) }, cancellationToken);

    private async Task<T> RunAsync<T>(object request, CancellationToken token)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "IndustrialVision", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var requestPath = Path.Combine(temporary, "request.json");
            var resultPath = Path.Combine(temporary, "result.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), token);
            var start = new ProcessStartInfo(pythonExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["PYTHONUTF8"] = "1";
            var config = Path.Combine(temporary, "config"); Directory.CreateDirectory(config);
            start.Environment["YOLO_CONFIG_DIR"] = config;
            start.Environment["MPLCONFIGDIR"] = config;
            start.ArgumentList.Add(Path.GetFullPath(workerPath)); start.ArgumentList.Add(requestPath); start.ArgumentList.Add(resultPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Python");
            using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = new System.Text.StringBuilder();
            async Task Drain(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line) { lock (errors) { errors.AppendLine(line); if (errors.Length > 24000) errors.Remove(0, errors.Length - 16000); } Log?.Invoke(line); }
            }
            await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(token));
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !File.Exists(resultPath)) throw new InvalidOperationException($"Python 执行失败 ({process.ExitCode})：{errors}");
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(resultPath, token), JsonOptions) ?? throw new InvalidDataException("模型未返回有效结果");
        }
        finally { Directory.Delete(temporary, true); }
    }
}
