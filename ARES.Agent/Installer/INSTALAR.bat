@echo off
title Instalador de ARES Agent
set /p ARES_KEY="Clave compartida de ARES: "
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar-ARES-Agent.ps1" -ServerUrl "https://ares-3bic.onrender.com" -ApiKey "%ARES_KEY%"
