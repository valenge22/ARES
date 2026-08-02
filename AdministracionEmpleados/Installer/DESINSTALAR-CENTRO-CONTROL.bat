@echo off
chcp 65001 >nul
title Desinstalar ARES Centro de Control
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Desinstalar-Centro-Control.ps1"
pause
