#Requires -RunAsAdministrator
<#
  uninstall.ps1 - Removes the GameLock service, program files, shortcut.
  Configuration (config.json with games + password hash) is left in place by default
  under C:\ProgramData\GameLock unless -RemoveConfig is passed.
#>
param(
    [switch]$RemoveConfig
)

$ErrorActionPreference = "Stop"

Write-Host "==> Stopping and removing GameLockService..."
$existing = Get-Service -Name GameLockService -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service GameLockService -ErrorAction SilentlyContinue
    sc.exe delete GameLockService | Out-Null
}

$installRoot = Join-Path ${env:ProgramFiles} "GameLock"
if (Test-Path $installRoot) {
    Write-Host "==> Removing program files at $installRoot..."
    Remove-Item -Recurse -Force $installRoot
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "GameLock.lnk"
if (Test-Path $shortcutPath) {
    Write-Host "==> Removing shortcut..."
    Remove-Item -Force $shortcutPath
}

if ($RemoveConfig) {
    $configRoot = Join-Path ${env:ProgramData} "GameLock"
    if (Test-Path $configRoot) {
        Write-Host "==> Removing configuration (games list + password hash) at $configRoot..."
        Remove-Item -Recurse -Force $configRoot
    }
}
else {
    Write-Host "Configuration left in place under `"$env:ProgramData\GameLock`". Re-run with -RemoveConfig to delete it too."
}

Write-Host "GameLock uninstalled. Games are no longer being blocked."
