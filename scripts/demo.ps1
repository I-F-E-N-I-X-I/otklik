Set-Location (Join-Path (Split-Path $PSScriptRoot -Parent) 'samples/demo-store')
python -m http.server 8080 --bind 127.0.0.1
