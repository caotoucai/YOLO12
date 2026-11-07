namespace IndustrialVision.Core;

public record Box(double X1, double Y1, double X2, double Y2)
{
    public double Width => X2 - X1;
    public double Height => Y2 - Y1;
    public bool ContainsCenter(Box other) => (other.X1 + other.X2) / 2 >= X1 && (other.X1 + other.X2) / 2 <= X2 && (other.Y1 + other.Y2) / 2 >= Y1 && (other.Y1 + other.Y2) / 2 <= Y2;
}
public record Detection(int ClassId, string ClassName, double Confidence, Box Box);
public record DetectionResult(string ImagePath, int Width, int Height, List<Detection> Detections, double InferenceMs);
public record ProductInspection(Detection Product, List<Detection> Defects)
{
    public bool HasDefect => Defects.Count > 0;
}
public record InspectionResult(DetectionResult Products, DetectionResult Defects, List<ProductInspection> Items, List<Detection> UnassignedDefects);
public record Annotation(int ClassId, Box Box);
public record TrainingOptions(string Model, string DatasetYaml, string OutputDirectory, int Epochs = 100, int ImageSize = 640, int Batch = 4, string Device = "cpu");
public record TrainingResult(string BestWeights, string SaveDirectory);
