# FolderBox build script: restore, build, test and (optionally) publish a self-contained x64 folder.
# Usage: .\build.ps1 [-Configuration Release] [-Publish]
param([string]$Configuration = "Release", [switch]$Publish)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "== Build ($Configuration, x64)" -ForegroundColor Cyan
dotnet build "$root\src\FolderBox.App\FolderBox.App.csproj" -c $Configuration -p:Platform=x64 -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "== Tests" -ForegroundColor Cyan
dotnet test "$root\tests\FolderBox.Core.Tests\FolderBox.Core.Tests.csproj" -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

if ($Publish) {
    Write-Host "== Publish (self-contained win-x64)" -ForegroundColor Cyan
    dotnet publish "$root\src\FolderBox.App\FolderBox.App.csproj" -c $Configuration -p:Platform=x64 -r win-x64 --self-contained true -o "$root\publish\win-x64" -nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    Write-Host "Published to $root\publish\win-x64\FolderBox.exe" -ForegroundColor Green
}
