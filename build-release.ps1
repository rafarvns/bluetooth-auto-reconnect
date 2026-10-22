<#
.SYNOPSIS
  Builds the production executable and the installer.

.DESCRIPTION
  1. Publishes the Release (production) build as a self-contained single-file exe to
     artifacts\publish.
  2. Compiles installer\BluetoothAutoReconnect.iss with Inno Setup 6 into artifacts\installer.

  The version comes from <Version> in the app's .csproj.
#>
[CmdletBinding()]
param(
    [string]$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\BluetoothAutoReconnect\BluetoothAutoReconnect.csproj'
$publishDir = Join-Path $root 'artifacts\publish'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "No <Version> found in $project" }
Write-Host "Version $version"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path $Iscc)) { throw "Inno Setup not found at $Iscc. Install Inno Setup 6 or pass -Iscc." }
& $Iscc "/DAppVersion=$version" (Join-Path $root 'installer\BluetoothAutoReconnect.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-ChildItem (Join-Path $root 'artifacts\installer') | Format-Table Name, Length
