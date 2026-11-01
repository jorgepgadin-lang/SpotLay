@echo off
rem Doble clic: resumen de SpotLay en GitHub (descargas, visitas, incidencias).
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stats.ps1"
echo.
pause
