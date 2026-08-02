@echo off
title Instalador de ARES Agent
set /p ARES_SERVER="Direccion HTTPS del servidor ARES: "
set /p ARES_KEY="Clave compartida de ARES: "
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar-ARES-Agent.ps1" -ServerUrl "%ARES_SERVER%" -ApiKey "%ARES_KEY%"
