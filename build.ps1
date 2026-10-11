$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$toolDirectory = Join-Path $root 'tools'
$nvflash = Join-Path $toolDirectory 'nvflashb.exe'
$nvflashHash = 'b0ddd652a3e8abcd6385898d5fb3db7c765118e5794822c9b1d944f0c6f5e4f1'
if (!(Test-Path -LiteralPath $nvflash)) {
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    Invoke-WebRequest 'https://github.com/WillyBilly06/NVFLASH/releases/download/Release/nvflashb.exe' -OutFile $nvflash
}
if ((Get-FileHash -LiteralPath $nvflash -Algorithm SHA256).Hash.ToLowerInvariant() -ne $nvflashHash) { throw 'NVFlashB SHA256 verification failed.' }
Copy-Item -LiteralPath (Join-Path $root 'docs/NVFLASH-PROVENANCE.txt') -Destination $toolDirectory
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
Copy-Item -LiteralPath (Join-Path $root 'tools') -Destination $output -Recurse
$zip = Join-Path (Split-Path $output) 'Prophecy-Power-Unlocker-1.2.2-win-x64.zip'
Compress-Archive -LiteralPath $output -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -LiteralPath (Join-Path (Split-Path $zip) 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Tests passed. Release package: $zip"
