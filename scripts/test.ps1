$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build tests/MockLab.Tests -c Release
if ($LASTEXITCODE) { throw 'Build failed' }
dotnet tests/MockLab.Tests/bin/Release/net10.0/MockLab.Tests.dll
if ($LASTEXITCODE) { throw 'Tests failed' }
