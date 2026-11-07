"""Download official COCO128 sample and YOLO12n weights; keep source provenance."""
import urllib.request
import zipfile
from pathlib import Path
import json
import subprocess

root = Path(__file__).resolve().parents[1]
sources = {
    'samples/coco128.zip': 'https://github.com/ultralytics/assets/releases/download/v0.0.0/coco128.zip',
    'models/yolo12n.pt': 'https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo12n.pt',
}
for name, url in sources.items():
    dest = root / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    valid = dest.exists() and dest.stat().st_size >= 100000
    if dest.suffix == '.zip':
        valid = valid and zipfile.is_zipfile(dest)
    if dest.suffix == '.pt':
        valid = valid and zipfile.is_zipfile(dest)
    if not valid:
        print('Downloading', url, flush=True)
        temporary = dest.with_suffix(dest.suffix + '.download')
        subprocess.run(['curl.exe', '--fail', '--location', '--retry', '2', '--connect-timeout', '20', '--max-time', '600', '--output', str(temporary), url], check=True)
        temporary.replace(dest)
with zipfile.ZipFile(root / 'samples/coco128.zip') as archive:
    archive.extractall(root / 'samples')
(root / 'samples/sources.json').write_text(json.dumps(sources, indent=2), encoding='utf-8')
print('Samples and weights ready', flush=True)
