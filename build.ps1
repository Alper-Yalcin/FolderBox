# FolderBox build script: restore, build, test and (optionally) publish a self-contained x64 folder,
# then (optionally) build the installer and the portable zip.
# Usage: .\build.ps1 [-Configuration Release] [-Publish] [-Installer] [-Version 1.0.0]
param([string]$Configuration = "Release", [switch]$Publish, [switch]$Installer, [string]$Version = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
}

Write-Host "== Build ($Configuration, x64, v$Version)" -ForegroundColor Cyan
dotnet build "$root\src\FolderBox.App\FolderBox.App.csproj" -c $Configuration -p:Platform=x64 -p:Version=$Version -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "== Tests" -ForegroundColor Cyan
dotnet test "$root\tests\FolderBox.Core.Tests\FolderBox.Core.Tests.csproj" -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

if ($Publish -or $Installer) {
    Write-Host "== Publish (self-contained win-x64)" -ForegroundColor Cyan
    dotnet publish "$root\src\FolderBox.App\FolderBox.App.csproj" -c $Configuration -p:Platform=x64 -p:Version=$Version -r win-x64 --self-contained true -o "$root\publish\win-x64" -nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    Write-Host "Published to $root\publish\win-x64\FolderBox.exe" -ForegroundColor Green
}

if ($Installer) {
    Write-Host "== Installer + portable zip" -ForegroundColor Cyan
    $iscc = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isinfo.php" }

    & $iscc /Q "/DAppVersion=$Version" "$root\installer\FolderBox.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    $setup = "$root\publish\FolderBox-Setup-$Version.exe"

    $zip = "$root\publish\FolderBox-$Version-win-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$root\publish\win-x64\*" -DestinationPath $zip -CompressionLevel Optimal

    foreach ($f in $setup, $zip) {
        $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
        "$hash  $(Split-Path $f -Leaf)" | Out-File -Encoding ascii "$f.sha256"
        Write-Host ("{0}  {1:N1} MB" -f (Split-Path $f -Leaf), ((Get-Item $f).Length / 1MB)) -ForegroundColor Green
    }
}
