param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish,
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Pauth40.csproj'
$output = Join-Path $PSScriptRoot 'dist'

Write-Host "Building Pauth40 ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo

if ($Publish) {
    Write-Host "Publishing self-contained single-file app to $output..." -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $output `
        --nologo
}

Write-Host "Done." -ForegroundColor Green
