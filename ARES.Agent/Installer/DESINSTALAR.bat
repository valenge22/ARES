@echo off
chcp 65001 >nul
title Desinstalador de ARES Agent
set "ARES_LOG=%TEMP%\ARES-Agent-Uninstall.log"
if exist "%ARES_LOG%" del /q "%ARES_LOG%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Desinstalar-ARES-Agent.ps1" -LogPath "%ARES_LOG%"
if errorlevel 1 (
    echo.
    echo No se pudo desinstalar ARES Agent.
    if exist "%ARES_LOG%" type "%ARES_LOG%"
    echo.
    pause
    exit /b 1
)
echo ARES Agent fue desinstalado correctamente.
pause
