# 构建 logifn：只用 Windows 自带的 .NET Framework 4.x 编译器，无需安装 SDK
#
# 用法:
#   .\build.ps1                  构建到 .\dist
#   .\build.ps1 -Clean           先清空输出目录再构建
#   .\build.ps1 -OutputDir bin   指定输出目录
#
# 若提示“禁止运行脚本”，改用:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

[CmdletBinding()]
param(
    [string]$OutputDir,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
Set-Location -LiteralPath $root

if (-not $OutputDir) { $OutputDir = Join-Path $root 'dist' }

$source = Join-Path $root 'src\LogiFn.cs'
if (-not (Test-Path -LiteralPath $source)) {
    throw "找不到源文件: $source"
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw '找不到 csc.exe（需要 .NET Framework 4.x）'
}

if ($Clean -and (Test-Path -LiteralPath $OutputDir)) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path

# 注意：源文件名必须放在最后，/out 与 /target 必须在它之前
# /codepage:65001 确保无 BOM 的 UTF-8 源文件里的中文正常编译
$common = '/nologo', '/optimize+', '/platform:anycpu', '/codepage:65001'

& $csc @common /target:exe "/out:$OutputDir\logifn.exe" $source
if ($LASTEXITCODE -ne 0) { throw "编译 logifn.exe 失败 ($LASTEXITCODE)" }

& $csc @common /target:winexe "/out:$OutputDir\logifnw.exe" $source
if ($LASTEXITCODE -ne 0) { throw "编译 logifnw.exe 失败 ($LASTEXITCODE)" }

Get-ChildItem -LiteralPath $OutputDir -Filter '*.exe' |
    Format-Table Name, Length, LastWriteTime -AutoSize

Write-Host "完成。用法: $OutputDir\logifn.exe help"
