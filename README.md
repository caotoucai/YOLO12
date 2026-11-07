# IndustrialVision — YOLO12 WPF 产品与缺陷检测

Visual Studio 2022（17.8+）打开 `IndustrialVision.sln`，将 `IndustrialVision.Wpf` 设为启动项目。使用 .NET 8、WPF 和 Python 3.11。DLL 没有 WPF 依赖，可供其他 .NET 8 应用调用。

完整的架构、模块说明、接口协议、数据格式、开发扩展、测试及部署方法见 [开发文档](DEVELOPMENT.md)；已有验证结果和精度限制见 [验证记录](VALIDATION.md)。

## 功能

- 单图多产品检测：每个目标的类别、置信度、原始图片像素坐标 `(X1,Y1,X2,Y2)`，左上角为原点，X 向右、Y 向下。
- 缺陷检测：加载自定义缺陷权重检测整张图；联合检测将缺陷中心落入的最小产品框作为所属产品，另行返回未归属缺陷。
- 图像等比显示，拖动绘制产品或缺陷矩形，选择类别、删除错误框、导入 YOLO 标签。
- 保存标准 YOLO 数据集（归一化中心坐标），支持明确的无目标负样本；训练/验证集分开，类别有序固定，重复图片按内容哈希阻止跨集合保存。
- 异步训练、日志、停止整个 Python 进程树、自动填入 best.pt，支持导出 ONNX。

## 安装与使用

```powershell
.\setup-python.ps1
dotnet build IndustrialVision.sln -c Release
dotnet run --project src/IndustrialVision.Wpf
```

若 PyPI 访问慢，可使用 `--index-url https://pypi.tuna.tsinghua.edu.cn/simple` 安装 requirements.txt。环境位于本目录 `.venv`。默认 CPU；GPU 需要根据 PyTorch 官方安装说明安装与本机匹配的 CUDA 版 PyTorch，然后将设备改为 `0`。CPU 版 PyTorch 无法使用 GPU，即使电脑有 NVIDIA 显卡。

软件启动时自动应用“单图物品检测”预设：本项目 Python 环境、`models/yolo12n.pt`、置信度 `0.25`、设备 `cpu`，关闭鼠标绘制标注框。直接点击“打开图片”，再点击“产品检测”即可看到物品名称、置信度和位置。修改设置后，可点击“恢复单图检测默认设置”重新应用预设；当前图片、检测结果和已有标注会保留。需要人工标注时，勾选“拖动鼠标绘制标注框”。

直接双击启动：`src/IndustrialVision.Wpf/bin/Release/net8.0-windows/IndustrialVision.Wpf.exe`。构建 exe 使用 `dotnet build IndustrialVision.sln -c Release -p:UseAppHost=true`。不要单独复制 exe，运行需要同目录 DLL、运行配置和项目的模型/Python 环境。运行日志保存在 `artifacts/logs`，包含启动版本、检测参数和完整异常；检测失败会弹出错误提示。无法在项目目录定位解决方案时，日志保存在系统临时目录的 `artifacts/logs`。

1. 产品测试：打开 `samples/bottles.jpg`，点击产品检测。COCO 类别包括 bottle，不包括每种工业产品或品牌。
2. 产品训练：类别输入例如 `can,bottle`，在实际图片中标注**所有**这些产品，保存到 `datasets/products`；独立图片勾选 val。至少准备非空 train 和 val，启动训练。
3. 缺陷训练：数据集目录改为 `datasets/defects`，类别例如 `scratch,dent,stain`，只框选实际瑕疵区域；无这些瑕疵的图勾选负样本后保存。勾选“训练缺陷模型”再启动训练。产品训练与缺陷训练务必用不同数据集目录。
4. 缺陷推理：加载训练得到的缺陷 `best.pt`，点击缺陷检测或联合检测，导出 JSON。

“未检出已知缺陷”仅表示当前模型和阈值下未发现学习过的缺陷，不等同质量合格，也不支持自动识别未训练的新型缺陷。框选整个不良产品可训练“有缺陷产品”定位，但要定位划痕本身，应标注划痕区域。现阶段为静态图片工作流；未接入工业相机或视频采集。

## DLL 调用

库：`src/IndustrialVision.Core/bin/Release/net8.0/IndustrialVision.Core.dll`。
分发时同时携带 `backend/worker.py`，并安装 Python/requirements；此版本 DLL 通过独立 Python 进程执行推理和训练，**不是免 Python 的原生推理 DLL**。导出的 ONNX 可交给后续 C#/ONNX Runtime 服务接入；本版本推理统一走 Ultralytics。

```csharp
using IndustrialVision.Core;

var client = new VisionClient(@"C:\app\.venv\Scripts\python.exe",
                              @"C:\app\backend\worker.py");
client.Log += Console.WriteLine;
var objects = await client.DetectAsync(@"C:\models\products.pt", @"C:\images\test.jpg");
foreach (var obj in objects.Detections)
    Console.WriteLine($"{obj.ClassName} {obj.Confidence} {obj.Box}");

var inspection = await client.InspectAsync(@"C:\models\products.pt",
    @"C:\models\defects.pt", @"C:\images\test.jpg");

using var stop = new CancellationTokenSource();
var trained = await client.TrainAsync(new TrainingOptions(
    @"C:\models\yolo12n.pt", @"C:\datasets\defects\data.yaml",
    @"C:\runs\defects", Epochs: 100, Device: "cpu"), stop.Token);

var dataset = new DatasetService();
dataset.Save(@"C:\datasets\products", @"C:\images\test.jpg", 1920, 1080,
    new[] { "bottle" }, new[] { new Annotation(0, new Box(10,20,200,400)) }, validation: false);
```

`DetectAsync` / `InspectAsync` / `TrainAsync` / `ExportOnnxAsync` 支持取消。`DatasetService.Save/Load` 管理标签。每次调用启动独立进程，适合离线单图和验证；连续产线低延迟场景需要常驻模型服务或 ONNX Runtime 扩展。联合检测使用整图缺陷模型，适用于在整图上标注训练的缺陷。

## 验证

```powershell
dotnet run --project tests/IndustrialVision.Smoke -c Release -- .
dotnet run --project tests/IndustrialVision.Smoke -c Release -- . samples/bottles.jpg --train
```

第一条验证边框转换、类别检查、数据隔离、缺陷归属；第二条通过真实 DLL/Python 调用检测瓶子并进行 1 epoch 训练、重新加载权重。1 epoch 只用于流程验证，不代表精度。

样图下载源见 `samples/sources.json`，选出的瓶子清单见 `samples/bottles-manifest.json`。COCO 图片采用原始来源的使用条款；训练标签来自 COCO128。Ultralytics/YOLO12 涉及 AGPL-3.0 或其商业授权，闭源商用部署前需确定适用授权。

YOLO12 原理与性能参考：https://docs.ultralytics.com/zh/models/yolo12 。模型以区域注意力和 R-ELAN 为核心；COCO/T4 基准不代表本机速度与工业缺陷精度。
