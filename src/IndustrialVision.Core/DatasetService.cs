using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace IndustrialVision.Core;

/// <summary>YOLO detection datasets, immutable ordered class schema, disjoint train/val by image content.</summary>
public sealed class DatasetService
{
    public string Save(string root, string imagePath, int width, int height, IReadOnlyList<string> classes, IReadOnlyList<Annotation> annotations, bool validation)
    {
        root = Path.GetFullPath(root);
        if (width <= 0 || height <= 0 || classes.Count == 0 || classes.Any(string.IsNullOrWhiteSpace) || classes.Distinct().Count() != classes.Count) throw new ArgumentException("图像尺寸或类别列表无效");
        foreach (var a in annotations)
            if (a.ClassId < 0 || a.ClassId >= classes.Count || !double.IsFinite(a.Box.X1 + a.Box.X2 + a.Box.Y1 + a.Box.Y2) || a.Box.X1 < 0 || a.Box.Y1 < 0 || a.Box.X2 > width || a.Box.Y2 > height || a.Box.Width <= 0 || a.Box.Height <= 0) throw new ArgumentException("标注类别或边框超出图片范围");
        Directory.CreateDirectory(root);
        var schema = Path.Combine(root, "classes.json");
        if (File.Exists(schema) && !JsonSerializer.Deserialize<string[]>(File.ReadAllText(schema))!.SequenceEqual(classes)) throw new InvalidOperationException("该数据集类别与当前类别顺序不一致，请使用新目录");
        var id = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).ToLowerInvariant();
        var split = validation ? "val" : "train";
        var opposite = validation ? "train" : "val";
        if (File.Exists(Path.Combine(root, "labels", opposite, id + ".txt"))) throw new InvalidOperationException("同一图片已存在于另一集合，禁止训练/验证数据泄漏");
        Directory.CreateDirectory(Path.Combine(root, "images", split)); Directory.CreateDirectory(Path.Combine(root, "labels", split));
        File.WriteAllText(schema, JsonSerializer.Serialize(classes));
        var target = Path.Combine(root, "images", split, id + Path.GetExtension(imagePath).ToLowerInvariant());
        if (!string.Equals(Path.GetFullPath(imagePath), target, StringComparison.OrdinalIgnoreCase)) File.Copy(imagePath, target, true);
        var lines = annotations.Select(a => FormattableString.Invariant($"{a.ClassId} {(a.Box.X1 + a.Box.X2) / 2 / width:F8} {(a.Box.Y1 + a.Box.Y2) / 2 / height:F8} {a.Box.Width / width:F8} {a.Box.Height / height:F8}"));
        File.WriteAllLines(Path.Combine(root, "labels", split, id + ".txt"), lines);
        var yaml = $"path: {JsonSerializer.Serialize(root.Replace('\\', '/'))}\ntrain: images/train\nval: images/val\nnames:\n" + string.Concat(classes.Select((name, i) => $"  {i}: {JsonSerializer.Serialize(name)}\n"));
        File.WriteAllText(Path.Combine(root, "data.yaml"), yaml);
        return target;
    }

    public IReadOnlyList<Annotation> Load(string labelPath, int width, int height) => File.ReadAllLines(labelPath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(line =>
    {
        var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 5) throw new InvalidDataException("YOLO 标签每行须有5列");
        double V(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
        return new Annotation(int.Parse(p[0]), new((V(1) - V(3) / 2) * width, (V(2) - V(4) / 2) * height, (V(1) + V(3) / 2) * width, (V(2) + V(4) / 2) * height));
    }).ToList();
}
