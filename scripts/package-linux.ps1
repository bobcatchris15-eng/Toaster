param([string]$Configuration='Release',[string]$Runtime='linux-x64')
$ErrorActionPreference='Stop'

$root=Split-Path $PSScriptRoot -Parent

[xml]$buildProps=Get-Content (Join-Path $root 'Directory.Build.props')
$version=$buildProps.Project.PropertyGroup.Version
if(-not $version){ throw 'No <Version> element in Directory.Build.props' }

$name="toaster-$version-$Runtime"
$staging=Join-Path $root "artifacts\linux\$name"
$outDir=Join-Path $root 'artifacts\linux'
Write-Host "Packaging Toaster $version for $Runtime" -ForegroundColor Green

Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item $staging -ItemType Directory -Force | Out-Null

# InvariantGlobalization keeps the tarball dependency-free. Without it .NET
# FailFasts on any host that has no ICU — which includes minimal containers and
# stock WSL images, and the failure is a bare stack trace with no useful message.
$publishArgs=@(
    '-c', $Configuration
    '-r', $Runtime
    '--self-contained', 'true'
    '-p:PublishSingleFile=false'
    '-p:InvariantGlobalization=true'
)

dotnet publish (Join-Path $root 'src\Toaster.Service\Toaster.Service.csproj') @publishArgs -o (Join-Path $staging 'service')
if($LASTEXITCODE -ne 0){ throw 'service publish failed' }

dotnet publish (Join-Path $root 'src\Toaster.Cli\Toaster.Cli.csproj') @publishArgs -o (Join-Path $staging 'cli')
if($LASTEXITCODE -ne 0){ throw 'cli publish failed' }

# The Tesseract package carries Windows-only natives, so OCR cannot run on Linux.
# Shipping the language data would only imply otherwise.
Remove-Item (Join-Path $staging 'service\x86') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $staging 'service\x64') -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $root 'installer\linux\install.sh') $staging
Copy-Item (Join-Path $root 'installer\linux\uninstall.sh') $staging
Copy-Item (Join-Path $root 'installer\linux\toaster.service') $staging

$tarball=Join-Path $outDir "$name.tar.gz"
Remove-Item $tarball -Force -ErrorAction SilentlyContinue

# bsdtar ships with Windows 10+. Execute bits do not survive a Windows file
# system, so install.sh chmods what it installs; the tarball itself is only a
# transport. Users still need `sh install.sh` rather than `./install.sh`.
tar -czf $tarball -C $outDir $name
if($LASTEXITCODE -ne 0){ throw 'tar failed' }

$size=[math]::Round((Get-Item $tarball).Length/1MB,1)
Write-Host ""
Write-Host "Wrote $tarball ($size MB)" -ForegroundColor Green
Write-Host "Install with:  tar -xzf $name.tar.gz && cd $name && sh install.sh"
