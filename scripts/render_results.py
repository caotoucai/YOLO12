import json
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
data = json.loads((root / 'artifacts/bottle-detections.json').read_text(encoding='utf-8-sig'))
image = Image.open(data['imagePath']).convert('RGB')
draw = ImageDraw.Draw(image)
for detection in data['detections']:
    b = detection['box']
    points = (b['x1'], b['y1'], b['x2'], b['y2'])
    draw.rectangle(points, outline='lime', width=3)
    draw.text((b['x1'], max(0, b['y1'] - 14)), f"{detection['className']} {detection['confidence']:.3f}", fill='lime', stroke_width=1, stroke_fill='black')
image.save(root / 'artifacts/bottle-preview.png')
