@echo off
title TallerScan - Generar APK
setlocal
rem Compila la app de Android en Release y deja el APK para pasar al celular
rem (WhatsApp, Drive o cable) e instalarlo a mano. Firmado con la clave de depuracion:
rem sirve para uso interno, no para Play Store.
cd /d "%~dp0" || goto :error

echo == Compilando APK de Android (Release)...
dotnet publish src\TallerScan.App -f net10.0-android -c Release -o artifacts\android || goto :error

echo.
echo == Listo: %~dp0artifacts\android\ar.com.tallerscan.scanner-Signed.apk
echo    En el celular: permitir "instalar apps desconocidas" para la app con la que se abra.
pause
exit /b 0

:error
echo.
echo == FALLO: revisar arriba.
pause
exit /b 1
