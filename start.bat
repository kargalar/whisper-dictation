@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo [HATA] Baslatma basarisiz oldu. Detaylar yukaridadir.
    pause
)

