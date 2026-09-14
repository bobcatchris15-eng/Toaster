$ErrorActionPreference='SilentlyContinue'
Stop-Service Toaster -Force
sc.exe delete Toaster | Out-Null
