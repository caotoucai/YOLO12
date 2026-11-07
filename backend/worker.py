"""File-based JSON bridge. stdout/stderr are logs; result file is written only on success."""
import json
import sys
from pathlib import Path


def run(req):
    from ultralytics import YOLO
    model = YOLO(req['model'], task='detect')
    if req['command'] == 'detect':
        result = model.predict(source=req['image'], conf=req['confidence'], device=req['device'], verbose=False)[0]
        h, w = result.orig_shape
        boxes = []
        for box in result.boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            cls = int(box.cls.item())
            boxes.append(dict(classId=cls, className=result.names[cls], confidence=float(box.conf.item()),
                              box=dict(x1=x1, y1=y1, x2=x2, y2=y2)))
        return dict(imagePath=str(Path(req['image']).resolve()), width=w, height=h, detections=boxes,
                    inferenceMs=sum(result.speed.values()))
    if req['command'] == 'train':
        import yaml
        data_file = Path(req['data']).resolve()
        data = yaml.safe_load(data_file.read_text(encoding='utf-8'))
        root = Path(data.get('path', data_file.parent))
        if not root.is_absolute():
            root = data_file.parent / root
        for split in ('train', 'val'):
            folder = root / data[split]
            images = [p for p in folder.glob('*') if p.suffix.lower() in ('.jpg', '.jpeg', '.png', '.bmp')]
            if not images:
                raise ValueError(f'{split} must contain independently annotated images: {folder}')
            for image in images:
                label = root / 'labels' / split / (image.stem + '.txt')
                if not label.is_file():
                    raise ValueError(f'Missing label (negative samples require an empty label file): {label}')
        model.train(data=str(data_file), epochs=req['epochs'], imgsz=req['imageSize'], batch=req['batch'],
                    device=req['device'], project=req['output'], name='train', exist_ok=False, workers=0, seed=42)
        best = Path(model.trainer.best).resolve()
        if not best.is_file():
            raise RuntimeError('Training did not produce best.pt')
        return dict(bestWeights=str(best), saveDirectory=str(model.trainer.save_dir.resolve()))
    if req['command'] == 'export':
        # Ultralytics 8.3 uses the legacy exporter contract. Torch >=2.9 defaults to dynamo.
        import functools
        import torch
        torch.onnx.export = functools.partial(torch.onnx.export, dynamo=False)
        return str(Path(model.export(format='onnx', opset=17, dynamic=False, simplify=False, nms=False)).resolve())
    raise ValueError('Unknown command')


if __name__ == '__main__':
    request = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    result = run(request)
    Path(sys.argv[2]).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
