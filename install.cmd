@echo off
rem Kobra Max Steuerung installieren oder aktualisieren.
rem Ohne PowerShell und ohne Admin-Rechte: nutzt nur curl.exe und tar.exe (in Windows 10/11 eingebaut).
rem Aufruf: install.cmd            -> nach H:\3d druk\KobraMax (ohne H: nach %%LOCALAPPDATA%%\KobraMax)
rem         install.cmd "D:\Ordner" -> nach D:\Ordner\KobraMax
setlocal
set "ZIEL=H:\3d druk"
if not exist H:\ set "ZIEL=%LOCALAPPDATA%"
if not "%~1"=="" set "ZIEL=%~1"
echo.
echo   Kobra Max Steuerung wird nach "%ZIEL%\KobraMax" installiert ...
echo.
tasklist /FI "IMAGENAME eq KobraMax.exe" 2>nul | find /I "KobraMax.exe" >nul && (
  echo   Schliesse laufendes Programm ...
  taskkill /IM KobraMax.exe >nul 2>&1
  ping -n 5 127.0.0.1 >nul
  taskkill /F /IM KobraMax.exe >nul 2>&1
)
if not exist "%ZIEL%" mkdir "%ZIEL%"
curl -fL -o "%TEMP%\KobraMax.zip" https://github.com/QMAXC/kopra-max/releases/latest/download/KobraMax-win-x64.zip || goto fehler
tar -xf "%TEMP%\KobraMax.zip" -C "%ZIEL%" || goto fehler
del "%TEMP%\KobraMax.zip" >nul 2>&1
start "" "%ZIEL%\KobraMax\KobraMax.exe"
echo.
echo   Fertig! Die App startet und legt "Kobra Max Steuerung" auf den Desktop.
echo.
goto :eof
:fehler
echo.
echo   Fehler beim Herunterladen oder Entpacken.
echo   Alternativ: https://github.com/QMAXC/kopra-max/releases/latest - ZIP von Hand entpacken.
pause
