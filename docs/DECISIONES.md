# Decisiones (versión 0.1)

Cambios respecto del plan (`docs/PLAN.md`) y por qué.

## Backend ELM327 propio, sin Shiny.Obd

El plan pedía evaluar Shiny.Obd. Se escribió un backend propio (`Infrastructure/Elm`) porque el plan exige cosas que una biblioteca tiene que dejar hacer: interceptar **todos** los comandos (incluida la inicialización), conservar el origen por ECU, trazas crudas y descartar respuestas tardías. Con código propio eso está garantizado y no suma dependencias. Si más adelante hace falta BLE con varios perfiles GATT, se puede reevaluar Shiny solo para el transporte.

## El simulador pasa por el mismo protocolo

En vez de un `SimulatedBackend` aparte, el modo simulación usa el **mismo** `Elm327Backend` contra un ELM327 emulado (`Simulation/Elm327Emulator`) que devuelve el texto exacto que mandaría un adaptador, en fragmentos al azar. Así el simulador prueba el parser, el armado ISO-TP y el manejo de errores, no solo la pantalla. El mismo emulador se sirve por TCP (`tools/TallerScan.Emulator`) para probar el camino Wi-Fi.

## Transportes de esta versión

- **Wi-Fi (TCP):** funciona en todas las plataformas sin permisos especiales y permite probar con el emulador. Probado.
- **Bluetooth clásico (SPP):** el de la mayoría de los ELM327 baratos. Android con `BluetoothSocket` (RFCOMM, UUID SPP estándar `00001101-…`, con reintento sin cifrar para clones); Windows con `RfcommDeviceService` + `StreamSocket` (no hace falta el COM virtual). Solo lista equipos **ya vinculados**: no escanea, así que en Android 12+ alcanza con `BLUETOOTH_CONNECT` (sin ubicación ni `BLUETOOTH_SCAN`). Sin probar con hardware.
- **BLE, USB/COM:** no implementados. BLE necesita los UUID GATT del adaptador concreto (el plan prohíbe inventarlos): se hace cuando haya un adaptador BLE elegido.

## Persistencia en JSON, no SQLite (por ahora)

El historial es un archivo JSON por diagnóstico en `FileSystem.AppDataDirectory/historial`. Alcanza para cientos de diagnósticos, no suma paquetes y el mismo archivo sirve para exportar/importar entre celular y PC. Se escribe a un temporal y se renombra (no quedan archivos a medias). Cuando haga falta buscar o comparar sesiones, pasar a `sqlite-net-pcl` como dice el plan (etapa 4); el formato `SessionRecord` tiene número de esquema para migrar.

## Lectura en vivo

Un solo worker por sesión. PIDs rápidos (RPM, carga, mariposa…) en cada vuelta y lentos (temperaturas, tensión) cada 3 s; vuelta mínima de 250 ms. La tasa que se muestra es la real. Si deja de llegar un valor se ve en gris con su antigüedad: nunca se dibuja un cero. En Android, al salir de la app se pausa la lectura (no se promete sondeo en segundo plano) pero no se corta la conexión, para que compartir un informe o el diálogo de permisos no la tiren.

## Catálogo de fallas

Descripciones en español solo de códigos **genéricos** (SAE J2012), unas 250 más la regla P0301–P0312. Un código del fabricante (P1xxx, etc.) se muestra con "código propio del fabricante": no se inventa una descripción.

## Escritura al auto (versión 0.2): borrado y actuadores

El plan arrancaba solo lectura. A pedido del taller se agregaron dos cosas que escriben, con estas reglas:

- **Alcances en vez de "modo libre".** `CommandPolicy` sigue siendo una lista cerrada. Un flujo que escribe abre un `CommandScope` con sus comandos exactos (por ejemplo `04`, o `ATSH7E0`/`ATCRA7E8`/`1003`/`3E80`/`2F40010302`/`2F400100`) y lo cierra en `finally`. Un valor no previsto en el perfil (`2F40010305`) se bloquea aunque el modo esté abierto. Sin hexadecimal arbitrario.
- **Borrar fallas de motor** es el modo 04 estándar (lo mismo que hace cualquier app de ELM327): funcional a todos los módulos. Flujo: leer → confirmar (con el efecto sobre monitores/VTV explicado) → guardar el diagnóstico como evidencia → `04` → esperar → releer → mostrar qué quedó. Las permanentes no se borran. Con el motor en marcha muchas ECU contestan `7F 04 22` y la app lo explica.
- **Borrar por módulo** (`14 FF FF FF` por direccionamiento físico) y **pruebas de actuadores** (`2F` InputOutputControl, `31` RoutineControl, `22` para el estado) van por UDS sobre ISO-TP con el ELM327 (`ATSH`, `ATCRA`, sesión `10 03`, `3E 80` cada 2 s). Eso **no es OBD2 genérico**: las direcciones, DIDs y valores son de cada marca y modelo y viven en un `ActuatorProfile`. Hoy existe solo el perfil del auto simulado, con identificadores inventados. Para un modelo real: documentación o captura de un escáner de marca, cargar el perfil, probar con el auto estacionado. `ProfileCatalog.Find` aplica un perfil por prefijo de VIN; sin perfil, la pestaña Pruebas lo dice y no ofrece nada.
- **Seguridad de la prueba:** antes de forzar algo se lee RPM y se exige 0 si la prueba lo pide; una sola prueba a la vez (cambiar apaga la anterior); tiempo máximo por prueba con apagado automático; "Detener todo"; al salir, desconectar o cortar la conexión se devuelve el control a la ECU (`2F DID 00`) y se vuelve a sesión por defecto (`10 01`) y al pedido funcional. Mientras el modo está abierto no se leen sensores ni fallas: el adaptador está direccionado a un módulo.
- Todo lo hecho queda en `Actions` del registro y en la sección "Acciones realizadas" del informe.
- En el simulador, la caja ignora el modo 04 (`ClearsWithMode04 = false`) y solo se limpia por su módulo, igual que muchas cajas y ABS reales: es justo lo que un escáner barato no hace.
- Acceso de seguridad (`27`) no está implementado: los módulos que lo exigen para actuadores devuelven `33` y la app lo muestra como "pide desbloqueo".

