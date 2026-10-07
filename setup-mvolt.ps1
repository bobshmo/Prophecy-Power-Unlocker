param([string]$Destination = (Join-Path $PSScriptRoot 'mvolt'))
$ErrorActionPreference = 'Stop'
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/b00nz/mVolt/releases/latest' -Headers @{ 'User-Agent' = 'Prophecy-Power-Unlocker' }
$asset = @($release.assets | Where-Object name -eq 'mVolt+.exe')
if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:([a-fA-F0-9]{64})$') {
    throw 'The official release did not provide one executable with a SHA-256 digest. Select the official executable manually.'
}
$expectedHash = $Matches[1]
$downloadUrl = $asset[0].browser_download_url
if ($downloadUrl -notlike 'https://github.com/b00nz/mVolt/releases/download/*') { throw 'Unexpected download location.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$temp = Join-Path $Destination ('download-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $temp
    if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Download checksum mismatch.' }
    Move-Item -LiteralPath $temp -Destination (Join-Path $Destination 'mVolt+.exe') -Force
    Write-Host "Installed official mVolt $($release.tag_name). No tuning was applied."
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}
