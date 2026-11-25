# Installs the current build of Glassify to %USERPROFILE%\Glassify and creates Start menu + Desktop shortcuts.
# Safe to re-run: closes Glassify first (cleanly, so your login is saved), copies the new files, verifies, starts it.
# One-time migration: if an old MiniSpotify install exists, its login and settings are copied over.
$root = Split-Path $PSScriptRoot
$dest = Join-Path $env:USERPROFILE 'Glassify'
$old  = Join-Path $env:USERPROFILE 'MiniSpotify'
New-Item -ItemType Directory -Force $dest | Out-Null

foreach ($name in 'Glassify', 'MiniSpotify') {
  Get-Process $name -ErrorAction SilentlyContinue | % { $_.CloseMainWindow() | Out-Null; $_.WaitForExit(15000) | Out-Null; if (-not $_.HasExited) { Stop-Process $_ -Force; $_.WaitForExit(5000) | Out-Null } }
}

if (-not (Test-Path "$dest\data") -and (Test-Path "$old\data")) {
  Copy-Item "$old\data" "$dest\data" -Recurse -Force
  Remove-Item "$dest\data\main.hwnd", "$dest\data\debug" -ErrorAction SilentlyContinue
  "moved your login and settings from MiniSpotify"
}

$ok = $false
for ($i = 0; $i -lt 30 -and -not $ok; $i++) { try { Copy-Item "$root\build\*" $dest -Recurse -Force -ErrorAction Stop; $ok = $true } catch { Start-Sleep 0.5 } }
Copy-Item "$root\theme.css" $dest -Force
"installed: " + ((Get-FileHash "$root\build\Glassify.exe").Hash -eq (Get-FileHash "$dest\Glassify.exe").Hash)

# shortcuts (and remove the old MiniSpotify ones)
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
  Remove-Item (Join-Path $dir 'MiniSpotify.lnk') -ErrorAction SilentlyContinue
  $sc = $shell.CreateShortcut((Join-Path $dir 'Glassify.lnk'))
  $sc.TargetPath = "$dest\Glassify.exe"; $sc.WorkingDirectory = $dest; $sc.IconLocation = "$dest\Glassify.exe,0"; $sc.Description = 'Glassify - a light glass player for Spotify'
  $sc.Save()
}
# an old startup entry, if any, now points at Glassify
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty $run -Name MiniSpotify -ErrorAction SilentlyContinue) { Remove-ItemProperty $run -Name MiniSpotify; Set-ItemProperty $run -Name Glassify -Value "`"$dest\Glassify.exe`" --startup" }

Start-Process "$dest\Glassify.exe"