## Barrido de módulos sin perfil (ABS, airbag, caja, carrocería)

Pregunta del taller: "¿un escáner barato borra todas las fallas y nosotros no?". Un escáner "todos los sistemas" no necesita saber qué electro activa: para **leer y borrar fallas** de cualquier módulo alcanzan servicios estándar, y las direcciones se descubren preguntando. Eso es lo que hace `ModuleScanner`:

- **Descubrir:** a cada dirección CAN posible se le manda `19 02 2F` (fallas con estado presente/pendiente/confirmada/histórica). Quien contesta, está. Si contesta "servicio no soportado" se prueba `18 00 FF 00` (KWP2000 sobre CAN, módulos viejos). Orden: 7E0–7E7 (ISO 15765-4, motor y caja), 700–7FF y 600–6FF; en 29 bits, `18DAxxF1` para las 256 direcciones. Unas 500 consultas: uno o dos minutos por Bluetooth, con progreso y cancelación.
- **Filtro de recepción:** el ELM327 solo muestra lo que pasa su filtro. Con `ATCF 600`/`ATCM 600` escucha cualquier cabecera 6xx/7xx, así se encuentran los módulos que no responden en "pedido + 8" (PSA: `752` → `772`, `6A8` → `688`). Si el adaptador no acepta `ATCF`/`ATCM` (clones viejos) o el bus llena el buffer (`BUFFER FULL`), se sigue con `ATCRA` y la convención +8 (Toyota, Ford, Hyundai, Honda, Fiat…): se pierde lo demás y la pantalla lo dice. Lo que no se cubre todavía: GM con pedidos en `2xx` y respuestas en `6xx` (+0x400) y módulos detrás de una puerta de enlace que no pasa diagnóstico por la ficha.
- **Borrar por módulo:** `14 FF FF FF` (UDS) o `14 FF 00` (KWP). Si el módulo contesta "no en esta sesión" (`7F 14 7F/7E`), se abre `10 03`, se repite y se cierra con `10 01`. Nunca seguridad (`27`). Se relee el módulo después. Un airbag con datos de choque responde `22` y queda así, como con cualquier escáner.
- **Honestidad en pantalla:** sin marca no hay nombres de módulos ni descripciones de códigos propios. Se muestra la dirección, el idioma (UDS/KWP), lo que ISO 15765-4 dice de 7E0–7E7, el nombre que informa el propio módulo por `09 0A` si lo hay, y "por sus códigos: chasis/carrocería/red" según la letra. Los genéricos (C0035, U0155…) sí tienen descripción.
- **Alcances:** el barrido abre uno de solo lectura (`ATSH`/`ATCRA`/`ATCF`/`ATCM`/`ATST` + `19 02 2F` + `18 00 FF 00`); el borrado, otro con la dirección de ese módulo y `14`/`10 03`/`10 01`. Al terminar, pase lo que pase, `ATSH 7DF`, `ATAR` y `ATST 32`. El simulador aplica el filtro de recepción igual que el ELM (`Elm327Emulator.Accepts`), así los tests ejercitan los dos caminos.

Lo que sigue dependiendo de perfiles por modelo: **pruebas de actuadores**. El borrado por módulo que ofrecían los perfiles (`ModuleClears`) sigue existiendo para el caso en que un módulo necesite un pedido distinto del estándar.

## Perfiles como archivos JSON

Un perfil real se arma con datos verificados en ese auto (captura o documentación: ver `PERFILES - Fuentes y plan.md`) y se importa desde la pestaña Pruebas; queda en `<datos de la app>/perfiles/<id>.json`. Esquema y reglas en `perfiles/README.md`. Al cargar, `ProfileStore.Validate` rechaza cualquier servicio fuera de `2F`/`31`/`30` (pasos), `14` (borrado), `10`/`3E` (sesión): un archivo no puede ampliar la política de comandos. Un perfil se aplica solo por prefijo de VIN; si no coincide, el mecánico puede elegirlo a mano y la pantalla avisa que lo verifique. En simulación siempre va el perfil simulado.

En Windows sin empaquetar los datos viven en `%LOCALAPPDATA%\TallerScan` (MAUI devolvía una carpeta llamada "Datos de programa"); en Android, en la carpeta privada de la app.

## Windows sin empaquetar

`WindowsPackageType=None` + `WindowsAppSDKSelfContained`: el `.exe` corre directo, sin instalar MSIX ni declarar capacidades de Bluetooth.

## Pendiente de decidir

- Qué adaptador comprar para el piloto (Bluetooth clásico vs BLE vs Wi-Fi). Ver `COMPATIBILIDAD.md`.
- Firma del APK: hoy usa la clave de depuración (alcanza para instalar a mano). Para Play Store hace falta un keystore propio, guardado fuera del repo.
- iPhone: el mecánico usa iPhone. Bluetooth clásico SPP **no** es accesible para apps de iPhone; ahí sirve un adaptador Wi-Fi o BLE. Compilar iOS necesita una Mac.
