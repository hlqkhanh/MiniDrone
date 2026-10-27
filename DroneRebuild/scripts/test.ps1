$ErrorActionPreference = 'Stop'
$rebuild = Split-Path -Parent $PSScriptRoot
$output = Join-Path $rebuild 'build'
New-Item -ItemType Directory -Force $output | Out-Null
& g++ -std=c++17 -Wall -Wextra -Werror (Join-Path $rebuild 'tests/core_tests.cpp') -o (Join-Path $output 'core_tests.exe')
if ($LASTEXITCODE) { throw 'Core compile failed' }
& (Join-Path $output 'core_tests.exe')
if ($LASTEXITCODE) { throw 'Core tests failed' }
& g++ -std=c++17 -Wall -Wextra -Werror "-I$(Join-Path $rebuild 'tests/imu-stubs')" (Join-Path $rebuild 'tests/attitude_tests.cpp') -o (Join-Path $output 'attitude_tests.exe')
if ($LASTEXITCODE) { throw 'Estimator compile failed' }
& (Join-Path $output 'attitude_tests.exe')
if ($LASTEXITCODE) { throw 'Estimator tests failed' }
& g++ -std=c++17 -Wall -Wextra -Werror "-I$(Join-Path $rebuild 'tests/sfun-stubs')" (Join-Path $rebuild 'tests/sfun_tests.cpp') -o (Join-Path $output 'sfun_tests.exe')
if ($LASTEXITCODE) { throw 'S-function wrapper compile failed' }
& (Join-Path $output 'sfun_tests.exe')
if ($LASTEXITCODE) { throw 'S-function wrapper tests failed' }
& g++ -std=c++17 -Wall -Wextra -Werror (Join-Path $rebuild 'simulation/native_sim.cpp') -o (Join-Path $output 'native_sim.exe')
if ($LASTEXITCODE) { throw 'Simulation compile failed' }
& (Join-Path $output 'native_sim.exe') $output
if ($LASTEXITCODE) { throw 'Synthetic simulations failed' }
Copy-Item -LiteralPath (Join-Path $output 'controller_vectors.csv') -Destination (Join-Path $rebuild 'simulation/controller_vectors.csv')
Write-Host 'Native tests passed. MATLAB/Simulink execution remains a separate check.'
