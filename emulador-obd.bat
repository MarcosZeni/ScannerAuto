@echo off
title TallerScan - Emulador OBD
setlocal
rem Emulador de adaptador ELM327 Wi-Fi + auto simulado, para probar la app sin auto.
rem En la app: Conexion > Wi-Fi > 127.0.0.1 (misma PC), 10.0.2.2 (emulador de Android)
rem o la IP de esta PC (celular en la misma red). Puerto 35000.
rem Uso: emulador-obd.bat [escenario]   (sin escenario: falla-encendido)
rem Escenarios: sano, falla-encendido, mezcla-pobre, multi-ecu, can29, kwp-viejo,
rem             recalienta, contacto-apagado, lento
cd /d "%~dp0" || goto :error

set ESCENARIO=%1
if "%ESCENARIO%"=="" set ESCENARIO=falla-encendido

echo == Emulador OBD (escenario %ESCENARIO%). Escribir "ayuda" para ver comandos, Ctrl+C para cortar.
dotnet run --project tools\TallerScan.Emulator -- --escenario %ESCENARIO% || goto :error
pause
exit /b 0

:error
echo.
echo == FALLO: revisar arriba.
pause
exit /b 1
