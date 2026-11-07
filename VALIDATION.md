# 验证记录

## 2026-11-07 上传前复核

- `dotnet build IndustrialVision.sln -c Release -p:UseAppHost=true`：成功，0 警告、0 错误。本机 SDK 为 9.0.312，构建目标仍为 .NET 8。
- 运行 `dotnet tests/IndustrialVision.Smoke/bin/Release/net8.0/IndustrialVision.Smoke.dll .`：五项 Core 逻辑检查通过，输出 `ALL CHECKS PASSED`。
- 使用 Python 3.11.5 对 `backend/worker.py` 及四个 `scripts/*.py` 文件执行 AST 语法检查：全部通过。
- 尝试运行带 `samples/bottles.jpg` 的真实推理冒烟程序，两次输出均在 Ultralytics 配置初始化日志之后结束，没有输出最终完成标记；`artifacts/bottle-detections.json` 仍为历史文件。因此此次不将真实推理记为通过，原因待后续排查。
- 此次未重新运行训练、ONNX 导出、WPF 界面或 CUDA 验证；下方相应内容为历史验证记录。

## 已完成

- `dotnet build IndustrialVision.sln -c Release`：0 警告、0 错误。
- DLL 测试：YOLO 标签归一化往返、越界框拒绝、类别顺序变更拒绝、同图跨训练/验证集拒绝、缺陷归属及未归属缺陷返回。
- 从 Ultralytics 官方下载 COCO128 与 YOLO12n；提取 6 张含瓶子的真实图片及 COCO 人工标签，4 张训练、2 张验证。来源在 `samples/sources.json`，清单在 `samples/bottles-manifest.json`。
- 通过 C# DLL 调用 Python，真实图片检测成功；坐标均在原图内，真实图片与标签保存成功。
- 真实训练：1 epoch、320 输入、batch 2、CPU，生成 `artifacts/training/train/weights/best.pt`，重新加载并推理成功。
- WPF 启动、载入图片、调用检测并生成界面截图，已检查等比显示、框位置和结果列表：`artifacts/wpf-preview.png`。鼠标交互尚未做完整人工验收。
- DLL ONNX 导出成功：`models/yolo12n.onnx`，opset 17、固定 640 输入、无内嵌 NMS。修复新版 Torch 默认导出器兼容问题，使用 legacy exporter。导出文件可由 ONNX 解析；独立 checker 调用长时间未返回，未确认 checker 通过，未验证 ONNX Runtime 数值一致性。

## 观察与限制

2026-11-05 闪退排查：原先只更新了 Release DLL，可双击的 exe 仍在 Debug 目录。现已重新构建 Debug 和 Release exe（0 警告、0 错误），增加任务及未处理异常日志。自动界面验证现在触发“产品检测”按钮事件，并等待检测完成；Release 瓶子样图与 Debug 复杂厨房样图均成功返回 5 个目标，截图为 `artifacts/button-detection-preview.png` 和 `artifacts/debug-button-detection-preview.png`。此验证没有模拟实体鼠标点击，未复现用户报告的闪退，原因尚未确认；若更新后仍出现，需要读取对应会话的 `artifacts/logs`。

展示图 `samples/bottles.jpg` 为 COCO 图片 000000000283，原图 428 × 640。实体瓶子置信度约 0.945，边框 `(65.6,33.3)-(150.8,354.4)`；桌面倒影被误识别为瓶子，置信度约 0.269。阈值 0.25 时总共返回瓶子、倒影、酒杯、餐桌、椅子五个检测目标。这是通用 COCO 权重，不是专用产品模型。

复杂厨房图片 000000000164 中有 10 个瓶子人工标注，预训练模型在阈值 0.25 下只返回 1 个瓶子，说明小目标存在明显漏检。该次原始结果保存在 `artifacts/cluttered-scene-detections.json`。

短轮训练最后验证 mAP50 约 0.0227、mAP50-95 约 0.0161，仅有两张验证图，精度很低，只验证训练链路，不具备质量验收意义。

没有下载或标注真实工业瑕疵数据，未验证真实缺陷识别精度。缺陷检测复用已验证的目标检测与训练接口；缺陷归属规则有逻辑测试，但不能据此认定模型能检测划痕、凹陷等缺陷。部署前需要真实产品和瑕疵数据、独立测试集及漏检/误检指标。

当前环境为 Python 3.11.5、Ultralytics 8.3.203、Torch 2.15.0+cpu。虽然本机有 RTX 4070 Ti，本次未做 CUDA 验证。DLL 为托管 .NET 8 库，运行依赖 Python 和 backend/worker.py；未提供纯 C/C++ ABI 或免 Python 推理实现。
