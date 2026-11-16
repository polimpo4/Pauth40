<#
.SYNOPSIS
    Publishes the Pauth40 Uno Platform builds.

.DESCRIPTION
    Produces the Windows single-file executable, the Linux x64 package and the
    WebAssembly bundle in ..\dist. Run from Windows with the .NET 10 SDK and the
    Android/wasm workloads installed.

.PARAMETER Runtime
    Runtime identifier used for the desktop build. Defaults to win-x64.

.PARAMETER SkipWasm
    Do not publish the WebAssembly build.

.PARAMETER SkipLinux
    Do not publish the Linux build (cross-compiled from Windows).

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Runtime win-arm64 -SkipLinux
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipWasm,
    [switch]$SkipLinux
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'Pauth40\Pauth40.csproj'
$dist = Join-Path (Split-Path -Parent $root) 'dist'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '0.0.0' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

function Publish-Target {
    param(
        [string]$Framework,
        [string]$Output,
        [string[]]$Extra = @()
    )

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Output | Out-Null

    $arguments = @(
        'publish', $project,
        '-f', $Framework,
        '-c', 'Release',
        '-o', $Output
    ) + $Extra

    Write-Host "dotnet $($arguments -join ' ')"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Framework" }
}

# ------------------------------------------------------------------- Windows
$windowsOut = Join-Path $dist "Pauth40-$version-$Runtime"
Publish-Target -Framework 'net10.0-windows10.0.26100' -Output $windowsOut -Extra @(
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:WindowsPackageType=None',
    '-p:UseMonoRuntime=false',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none'
)

Get-ChildItem $windowsOut -Filter *.pdb | Remove-Item -Force
$exe = Join-Path $windowsOut 'Pauth40.exe'
$releaseExe = Join-Path $dist "Pauth40-$version-$Runtime.exe"
Move-Item $exe $releaseExe -Force
Remove-Item $windowsOut -Recurse -Force
Write-Host "Windows: $releaseExe"

# --------------------------------------------------------------------- Linux
if (-not $SkipLinux) {
    $linuxOut = Join-Path $dist "Pauth40-$version-linux-x64"
    Publish-Target -Framework 'net10.0-desktop' -Output $linuxOut -Extra @(
        '-r', 'linux-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:UseMonoRuntime=false',
        '-p:DebugType=none'
    )

    Get-ChildItem $linuxOut -Filter *.pdb | Remove-Item -Force
    Copy-Item (Join-Path $root 'Pauth40.desktop') $linuxOut -Force
    Copy-Item (Join-Path $root 'install-linux.sh') $linuxOut -Force
    Copy-Item (Join-Path $root 'uninstall-linux.sh') $linuxOut -Force

    $icon = Join-Path $linuxOut 'Assets\app_mark.png'
    if (Test-Path $icon) { Copy-Item $icon (Join-Path $linuxOut 'pauth40.png') -Force }

    $archive = Join-Path $dist "Pauth40-$version-linux-x64.tar.gz"
    if (Test-Path $archive) { Remove-Item $archive -Force }
    tar -C $linuxOut -czf $archive .
    Write-Host "Linux: $archive"
}

# ---------------------------------------------------------------- WebAssembly
if (-not $SkipWasm) {
    $wasmOut = Join-Path $dist 'wasm'
    Publish-Target -Framework 'net10.0-browserwasm' -Output $wasmOut

    $wwwroot = Join-Path $wasmOut 'wwwroot'
    if (Test-Path $wwwroot) {
        $archive = Join-Path $dist "Pauth40-$version-wasm.zip"
        if (Test-Path $archive) { Remove-Item $archive -Force }
        Compress-Archive -Path (Join-Path $wwwroot '*') -DestinationPath $archive
        Write-Host "WebAssembly: $archive"
    }

    Remove-Item $wasmOut -Recurse -Force
}

Write-Host ''
Write-Host "Artifacts in ${dist}:"
Get-ChildItem $dist | Select-Object Name, Length | Format-Table -AutoSize
