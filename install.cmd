@echo off
rem Kobra Max Steuerung installieren oder aktualisieren (keine Admin-Rechte noetig)
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/QMAXC/kopra-max/main/install.ps1 | iex"
pause
