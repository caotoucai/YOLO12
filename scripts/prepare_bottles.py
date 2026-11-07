"""Select real COCO bottle images and existing human annotations; no generated labels."""
from pathlib import Path
import json
import shutil
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
source = root / 'samples/coco128'
dest = root / 'samples/bottle-dataset'
entries = []
for label in sorted((source / 'labels/train2017').glob('*.txt')):
    lines = [line.split() for line in label.read_text().splitlines() if line.strip()]
    bottles = [line for line in lines if line[0] == '39']
    if bottles:
        entries.append((label, bottles))
if len(entries) < 2:
    raise RuntimeError('Need at least two independent bottle images')
manifest = []
for i, (label, boxes) in enumerate(entries):
    split = 'val' if i % 3 == 0 else 'train'
    for folder in ('images', 'labels'):
        (dest / folder / split).mkdir(parents=True, exist_ok=True)
    image = source / 'images/train2017' / (label.stem + '.jpg')
    shutil.copy2(image, dest / 'images' / split / image.name)
    (dest / 'labels' / split / label.name).write_text('\n'.join('0 ' + ' '.join(b[1:]) for b in boxes) + '\n')
    manifest.append(dict(image=str(image), split=split, bottles=len(boxes)))
(dest / 'data.yaml').write_text('path: ' + json.dumps(str(dest).replace('\\', '/')) + '\ntrain: images/train\nval: images/val\nnames:\n  0: bottle\n', encoding='utf-8')
(dest / 'classes.json').write_text('["bottle"]')
(root / 'samples/bottles-manifest.json').write_text(json.dumps(manifest, indent=2))
# Use image containing most bottle annotations for demonstration, not accuracy measurement.
best = next((label for label, _ in entries if label.stem == '000000000283'), entries[0][0])
shutil.copy2(source / 'images/train2017' / (best.stem + '.jpg'), root / 'samples/bottles.jpg')
print(json.dumps(manifest, indent=2))
