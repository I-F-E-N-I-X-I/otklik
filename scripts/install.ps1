param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Отклик'), [switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
$sourcePath = [IO.Path]::GetFullPath($PSScriptRoot)
if ($destinationPath -eq $sourcePath) { throw 'Extract the package to a different folder before installation.' }
if (!(Test-Path -LiteralPath (Join-Path $sourcePath 'Отклик.exe'))) { throw 'Run install.ps1 from the published package.' }
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$manifest = @()
foreach ($item in Get-ChildItem -LiteralPath $sourcePath -File -Recurse) {
    $relative = $item.FullName.Substring($sourcePath.Length + 1)
    if ($relative -eq 'installed-files.json') { continue }
    $outputPath = Join-Path $destinationPath $relative
    New-Item -ItemType Directory -Path (Split-Path $outputPath -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $item.FullName -Destination $outputPath -Force
    $manifest += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash }
}
$shortcutPath = $null
if (!$NoShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Отклик.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcutPath)
    $link.TargetPath = Join-Path $destinationPath 'Отклик.exe'
    $link.WorkingDirectory = $destinationPath
    $link.Save()
}
@{ files = $manifest; shortcut = $shortcutPath } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destinationPath 'installed-files.json') -Encoding UTF8
Write-Host "Installed: $destinationPath"
