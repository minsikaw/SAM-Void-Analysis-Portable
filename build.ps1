$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Building RF GaN SAM Batch Quantifier v1 (.NET 8, Windows)...'
dotnet restore .\RFGAN_BatchQuantifier_V1.sln
dotnet build .\RFGAN_BatchQuantifier_V1.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'Running headless smoke tests...'
dotnet run --project .\RFGAN.SmokeTests\RFGAN.SmokeTests.csproj --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed' }
Write-Host 'Start UI with: dotnet run --project .\RFGAN.Desktop\RFGAN.Desktop.csproj'
