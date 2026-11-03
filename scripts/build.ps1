$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build -c Release
if ($LASTEXITCODE) { throw 'Build failed' }
