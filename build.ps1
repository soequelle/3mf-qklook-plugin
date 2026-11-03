<#
.SYNOPSIS
    Builds the plugin and packs it as dist\QuickLook.Plugin.ThreeMFViewer.qlplugin.

.PARAMETER Install
    Also copies the plugin into the QuickLook user plugin folder and restarts QuickLook.

.PARAMETER QuickLookDir
    Where QuickLook is installed (QuickLook.Common.dll is referenced from there).
#>
param(
    [switch]$Install,
    [string]$QuickLookDir = "$env:LOCALAPPDATA\Programs\QuickLook"
)

$ErrorActionPreference = 'Stop'
$name = 'QuickLook.Plugin.ThreeMFViewer'
$project = Join-Path $PSScriptRoot "src\$name\$name.csproj"
$output = Join-Path $PSScriptRoot "src\$name\bin\Release"
$dist = Join-Path $PSScriptRoot 'dist'
$package = Join-Path $dist "$name.qlplugin"

dotnet build $project -c Release -nologo "-p:QuickLookDir=$QuickLookDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# A .qlplugin is a zip with the plugin files at its root.
$files = @("$name.dll", 'QuickLook.Plugin.Metadata.config') | ForEach-Object { Join-Path $output $_ }
$files += Join-Path $PSScriptRoot 'LICENSE'
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $package) { Remove-Item $package }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($package, 'Create')
try {
    foreach ($file in $files) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, (Split-Path $file -Leaf)) | Out-Null
    }
}
finally {
    $zip.Dispose()
}
Write-Host "Packed $package"

if ($Install) {
    # Same folder the QuickLook plugin installer uses (the portable version keeps it next to the exe).
    $dataDir = if (Test-Path (Join-Path $QuickLookDir 'UserData')) { Join-Path $QuickLookDir 'UserData' } else { Join-Path $env:APPDATA 'pooi.moe\QuickLook' }
    $target = Join-Path $dataDir "QuickLook.Plugin\$name"

    $running = Get-Process QuickLook -ErrorAction SilentlyContinue
    if ($running) {
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $files $target -Force
    Write-Host "Installed to $target"

    Start-Process (Join-Path $QuickLookDir 'QuickLook.exe')
    Write-Host 'QuickLook restarted.'
}
