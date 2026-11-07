using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IndustrialVision.Core;
using Microsoft.Win32;

namespace IndustrialVision.Wpf;
public partial class MainWindow : Window
{
    private string? imagePath;
    private readonly List<Annotation> annotations = [];
    private readonly List<Detection> detections = [];
    private object? lastResult;
    private CancellationTokenSource? active;
    private Task detectionTask = Task.CompletedTask;
    private Point? start;
    private Rectangle? preview;
    private readonly string root;
    private readonly string workspace;
    public MainWindow()
    {
        InitializeComponent();
        root = AppContext.BaseDirectory;
        while (!Directory.Exists(System.IO.Path.Combine(root, "backend")) && Directory.GetParent(root) is { } parent) root = parent.FullName;
        // Prefer workspace venv when launched from Visual Studio.
        workspace = AppContext.BaseDirectory;
        while (!File.Exists(System.IO.Path.Combine(workspace, "IndustrialVision.sln")) && Directory.GetParent(workspace) is { } p) workspace = p.FullName;
        SetDetectionDefaults();
        DatasetRoot.Text = System.IO.Path.Combine(workspace, "datasets", "products");
        UpdateClasses();
        Loaded += async (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length != 4 || args[1] != "--verify-ui") return;
            try
            {
                LoadImage(System.IO.Path.GetFullPath(args[2]));
                ProductDetectionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await detectionTask;
                if (lastResult == null) throw new InvalidOperationException("按钮检测验证失败，详情见日志：" + App.LogPath);
                UpdateLayout();
                var render = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                render.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using var file = File.Create(System.IO.Path.GetFullPath(args[3])); encoder.Save(file);
            }
            finally { Close(); }
        };
    }
    private void SetDetectionDefaults()
    {
        var python = System.IO.Path.Combine(workspace, ".venv", "Scripts", "python.exe");
        PythonPath.Text = File.Exists(python) ? python : "python";
        ProductModel.Text = System.IO.Path.Combine(workspace, "models", "yolo12n.pt");
        Confidence.Text = "0.25";
        Device.Text = "cpu";
        AnnotationMode.IsChecked = false;
        Status.Text = imagePath == null ? "单图检测就绪 · 打开图片后点击产品检测" : "默认设置已恢复 · 点击产品检测";
    }
    private void ApplyDetectionPreset(object sender, RoutedEventArgs e) => SetDetectionDefaults();
    private void Log(string text)
    {
        App.WriteLog(text);
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (Logs.Text.Length > 64000) Logs.Clear();
            Logs.AppendText(text + Environment.NewLine);
            Logs.ScrollToEnd();
        });
    }
    private VisionClient Client() { var c = new VisionClient(PythonPath.Text, System.IO.Path.Combine(root, "backend", "worker.py")); c.Log += Log; return c; }
    private async Task Run(Func<VisionClient, CancellationToken, Task> action)
    {
        if (active != null) return;
        using var cts = new CancellationTokenSource(); active = cts;
        try
        {
            Settings.IsEnabled = false; Status.Text = "执行中…";
            Log($"开始任务：图片={imagePath}；模型={ProductModel.Text}；Python={PythonPath.Text}；设备={Device.Text}；置信度={Confidence.Text}");
            await action(Client(), cts.Token); Status.Text = "完成"; Log("任务完成");
        }
        catch (OperationCanceledException) { Status.Text = "已停止"; }
        catch (Exception ex) { Status.Text = "失败，详情见日志"; Log(ex.ToString()); MessageBox.Show(this, $"{ex.Message}\n\n详细日志：{App.LogPath}", "操作失败"); }
        finally { active = null; Settings.IsEnabled = true; }
    }
    private string RequireImage() => imagePath ?? throw new InvalidOperationException("请先打开图片");
    private double Threshold() => double.Parse(Confidence.Text, System.Globalization.CultureInfo.InvariantCulture);
    private static string? Pick(string filter) { var dlg = new OpenFileDialog { Filter = filter }; return dlg.ShowDialog() == true ? dlg.FileName : null; }
    private void PickProduct(object sender, RoutedEventArgs e) { if (Pick("YOLO 模型|*.pt;*.onnx") is { } p) ProductModel.Text = p; }
    private void PickDefect(object sender, RoutedEventArgs e) { if (Pick("YOLO 模型|*.pt;*.onnx") is { } p) DefectModel.Text = p; }
    private void OpenImage(object sender, RoutedEventArgs e)
    {
        if (Pick("图片|*.jpg;*.jpeg;*.png;*.bmp") is not { } p) return;
        try
        {
            LoadImage(p);
        } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private void LoadImage(string p)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(p); bitmap.EndInit(); bitmap.Freeze();
        Photo.Source = bitmap; ImageSurface.Width = bitmap.PixelWidth; ImageSurface.Height = bitmap.PixelHeight;
        imagePath = p; annotations.Clear(); detections.Clear(); lastResult = null; Results.Items.Clear(); Negative.IsChecked = false; Redraw(); Status.Text = $"{System.IO.Path.GetFileName(p)} · {bitmap.PixelWidth} × {bitmap.PixelHeight}";
    }
    private async void DetectProducts(object sender, RoutedEventArgs e)
    {
        detectionTask = Run(async (c, t) => Show(await c.DetectAsync(ProductModel.Text, RequireImage(), Threshold(), Device.Text, t)));
        await detectionTask;
    }
    private async void DetectDefects(object sender, RoutedEventArgs e) => await Run(async (c, t) => Show(await c.DetectAsync(DefectModel.Text, RequireImage(), Threshold(), Device.Text, t)));
    private async void Inspect(object sender, RoutedEventArgs e) => await Run(async (c, t) =>
    {
        var r = await c.InspectAsync(ProductModel.Text, DefectModel.Text, RequireImage(), Threshold(), Device.Text, t);
        Show(r.Products); detections.AddRange(r.Defects.Detections); lastResult = r;
        foreach (var item in r.Items) Results.Items.Add($"{item.Product.ClassName}: {(item.HasDefect ? "检出缺陷 " + item.Defects.Count : "未检出已知缺陷")}");
        Results.Items.Add($"未归属产品的缺陷: {r.UnassignedDefects.Count}"); Redraw();
    });
    private void Show(DetectionResult r)
    {
        lastResult = r; detections.Clear(); detections.AddRange(r.Detections); Results.Items.Clear();
        Results.Items.Add($"目标 {r.Detections.Count} · 模型 {r.InferenceMs:F1} ms");
        foreach (var d in r.Detections) Results.Items.Add($"{d.ClassName} {d.Confidence:P0}\n({d.Box.X1:F1},{d.Box.Y1:F1}) → ({d.Box.X2:F1},{d.Box.Y2:F1})");
        Redraw();
    }
    private string[] ClassNames() => Classes.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private void ClassesChanged(object sender, TextChangedEventArgs e) { if (SelectedClass != null) UpdateClasses(); }
    private void UpdateClasses() { SelectedClass.ItemsSource = ClassNames(); SelectedClass.SelectedIndex = 0; }
    private Point Bounded(Point p) => new(Math.Clamp(p.X, 0, ImageSurface.Width), Math.Clamp(p.Y, 0, ImageSurface.Height));
    private void BeginBox(object sender, MouseButtonEventArgs e)
    {
        if (active != null || imagePath == null || AnnotationMode.IsChecked != true || SelectedClass.SelectedIndex < 0) return;
        start = Bounded(e.GetPosition(Overlay)); Overlay.CaptureMouse(); preview = Draw(new(start.Value.X, start.Value.Y, start.Value.X, start.Value.Y), Brushes.Yellow, null);
    }
    private void MoveBox(object sender, MouseEventArgs e)
    {
        if (start is not { } s || preview == null) return;
        var p = Bounded(e.GetPosition(Overlay)); Canvas.SetLeft(preview, Math.Min(s.X, p.X)); Canvas.SetTop(preview, Math.Min(s.Y, p.Y)); preview.Width = Math.Abs(p.X - s.X); preview.Height = Math.Abs(p.Y - s.Y);
    }
    private void EndBox(object sender, MouseButtonEventArgs e)
    {
        if (start is not { } s) return;
        var p = Bounded(e.GetPosition(Overlay)); start = null; Overlay.ReleaseMouseCapture();
        if (Math.Abs(p.X - s.X) >= 3 && Math.Abs(p.Y - s.Y) >= 3) { annotations.Add(new(SelectedClass.SelectedIndex, new(Math.Min(p.X, s.X), Math.Min(p.Y, s.Y), Math.Max(p.X, s.X), Math.Max(p.Y, s.Y)))); Negative.IsChecked = false; }
        Redraw();
    }
    private Rectangle Draw(Box b, Brush color, string? label)
    {
        var scale = Math.Max(1, ImageSurface.Width / 700);
        var rectangle = new Rectangle { Width = b.Width, Height = b.Height, Stroke = color, StrokeThickness = 2 * scale, IsHitTestVisible = false };
        Canvas.SetLeft(rectangle, b.X1); Canvas.SetTop(rectangle, b.Y1); Overlay.Children.Add(rectangle);
        if (label != null) { var text = new TextBlock { Text = label, Foreground = color, Background = Brushes.Black, FontSize = 13 * scale, IsHitTestVisible = false }; Canvas.SetLeft(text, b.X1); Canvas.SetTop(text, Math.Max(0, b.Y1 - 20 * scale)); Overlay.Children.Add(text); }
        return rectangle;
    }
    private void Redraw()
    {
        Overlay.Children.Clear(); Annotations.Items.Clear();
        foreach (var d in detections) Draw(d.Box, Brushes.LimeGreen, $"{d.ClassName} {d.Confidence:P0}");
        var classes = ClassNames();
        foreach (var a in annotations) { var name = a.ClassId < classes.Length ? classes[a.ClassId] : $"class {a.ClassId}"; Draw(a.Box, Brushes.Orange, name); Annotations.Items.Add($"{name} ({a.Box.X1:F0},{a.Box.Y1:F0})-({a.Box.X2:F0},{a.Box.Y2:F0})"); }
    }
    private void DeleteAnnotation(object sender, RoutedEventArgs e) { if (Annotations.SelectedIndex is var i && i >= 0) { annotations.RemoveAt(i); Redraw(); } }
    private void ClearAnnotations(object sender, RoutedEventArgs e) { annotations.Clear(); Redraw(); }
    private void SaveAnnotations(object sender, RoutedEventArgs e)
    {
        try {
            if (annotations.Count == 0 && Negative.IsChecked != true) throw new InvalidOperationException("请绘制标注，或明确勾选负样本");
            var target = new DatasetService().Save(DatasetRoot.Text, RequireImage(), (int)ImageSurface.Width, (int)ImageSurface.Height, ClassNames(), annotations, Validation.IsChecked == true);
            Log("已保存图片、YOLO 标签和 data.yaml：" + target); Status.Text = "标注已保存";
        } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private void ImportLabels(object sender, RoutedEventArgs e)
    {
        try { RequireImage(); if (Pick("YOLO 标签|*.txt") is not { } p) return; var loaded = new DatasetService().Load(p, (int)ImageSurface.Width, (int)ImageSurface.Height); annotations.Clear(); annotations.AddRange(loaded); Redraw(); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private async void Train(object sender, RoutedEventArgs e) => await Run(async (c, t) =>
    {
        var defect = TrainDefect.IsChecked == true;
        var r = await c.TrainAsync(new(ProductModel.Text, System.IO.Path.Combine(DatasetRoot.Text, "data.yaml"), System.IO.Path.Combine(DatasetRoot.Text, "runs"), int.Parse(Epochs.Text), int.Parse(ImageSize.Text), int.Parse(Batch.Text), Device.Text), t);
        if (defect) DefectModel.Text = r.BestWeights; else ProductModel.Text = r.BestWeights; Log("训练权重：" + r.BestWeights);
    });
    private async void ExportOnnx(object sender, RoutedEventArgs e) => await Run(async (c, t) => Log("ONNX：" + await c.ExportOnnxAsync(TrainDefect.IsChecked == true ? DefectModel.Text : ProductModel.Text, t)));
    private void ExportResults(object sender, RoutedEventArgs e)
    {
        if (lastResult == null) { MessageBox.Show("请先执行检测"); return; }
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "detections.json" };
        if (dlg.ShowDialog() == true) { try { File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(lastResult, VisionClient.JsonOptions)); } catch (Exception ex) { MessageBox.Show(ex.Message); } }
    }
    private void Cancel(object sender, RoutedEventArgs e) => active?.Cancel();
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => active?.Cancel();
}
