$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path -LiteralPath '.venv/Scripts/python.exe')) { python -m venv .venv }
& ./.venv/Scripts/python.exe -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) { throw 'Python dependencies installation failed' }
& ./.venv/Scripts/python.exe scripts/download_samples.py
if ($LASTEXITCODE -ne 0) { throw 'Sample download failed' }
& ./.venv/Scripts/python.exe scripts/prepare_bottles.py
if ($LASTEXITCODE -ne 0) { throw 'Sample preparation failed' }
