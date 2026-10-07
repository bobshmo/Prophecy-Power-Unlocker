$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/ProphecyPowerUnlocker.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$output = Join-Path $root "artifacts/$stamp/Prophecy-Power-Unlocker"
& dotnet build $project -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$testDll = Join-Path $root 'src/bin/Release/net8.0-windows10.0.19041.0/win-x64/Prophecy Power Unlocker.dll'
& dotnet $testDll --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
& dotnet publish $project -c Release --no-restore -o $output -p:PublishSingleFile=false -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('README.md', 'THIRD-PARTY-NOTICES.md', 'setup-mvolt.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $output -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $output -Recurse
$zip = Join-Path (Split-Path $output) 'Prophecy-Power-Unlocker-1.0.0-win-x64.zip'
Compress-Archive -LiteralPath $output -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -LiteralPath (Join-Path (Split-Path $zip) 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Tests passed. Release package: $zip"
