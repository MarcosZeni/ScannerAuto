@echo off
title TallerScan - App Windows
setlocal
rem Compila y abre la app en Windows. Esta ventana queda abierta mientras la app corre.
cd /d "%~dp0" || goto :error

echo == Compilando y abriendo la app...
dotnet run --project src\TallerScan.App -f net10.0-windows10.0.19041.0 || goto :error
pause
exit /b 0

:error
echo.
echo == FALLO: revisar arriba.
pause
exit /b 1
