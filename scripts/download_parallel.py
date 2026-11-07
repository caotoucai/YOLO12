"""Range download of official assets; validates every chunk before atomic replacement."""
import concurrent.futures
import math
import urllib.request
from pathlib import Path
import time

root = Path(__file__).resolve().parents[1]


def download(url, target):
    with urllib.request.urlopen(urllib.request.Request(url, method='HEAD'), timeout=45) as r:
        resolved = r.url
        size = int(r.headers['Content-Length'])
    chunk = math.ceil(size / 16)
    def part(i):
        start, end = i * chunk, min(size, (i + 1) * chunk) - 1
        for attempt in range(3):
            try:
                req = urllib.request.Request(resolved, headers={'Range': f'bytes={start}-{end}'})
                with urllib.request.urlopen(req, timeout=90) as r:
                    data = r.read()
                    if r.status != 206 or len(data) != end - start + 1:
                        raise RuntimeError(f'Bad range response: {r.status} {len(data)}')
                    return data
            except Exception:
                if attempt == 2:
                    raise
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as pool:
        pieces = list(pool.map(part, range(16)))
    temporary = target.with_suffix(target.suffix + '.download')
    temporary.write_bytes(b''.join(pieces))
    temporary.replace(target)
    print(target.name, target.stat().st_size, flush=True)


with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    jobs = [pool.submit(download, 'https://github.com/ultralytics/assets/releases/download/v0.0.0/coco128.zip', root / 'samples/coco128.zip'),
            pool.submit(download, 'https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo12n.pt', root / 'models/yolo12n.pt')]
    for job in jobs:
        job.result()
