$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  dotnet restore .\Toaster.sln
  dotnet build .\Toaster.sln -c Debug
  Write-Host "Starting Toaster service in console mode at http://127.0.0.1:47321" -ForegroundColor Green
  dotnet run --project .\src\Toaster.Service\Toaster.Service.csproj
} finally { Pop-Location }
