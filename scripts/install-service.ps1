param([string]$InstallDir)
$ErrorActionPreference='Stop'
$exe=Join-Path $InstallDir 'Service\Toaster.Service.exe'
if(Get-Service Toaster -ErrorAction SilentlyContinue){ Stop-Service Toaster -Force -ErrorAction SilentlyContinue; sc.exe delete Toaster | Out-Null; Start-Sleep -Milliseconds 500 }
sc.exe create Toaster binPath= ('"'+$exe+'"') start= auto DisplayName= 'Toaster Local Knowledge Service' | Out-Null
sc.exe description Toaster 'Local expertise and source-memory service for AI tools.' | Out-Null
sc.exe start Toaster | Out-Null
