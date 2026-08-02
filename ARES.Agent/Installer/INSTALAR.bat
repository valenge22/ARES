@echo off
chcp 65001 >nul
title Instalador de ARES Agent
set "ARES_LOG=%TEMP%\ARES-Agent-Install.log"
if exist "%ARES_LOG%" del /q "%ARES_LOG%"
echo ========================================
echo        INSTALADOR DE ARES AGENT
echo ========================================
echo.

if not exist "%~dp0app\ARES.Agent.exe" (
    echo ERROR: Falta app\ARES.Agent.exe.
    echo.
    echo No ejecutes este archivo desde el codigo fuente de GitHub.
    echo Descarga y descomprime el ZIP completo desde GitHub Releases.
    echo.
    pause
    exit /b 1
)

set /p ARES_KEY="Clave compartida de ARES: "
if "%ARES_KEY%"=="" (
    echo ERROR: La clave no puede estar vacia.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar-ARES-Agent.ps1" -ServerUrl "https://ares-3bic.onrender.com" -ApiKey "%ARES_KEY%" -LogPath "%ARES_LOG%"
if errorlevel 1 (
    echo.
    echo La instalacion no pudo completarse.
    echo.
    if exist "%ARES_LOG%" (
        echo DETALLE DEL ERROR:
        echo ----------------------------------------
        type "%ARES_LOG%"
        echo ----------------------------------------
    ) else (
        echo No se genero el registro. Es posible que se haya cancelado el permiso de administrador.
    )
    echo.
    echo Registro: %ARES_LOG%
    pause
    exit /b 1
)

echo.
echo Instalacion completada correctamente.
pause
