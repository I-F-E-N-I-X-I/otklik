$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($PSScriptRoot)
$manifestPath = Join-Path $target 'installed-files.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Installation manifest not found. Portable files can be removed manually.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
# Validate every path before deleting anything. Never recursively remove the install folder.
$checked = @()
foreach ($entry in $manifest.files) {
    $candidate = [IO.Path]::GetFullPath((Join-Path $target $entry.path))
    if (!$candidate.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path' }
    $cursor = Get-Item -LiteralPath (Split-Path $candidate -Parent)
    while ($cursor -and $cursor.FullName.Length -ge $target.Length) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse point in installation path' }
        $cursor = $cursor.Parent
    }
    $checked += @{ path = $candidate; sha256 = $entry.sha256 }
}
foreach ($entry in $checked) {
    if (Test-Path -LiteralPath $entry.path -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash -eq $entry.sha256) {
            Remove-Item -LiteralPath $entry.path -Force
        } else { Write-Host "Preserved modified file: $($entry.path)" }
    }
}
if ($manifest.shortcut -and (Test-Path -LiteralPath $manifest.shortcut)) {
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($manifest.shortcut).TargetPath -eq (Join-Path $target 'Отклик.exe')) { Remove-Item -LiteralPath $manifest.shortcut }
}
Remove-Item -LiteralPath $manifestPath -Force
Write-Host 'Application files removed. User-created and modified files were preserved.'
