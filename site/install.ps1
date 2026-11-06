# Installs Job Orchestrator on Windows from the latest GitHub release, after checking the file's SHA-256 against the
# SHA256SUMS.txt published with it. Then it runs the same Setup.exe the downloads use.
#
#   irm https://gabrielandre-math.github.io/job-orchestrator/install.ps1 | iex
#
# Read it before running it: https://github.com/gabrielandre-math/job-orchestrator/blob/main/site/install.ps1
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$release = 'https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download'
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$file = "job-orchestrator-windows-$arch-setup.exe"
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ('job-orchestrator-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = Join-Path $dir $file
$sums = Join-Path $dir 'SHA256SUMS.txt'

Write-Host "Downloading $file"
Invoke-WebRequest -Uri "$release/$file" -OutFile $exe -UseBasicParsing
Invoke-WebRequest -Uri "$release/SHA256SUMS.txt" -OutFile $sums -UseBasicParsing

$expected = Get-Content $sums | ForEach-Object { $parts = $_ -split '\s+'; if ($parts.Count -ge 2 -and $parts[1] -eq $file) { $parts[0].ToLowerInvariant() } } | Select-Object -First 1
$actual = (Get-FileHash -Algorithm SHA256 -Path $exe).Hash.ToLowerInvariant()
if (-not $expected -or $expected -ne $actual) {
    Remove-Item -Recurse -Force $dir
    throw "The SHA-256 of $file does not match SHA256SUMS.txt. Nothing was installed."
}
Write-Host 'SHA-256 checked'

if ($env:JO_INSTALL_DRY_RUN -eq '1') { Write-Host 'Dry run: stopping before the installer'; Remove-Item -Recurse -Force $dir; return }

Start-Process -FilePath $exe -Wait
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
