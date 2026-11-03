param([switch]$SelfContained, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$mode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$out = if ($OutputDirectory) { $OutputDirectory } else { "artifacts/Отклик-win-x64-$mode" }
dotnet publish src/MockLab.Desktop -c Release -r win-x64 --self-contained $SelfContained.IsPresent -o $out -m:1 -p:NuGetAudit=false
if ($LASTEXITCODE) { throw 'Publish failed' }
New-Item -ItemType Directory -Force -Path "$out/samples","$out/docs" | Out-Null
Copy-Item samples/* "$out/samples" -Recurse -Force
Get-ChildItem -LiteralPath docs | Where-Object { $_.Name -ne 'IMPLEMENTATION_PLAN.md' } | Copy-Item -Destination "$out/docs" -Recurse -Force
Copy-Item scripts/install.ps1,scripts/uninstall.ps1 $out -Force
Copy-Item README.md $out -Force
Compress-Archive -Path "$out/*" -DestinationPath "$out.zip" -Force
