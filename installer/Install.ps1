<#
.SYNOPSIS
  Installs ifredrix Download Manager per-user (no admin needed).
.DESCRIPTION
  Publishes the app to %LocalAppData%\Programs\ifredrixDownloadManager,
  creates Start Menu (and optional Desktop) shortcuts, and optionally
  registers the .torrent association and logon autostart.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File installer\Install.ps1 -Desktop -Autostart
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\ifredrixDownloadManager'),
    [switch]$Desktop,
    [switch]$Autostart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host '== Building (Release) =='
& dotnet publish (Join-Path $root 'ifredrixDownloadManager.csproj') `
    -c Release --nologo -v minimal `
    -o (Join-Path $env:TEMP 'ifredrix-publish')
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Write-Host "== Copying to $InstallDir =="
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item (Join-Path $env:TEMP 'ifredrix-publish\*') $InstallDir -Recurse -Force
Remove-Item -Recurse -Force (Join-Path $env:TEMP 'ifredrix-publish')

$exe = Join-Path $InstallDir 'ifredrixDownloadManager.exe'
if (-not (Test-Path $exe)) { throw "expected exe missing: $exe" }

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $InstallDir
    $link.Description = 'ifredrix Download Manager'
    $link.Save()
    Write-Host "shortcut: $path"
}

$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'ifredrix Download Manager.lnk'
New-Shortcut $startMenu
if ($Desktop) {
    New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ifredrix Download Manager.lnk')
}

# .torrent association (HKCU, no admin). Back up any previous mapping first.
$classes = 'HKCU:\Software\Classes'
$progId = 'ifredrixDownloadManager.torrent'
$prev = (Get-ItemProperty -Path "$classes\.torrent" -Name '(default)' -ErrorAction SilentlyContinue).'(default)'
if ($prev -and ($prev -ne $progId)) {
    New-Item -Force 'HKCU:\Software\ifredrixDownloadManager\AssocBackup' | Out-Null
    Set-ItemProperty 'HKCU:\Software\ifredrixDownloadManager\AssocBackup' -Name 'torrent' -Value $prev
}
New-Item -Force "$classes\.torrent" | Out-Null
Set-ItemProperty "$classes\.torrent" -Name '(default)' -Value $progId
New-Item -Force "$classes\$progId" | Out-Null
Set-ItemProperty "$classes\$progId" -Name '(default)' -Value 'Torrent file (ifredrix Download Manager)'
New-Item -Force "$classes\$progId\shell\open\command" | Out-Null
Set-ItemProperty "$classes\$progId\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
New-Item -Force "$classes\$progId\DefaultIcon" | Out-Null
Set-ItemProperty "$classes\$progId\DefaultIcon" -Name '(default)' -Value "`"$exe`",0"
Write-Host 'associated: .torrent'

if ($Autostart) {
    New-Item -Force 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' | Out-Null
    Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'ifredrixDownloadManager' -Value "`"$exe`""
    Write-Host 'autostart: on'
}

Write-Host '== Installed. Start it from the Start Menu. =='
