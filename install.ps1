# Kobra Max Steuerung - Installation ohne Admin-Rechte.
# Aufruf aus CMD:
#   powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/QMAXC/kopra-max/main/install.ps1 | iex"
#
# Laedt die neueste Version, entpackt sie nach H:\3d druk\KobraMax (ohne H: nach %LOCALAPPDATA%\KobraMax),
# legt eine Desktop-Verknuepfung an und startet die App. Erneut ausfuehren = aktualisieren.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # macht den Download in Windows PowerShell 5.1 viel schneller
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$url = 'https://github.com/QMAXC/kopra-max/releases/latest/download/KobraMax-win-x64.zip'
$ziel = if (Test-Path 'H:\') { 'H:\3d druk\KobraMax' } else { Join-Path $env:LOCALAPPDATA 'KobraMax' }
$temp = Join-Path $env:TEMP ("KobraMax-" + [Guid]::NewGuid().ToString('N'))
$zip = "$temp.zip"

try {
    Write-Host ''
    Write-Host '  Kobra Max Steuerung wird installiert ...' -ForegroundColor Cyan
    Write-Host "  Ziel: $ziel"
    Write-Host ''

    # Ein offenes Programm blockiert die Dateien (und den USB-Port).
    $laufend = Get-Process -Name KobraMax -ErrorAction SilentlyContinue
    if ($laufend) {
        Write-Host '  Schliesse laufende Kobra Max Steuerung ...'
        $laufend | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 3
        Get-Process -Name KobraMax -ErrorAction SilentlyContinue | Stop-Process -Force
    }

    Write-Host '  [1/4] Herunterladen (ca. 90 MB) ...'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Write-Host '  [2/4] Entpacken ...'
    Expand-Archive -Path $zip -DestinationPath $temp -Force
    $quelle = Join-Path $temp 'KobraMax'
    if (-not (Test-Path (Join-Path $quelle 'KobraMax.exe'))) { throw 'KobraMax.exe nicht im Download gefunden.' }

    Write-Host '  [3/4] Kopieren ...'
    New-Item -ItemType Directory -Force -Path $ziel | Out-Null
    Copy-Item -Path (Join-Path $quelle '*') -Destination $ziel -Recurse -Force
    Get-ChildItem -Path $ziel -Recurse -File | Unblock-File   # "Aus dem Internet"-Markierung entfernen

    Write-Host '  [4/4] Desktop-Verknuepfung ...'
    $exe = Join-Path $ziel 'KobraMax.exe'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $desktop 'Kobra Max Steuerung.lnk'))
    $link.TargetPath = $exe
    $link.WorkingDirectory = $ziel
    $link.IconLocation = "$exe,0"
    $link.Description = 'Anycubic Kobra Max ueber USB steuern'
    $link.Save()

    Write-Host ''
    Write-Host '  Fertig! "Kobra Max Steuerung" liegt jetzt auf dem Desktop.' -ForegroundColor Green
    Write-Host ''
    Start-Process -FilePath $exe -WorkingDirectory $ziel
}
catch {
    Write-Host ''
    Write-Host "  Fehler: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '  Alternativ: ZIP von https://github.com/QMAXC/kopra-max/releases/latest herunterladen und von Hand entpacken.'
    Write-Host ''
}
finally {
    Remove-Item -Path $zip, $temp -Recurse -Force -ErrorAction SilentlyContinue
}
