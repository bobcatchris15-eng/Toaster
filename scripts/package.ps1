param([string]$Configuration='Release',[string]$Runtime='win-x64')
$ErrorActionPreference='Stop'

$root=Split-Path $PSScriptRoot -Parent

# The version lives in Directory.Build.props; the installer is told, never asked.
[xml]$buildProps=Get-Content (Join-Path $root 'Directory.Build.props')
$version=$buildProps.Project.PropertyGroup.Version
if(-not $version){ throw 'No <Version> element in Directory.Build.props' }
Write-Host "Packaging Toaster $version" -ForegroundColor Green
$out=Join-Path $root 'artifacts\publish'
$redist=Join-Path $root 'artifacts\redist'

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $out -ItemType Directory -Force | Out-Null
New-Item $redist -ItemType Directory -Force | Out-Null

$projects=@('Toaster.Service','Toaster.Cli','Toaster.Tray')
foreach($p in $projects){
    $project=Join-Path $root "src\$p\$p.csproj"
    $destination=Join-Path $out $p
    if($p -eq 'Toaster.Service'){
        # Keep the service multi-file so native OCR DLL loading remains boring and reliable.
        dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=false -o $destination
    }
    else{
        dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -o $destination
    }
}

# Lightweight English OCR model. This is only used on PDF pages where ordinary text extraction is insufficient.
$tessDir=Join-Path $out 'Toaster.Service\tessdata'
New-Item $tessDir -ItemType Directory -Force | Out-Null
$engData=Join-Path $tessDir 'eng.traineddata'
if(-not (Test-Path $engData)){
    Write-Host 'Downloading compact English OCR data...'
    Invoke-WebRequest 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata' -OutFile $engData
}

# Tesseract's Windows native binaries require the Visual C++ 2022 runtime. Bundle the official redistributable so setup works on clean PCs.
$vcRedist=Join-Path $redist 'vc_redist.x64.exe'
if(-not (Test-Path $vcRedist)){
    Write-Host 'Downloading Microsoft Visual C++ runtime...'
    Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $vcRedist
}

$iscc=(Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if(-not $iscc){
    $candidates=@(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $iscc=$candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if($iscc){
    & $iscc "/DMyAppVersion=$version" (Join-Path $root 'installer\Toaster.iss')
}
else{
    Write-Warning 'Inno Setup compiler not found. Published binaries are ready under artifacts\publish.'
}