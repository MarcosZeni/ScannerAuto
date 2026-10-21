# Entorno

Registrado el 20/10/2026 en la PC del desarrollador (Windows 11 Pro 26300).

| Componente | Versión |
|---|---|
| .NET SDK | 10.0.401 (`global.json`, rollForward latestFeature) |
| Workloads | `maui-android` y `maui-windows` 10.0.110 (manifiesto 10.0.100, SDK band 10.0.400) |
| Microsoft.Maui.Controls | 10.0.110 (fijado en el csproj) |
| Android SDK | `D:\Android\sdk`: platform `android-36`, build-tools 36.1.0, imagen `android-35` google_apis x86_64 |
| JDK | `D:\Android\java\jdk-17.0.20.1+1` (`JAVA_HOME`) |
| Emulador Android | AVD `TallerScan` (API 35, x86_64) en `D:\Android\avd`, aceleración WHPX |
| Tests | xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4 |

## Destinos

- `net10.0-android`, mínimo Android 7.0 (API 24). APK sin empaquetar en AAB.
- `net10.0-windows10.0.19041.0`, mínimo Windows 10 1809 (10.0.17763). Sin empaquetar, Windows App SDK incluido.
- `Core`, `Infrastructure`, emulador y tests: `net10.0`.
- iOS / Mac Catalyst: no configurados (hace falta una Mac).

## Qué se ejecutó (20/10/2026)

| Prueba | Debug | Release |
|---|---|---|
| `dotnet test` (103 tests) | OK | — |
| App Windows: barrido de módulos con `todos-los-modulos` (503 direcciones, 5 módulos, 6 fallas), "Borrar las fallas de todos los módulos" (4 de 5 aceptan; el airbag no), relectura | OK (UI Automation) | — |
| App Windows: perfil importado elegido a mano en una sesión Wi-Fi contra el emulador OBD, prueba de actuador con ese perfil | OK (UI Automation) | — |
| App Windows: borrado de motor (rechazado con motor en marcha, aceptado apagado), borrado de caja por módulo, pruebas de actuadores (electro, inyector, rutina, detener todo, salir), vuelta a sensores | OK (UI Automation) | — |
| APK en emulador Android 15: pruebas de actuadores, borrado de motor, compartir informe | — | OK |
| Build Windows | OK, 0 advertencias | — |
| Build Android | OK, 0 advertencias | OK (`dotnet publish`) |
| App Windows: simulador, fallas, sensores, historial, traza | OK (manejada por UI Automation) | — |
| App Windows: Wi-Fi contra el emulador OBD | OK | — |
| APK en emulador Android 15: Wi-Fi contra el emulador OBD, fallas, sensores, guardar, informe, compartir, permiso Bluetooth | — | OK |
| Teléfono físico | No probado | No probado |
| Adaptador y auto reales | No probado | No probado |

Nota: en esta PC las capturas de pantalla de la app de Windows salen en blanco (también con la plantilla vacía de MAUI); se verificó leyendo la interfaz con UI Automation. Las capturas del emulador de Android salen bien.
