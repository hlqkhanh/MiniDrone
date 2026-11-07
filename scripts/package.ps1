$ErrorActionPreference='Stop'
$rebuild=Split-Path -Parent $PSScriptRoot
& python (Join-Path $PSScriptRoot 'package.py')
if ($LASTEXITCODE) { throw 'Packaging failed' }
