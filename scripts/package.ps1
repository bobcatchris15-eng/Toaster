param([string]$Configuration='Release',[string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $root 'artifacts\publish'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $out -ItemType Directory -Force | Out-Null
$projects=@('Toaster.Service','Toaster.Cli','Toaster.Tray')
foreach($p in $projects){ dotnet publish (Join-Path $root "src\$p\$p.csproj") -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -o (Join-Path $out $p) }
$iscc=(Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if(-not $iscc){ $candidate='C:\Program Files (x86)\Inno Setup 6\ISCC.exe'; if(Test-Path $candidate){$iscc=$candidate} }
if($iscc){ & $iscc (Join-Path $root 'installer\Toaster.iss') } else { Write-Warning 'Inno Setup compiler not found. Published binaries are ready under artifacts\publish.' }
