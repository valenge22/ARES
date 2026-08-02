@echo off
chcp 65001 >nul
title Instalador de ARES Centro de Control
set "ARES_LOG=%TEMP%\ARES-ControlCenter-Install.log"
if exist "%ARES_LOG%" del /q "%ARES_LOG%"

echo ========================================
echo    ARES CENTRO DE CONTROL - INSTALAR
echo ========================================
echo.

if not exist "%~dp0app\ARES.ControlCenter.exe" (
    echo ERROR: Falta app\ARES.ControlCenter.exe.
    echo Descarga y descomprime el ZIP completo desde GitHub Releases.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar-Centro-Control.ps1" -LogPath "%ARES_LOG%"
if errorlevel 1 (
    echo.
    echo La instalación no pudo completarse.
    if exist "%ARES_LOG%" (
        echo ----------------------------------------
        type "%ARES_LOG%"
        echo ----------------------------------------
    )
    pause
    exit /b 1
)

echo.
echo ARES Centro de Control se instaló correctamente.
pause
