<#
.SYNOPSIS
  Removes a per-user install created by Install.ps1.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File installer\Uninstall.ps1
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\ifredrixDownloadManager')
)

$ErrorActionPreference = 'Continue'
$exe = Join-Path $InstallDir 'ifredrixDownloadManager.exe'

Get-Process -Name 'ifredrixDownloadManager' -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Seconds 1

foreach ($path in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'ifredrix Download Manager.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ifredrix Download Manager.lnk')
)) {
    if (Test-Path $path) {
        Remove-Item -Force $path
        Write-Host "removed shortcut: $path"
    }
}

# Only remove our own association / autostart entries; restore previous mapping.
$assoc = (Get-ItemProperty -Path 'HKCU:\Software\Classes\.torrent' -Name '(default)' -ErrorAction SilentlyContinue).'(default)'
if ($assoc -eq 'ifredrixDownloadManager.torrent') {
    Remove-Item -Recurse -Force 'HKCU:\Software\Classes\ifredrixDownloadManager.torrent' -ErrorAction SilentlyContinue
    $backup = (Get-ItemProperty -Path 'HKCU:\Software\ifredrixDownloadManager\AssocBackup' -Name 'torrent' -ErrorAction SilentlyContinue).'torrent'
    if ($backup) {
        New-Item -Force 'HKCU:\Software\Classes\.torrent' | Out-Null
        Set-ItemProperty -Path 'HKCU:\Software\Classes\.torrent' -Name '(default)' -Value $backup
        Write-Host 'association restored to previous app'
    } else {
        Remove-ItemProperty -Path 'HKCU:\Software\Classes\.torrent' -Name '(default)' -ErrorAction SilentlyContinue
    }
    Remove-Item -Recurse -Force 'HKCU:\Software\ifredrixDownloadManager\AssocBackup' -ErrorAction SilentlyContinue
    Write-Host 'association removed'
}

$run = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ifredrixDownloadManager' -ErrorAction SilentlyContinue).'ifredrixDownloadManager'
if ($run -like "*$InstallDir*") {
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ifredrixDownloadManager' -ErrorAction SilentlyContinue
    Write-Host 'autostart removed'
}

if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "removed dir: $InstallDir"
}

Write-Host '== Uninstalled. (Downloads, settings and tool cache under %LocalAppData%\ifredrixDownloadManager data dirs are kept.) =='
