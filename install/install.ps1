#Requires -RunAsAdministrator
<#
  install.ps1 - Builds (publishes) GameLock and installs the protection service.
  Run from an elevated PowerShell prompt, from the repo root (where GameLock.sln lives).
#>

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot | Split-Path -Parent   # repo root (install\ is one level below root)
$serviceProj = Join-Path $root "src\GameLock.Service\GameLock.Service.csproj"
$guiProj     = Join-Path $root "src\GameLock.Gui\GameLock.Gui.csproj"

$installRoot = Join-Path ${env:ProgramFiles} "GameLock"
$serviceOut  = Join-Path $installRoot "Service"
$guiOut      = Join-Path $installRoot "Gui"

Write-Host "==> Publishing GameLock.Service..."
dotnet publish $serviceProj -c Release -r win-x64 --self-contained false -o $serviceOut

Write-Host "==> Publishing GameLock.Gui..."
dotnet publish $guiProj -c Release -r win-x64 --self-contained false -o $guiOut

Write-Host "==> Registering Windows Service (GameLockService)..."
$svcExe = Join-Path $serviceOut "GameLockService.exe"

$existing = Get-Service -Name GameLockService -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service already exists; stopping and deleting old registration first."
    Stop-Service GameLockService -ErrorAction SilentlyContinue
    sc.exe delete GameLockService | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create GameLockService binPath= "`"$svcExe`"" start= auto obj= LocalSystem DisplayName= "GameLock Protection Service"
sc.exe description GameLockService "Enforces parental game locks; runs at boot as SYSTEM."
sc.exe failure GameLockService reset= 86400 actions= restart/5000/restart/5000/restart/5000
Start-Service GameLockService

Write-Host "==> Creating Start Menu shortcut for the management GUI..."
$wshell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "GameLock.lnk"
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $guiOut "GameLockGui.exe"
$shortcut.WorkingDirectory = $guiOut
$shortcut.Description = "GameLock parental game control"
$shortcut.Save()

Write-Host ""
Write-Host "GameLock installed."
Write-Host "  Service:   GameLockService (running, auto-start)"
Write-Host "  GUI:       $guiOut\GameLockGui.exe"
Write-Host "  Shortcut:  $shortcutPath"
Write-Host ""
Write-Host "Launch the GUI (it will prompt UAC) to create the parent password and add games."
