# 自包含发布并编译安装包。在仓库根目录执行：
#   powershell -ExecutionPolicy Bypass -File setup\build-release.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "未找到 Inno Setup 6（ISCC.exe）。请安装：winget install JRSoftware.InnoSetup" }

Write-Host "发布自包含程序到 publish\ ..."
dotnet publish src\IMTReader.App\IMTReader.App.csproj -c Release -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

New-Item -ItemType Directory -Force -Path dist | Out-Null
Write-Host "编译安装包 ..."
& $iscc /Q "$root\setup\IMTReader.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败" }

Get-ChildItem dist\*.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 1)
    Write-Host "完成：$($_.FullName)  ($mb MB)"
}
