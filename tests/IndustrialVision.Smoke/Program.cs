using IndustrialVision.Core;
using System.Text.Json;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "artifacts", "smoke", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
void Check(bool ok, string description) { if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
var image = Path.Combine(output, "source.jpg"); File.WriteAllBytes(image, [1, 2, 3]);
var service = new DatasetService();
var sample = new Annotation(0, new(10, 20, 90, 180));
var dataset = Path.Combine(output, "dataset");
var saved = service.Save(dataset, image, 100, 200, ["bottle"], [sample], false);
var label = Path.Combine(dataset, "labels", "train", Path.GetFileNameWithoutExtension(saved) + ".txt");
var loaded = service.Load(label, 100, 200).Single();
Check(Math.Abs(loaded.Box.X1 - 10) < .001 && Math.Abs(loaded.Box.Y2 - 180) < .001, "YOLO normalization round trip");
try { service.Save(dataset, image, 100, 200, ["bottle"], [sample], true); throw new Exception("Leak permitted"); } catch (InvalidOperationException) { Console.WriteLine("PASS train/val leakage rejected"); }
try { service.Save(dataset, image, 100, 200, ["scratch"], [sample], false); throw new Exception("Schema changed"); } catch (InvalidOperationException) { Console.WriteLine("PASS class schema immutable"); }
try { service.Save(dataset, image, 100, 200, ["bottle"], [new(0, new(-1, 0, 20, 20))], false); throw new Exception("Invalid box permitted"); } catch (ArgumentException) { Console.WriteLine("PASS invalid box rejected"); }
var product = new Detection(0, "bottle", .9, new(10, 10, 90, 190));
var defect = new Detection(0, "scratch", .8, new(30, 50, 40, 60));
var outside = new Detection(0, "scratch", .7, new(95, 195, 99, 199));
var associated = VisionClient.Associate(new(image, 100, 200, [product], 1), new(image, 100, 200, [defect, outside], 1));
Check(associated.Items.Single().HasDefect && associated.UnassignedDefects.Count == 1, "defect association and unassigned defects");

if (args.Length > 1)
{
    var client = new VisionClient(Path.Combine(root, ".venv", "Scripts", "python.exe"), Path.Combine(root, "backend", "worker.py"));
    client.Log += Console.WriteLine;
    var result = await client.DetectAsync(Path.Combine(root, "models", "yolo12n.pt"), Path.GetFullPath(args[1]), .25);
    Check(result.Detections.Count > 0, "real YOLO12 inference through DLL");
    Check(result.Detections.Any(d => d.ClassName == "bottle"), "real bottle detected");
    Check(result.Detections.All(d => d.Box.X1 >= 0 && d.Box.Y1 >= 0 && d.Box.X2 <= result.Width + .01 && d.Box.Y2 <= result.Height + .01), "coordinates inside original image");
    await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "bottle-detections.json"), JsonSerializer.Serialize(result, VisionClient.JsonOptions));
    foreach (var d in result.Detections) Console.WriteLine($"{d.ClassName} {d.Confidence:F3} [{d.Box.X1:F1},{d.Box.Y1:F1},{d.Box.X2:F1},{d.Box.Y2:F1}]");
    var annotationsPath = service.Save(Path.Combine(output, "real-dataset"), result.ImagePath, result.Width, result.Height, ["bottle"], result.Detections.Where(d => d.ClassName == "bottle").Select(d => new Annotation(0, d.Box)).ToList(), false);
    Check(File.Exists(annotationsPath), "real image saved via annotation DLL (predictions for serialization test only)");
    if (args.Contains("--export"))
    {
        var onnx = await client.ExportOnnxAsync(Path.Combine(root, "models", "yolo12n.pt"));
        Check(File.Exists(onnx), "ONNX export through DLL");
    }
    if (args.Length > 2 && args[2] == "--train")
    {
        var training = await client.TrainAsync(new(Path.Combine(root, "models", "yolo12n.pt"), Path.Combine(root, "samples", "bottle-dataset", "data.yaml"), Path.Combine(root, "artifacts", "training"), 1, 320, 2));
        Check(File.Exists(training.BestWeights), "real training produced best.pt");
        var trained = await client.DetectAsync(training.BestWeights, Path.GetFullPath(args[1]));
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "trained-detections.json"), JsonSerializer.Serialize(trained, VisionClient.JsonOptions));
        Console.WriteLine("PASS trained weights inference (no accuracy claim)");
    }
}
Console.WriteLine("ALL CHECKS PASSED " + output);
