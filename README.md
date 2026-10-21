# TallerScan Scanner

App de diagnóstico OBD2 para el taller: se conecta directo a un adaptador ELM327 desde el celular Android o desde la PC con Windows, lee códigos de falla, luz de falla, monitores, freeze frame, VIN y sensores en vivo, borra fallas después de la reparación, y guarda cada diagnóstico en el historial del equipo con un informe para el cliente.

Al adaptador solo salen consultas de lectura, salvo en los flujos que el mecánico confirma: **borrar fallas** (de motor, o de cualquier módulo encontrado en el barrido) y **pruebas de actuadores** (electro, bomba, inyectores…). Cada uno abre un alcance con la lista exacta de comandos que necesita y lo cierra al terminar (`CommandPolicy` + `CommandScope`); no hay consola libre.

## Estado (versión 0.2, 20/10/2026)

| Qué | Estado |
|---|---|
| Núcleo, protocolo ELM327, CAN 11/29 bits, ISO 9141/KWP | Hecho, 103 tests |
| Simulador (10 autos de prueba, con motor apagable, actuadores, caja que ignora el 04, ABS/airbag/carrocería) | Hecho, probado en Windows y en el emulador de Android |
| Borrar fallas de motor (modo 04): evidencia → confirmar → borrar → releer | Hecho, probado con el simulador |
| **Barrido de todos los módulos** por CAN (ABS, airbag, caja, carrocería…) sin perfil: lectura de fallas por módulo (UDS `19 02` / KWP `18`) y borrado por módulo (`14`, abriendo sesión si hace falta). Lo que hace un escáner "todos los sistemas" para fallas. Sin nombres de módulo ni descripciones de códigos propios de la marca | Hecho, probado con el simulador (Windows, UI Automation). **Sin validar en un auto real** |
| Pruebas de actuadores (UDS `2F`/`31`) | Infraestructura hecha y probada con el simulador. **Para un auto real hace falta un perfil por modelo** (direcciones y códigos de esa marca); hoy solo existe el del auto simulado. Los perfiles se importan como JSON desde la pestaña Pruebas ([perfiles/README.md](perfiles/README.md)). De dónde salen los datos: [docs/PERFILES - Fuentes y plan.md](docs/PERFILES%20-%20Fuentes%20y%20plan.md) |
| Wi-Fi (TCP) | Probado contra el emulador OBD, en Windows y en el emulador de Android |
| Bluetooth clásico (SPP), Android y Windows | Escrito, permiso probado en Android. **Sin probar con un adaptador real** |
| BLE, USB/COM, iPhone | No implementado |
| Auto real | **No validado** (ver [docs/COMPATIBILIDAD.md](docs/COMPATIBILIDAD.md)) |

## Probar sin auto

1. **Simulador dentro de la app:** Conexión → Tipo de conexión → *Simulador (sin auto)* → elegir el auto → Conectar. En Conexión y en Pruebas hay botones para apagar o arrancar el motor simulado: el borrado y las pruebas de actuadores piden motor apagado, como en un auto real.
2. **Emulador OBD por Wi-Fi** (prueba el camino real de red, como un adaptador Wi-Fi): `emulador-obd.bat [escenario]` y en la app Conexión → Wi-Fi → `127.0.0.1` (misma PC), `10.0.2.2` (emulador de Android) o la IP de la PC (celular en la misma red), puerto `35000`. En la consola del emulador se pueden agregar fallas en caliente (`falla P0300`, `pendiente P0171`, `limpiar`), acelerar (`andar`) o cambiar de auto (`escenario kwp-viejo`). Escribir `ayuda`.

Escenarios: `sano`, `falla-encendido`, `mezcla-pobre`, `multi-ecu` (motor + caja), `todos-los-modulos` (motor, caja, ABS que pide sesión, airbag con datos de choque, carrocería por KWP: para el barrido de módulos), `can29`, `kwp-viejo` (ISO 9141-2), `recalienta`, `contacto-apagado`, `lento`.

## Comandos (verificados el 20/10/2026)

| Para | Comando |
|---|---|
| Tests | `dotnet test tests/TallerScan.Tests` |
| Abrir en Windows | `abrir-windows.bat` (o `dotnet run --project src/TallerScan.App -f net10.0-windows10.0.19041.0`) |
| APK para el celular | `generar-apk.bat` → `artifacts/android/ar.com.tallerscan.scanner-Signed.apk` |
| Emulador OBD | `emulador-obd.bat falla-encendido` (acepta `--motor apagado` para probar borrado y actuadores) |
| Plantilla de perfil | `dotnet run --project tools/TallerScan.Emulator -- --exportar-perfil perfiles/ejemplo-simulado.json` |
| Compilar Android (Debug) | `dotnet build src/TallerScan.App -f net10.0-android` |

El APK de **Debug** no sirve para instalar a mano (Fast Deployment deja las DLL afuera); para el celular, siempre `generar-apk.bat` (Release).

## Estructura

```
src/TallerScan.Core            dominio, catálogo de PIDs y DTC, decodificación, política de comandos y alcances, direcciones del barrido, perfiles de actuadores (net10.0, sin MAUI)
src/TallerScan.Infrastructure  ELM327 (enlace, parser, armado ISO-TP), UDS (sesión, 2F/31/14/22), barrido de módulos (19/18/14), pruebas de actuadores, transportes TCP, simulador, sesión, historial JSON, informes
src/TallerScan.App             MAUI Android + Windows: pantallas, ViewModels, Bluetooth por plataforma (Platforms/*/Bluetooth)
tools/TallerScan.Emulator      emulador ELM327 por TCP para probar sin auto
tests/TallerScan.Tests         xunit: vectores de decodificación, tramas, política, punta a punta contra el emulador
docs/                          decisiones, compatibilidad, entorno
```

Plan completo y razones de cada límite: [../Docs/TallerScan_Plan_Desarrollo_Multiplataforma.md](../Docs/TallerScan_Plan_Desarrollo_Multiplataforma.md).
