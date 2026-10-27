param([switch]$FirmwareOnly,[switch]$AppOnly)
$ErrorActionPreference = 'Stop'
$rebuild = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $rebuild
$build = Join-Path $rebuild 'build'
New-Item -ItemType Directory -Force (Join-Path $build 'tmp') | Out-Null
$env:TEMP = Join-Path $build 'tmp'
$env:TMP = $env:TEMP
if (-not $AppOnly) {
    $sketch = Join-Path $rebuild 'firmware/DroneRebuild'
    $files = Get-ChildItem -LiteralPath $sketch -File | Where-Object { $_.Name -ne 'BuildInfo.h' -and $_.Name -ne 'WifiSecrets.h' } | Sort-Object Name
    $hashes = ($files | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $sourceId = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashes)))).Replace('-','').Substring(0,16)
    $stamp = "#pragma once`nconstexpr const char* rebuildSourceId=`"$sourceId`";`n"
    [IO.File]::WriteAllText((Join-Path $sketch 'BuildInfo.h'),$stamp)
    $cli = Join-Path $repo '.tools/arduino-cli/arduino-cli.exe'
    $arguments = @('compile','--warnings','all','--config-file',(Join-Path $repo '.tools/arduino-config.yaml'),'--fqbn','espressif:esp32:esp32','--build-path',(Join-Path $build 'esp32'))
    $bundles=Join-Path $repo '.tools/esp32-bundles'
    $arguments += @('--build-property',"runtime.tools.esptool_py.path=$(Join-Path $bundles 'esptool-windows-amd64')",'--build-property',"runtime.tools.esp32-libs.path=$(Join-Path $bundles 'esp32-libs')",'--build-property',"runtime.tools.esp-x32.path=$(Join-Path $bundles 'xtensa-esp-elf')",$sketch)
    & $cli @arguments
    if ($LASTEXITCODE) { throw 'ESP32 build failed' }
    Write-Host "Source ID: $sourceId (Wi-Fi secrets excluded)"
}
if (-not $FirmwareOnly) {
    $env:DOTNET_CLI_HOME = Join-Path $build 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $repo '.packages'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    & dotnet build (Join-Path $rebuild 'app/DroneRebuild.App.csproj') --disable-build-servers -m:1 -c Release -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -v:minimal
    if ($LASTEXITCODE) { throw 'WPF build failed' }
}
