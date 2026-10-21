# Compatibilidad

Criterio (del plan): **Verificado** = probado en esa combinación de vehículo, motor, interfaz y versión de la app. **Parcial** = algunas consultas andan. **No validado** = no probado. **Fuera de alcance** = no implementado.

## Vehículos

| Marca / modelo | Año / motor | Protocolo observado | Interfaz | Lecturas comprobadas | Resultado |
|---|---|---|---|---|---|
| (ninguno todavía) | | | | | No validado |

## Protocolos (parser)

| Protocolo | Estado | Cómo se probó |
|---|---|---|
| ISO 15765-4 CAN 11 bits | Implementado | Tests y emulador (varias ECU, mensajes largos ISO-TP) |
| ISO 15765-4 CAN 29 bits | Implementado | Tests y emulador |
| ISO 9141-2 / ISO 14230 (KWP) | Implementado | Tests y emulador (DTC en varias líneas, VIN en 5 mensajes, checksum) |
| SAE J1850 PWM/VPW | Implementado sin verificar checksum (CRC) | Solo lectura de cabecera; sin fixtures propios |
| SAE J1939, OBDonUDS (J1979-2), DoIP | Fuera de alcance | — |

## Transportes

| Transporte | Android | Windows | Resultado |
|---|---|---|---|
| Simulador interno | Probado (emulador Android 15) | Probado | OK |
| Wi-Fi / TCP | Probado contra el emulador OBD (emulador Android 15) | Probado contra el emulador OBD | Sin adaptador real |
| Bluetooth clásico SPP | Implementado; permiso y listado probados en emulador | Implementado | No validado |
| BLE | — | — | Fuera de alcance (falta elegir adaptador) |
| USB / COM | — | — | Fuera de alcance |

## Barrido de módulos (fallas de ABS, airbag, caja, carrocería)

Sin perfil, por CAN. Lo que se encuentra depende de dos cosas: el filtro de recepción que acepte el adaptador y la convención de direcciones de la marca.

| Convención de direcciones | Marcas (según fuentes públicas: opendbc, documentación ISO) | Con filtro amplio (`ATCF`/`ATCM`) | Solo con `ATCRA` (+8) |
|---|---|---|---|
| ISO 15765-4: `7E0–7E7` → `7E8–7EF`, `18DAxxF1` → `18DAF1xx` | Todas (motor, caja) | Sí | Sí |
| "Pedido + 8" en `700–7FF` | Toyota, Ford, Hyundai/Kia, Honda, Mazda, Fiat moderno, Chevrolet (parte) | Sí | Sí |
| `7xx` → `7xx + 0x20` / `6xx` → `6xx − 0x20` | PSA, Renault/Nissan (parte) | Sí | No |
| `7xx` → `7xx + 0x6A` | VW/Audi/Seat UDS (2008+) | Sí | No |
| `2xx` → `6xx` (+0x400) | GM GMLAN (BCM, ABS viejos) | **No** (no se barre `2xx`) | No |

| Vehículo | Adaptador | Módulos encontrados | Borrado | Resultado |
|---|---|---|---|---|
| Simulador `todos-los-modulos` | Emulado | Motor 7E8, caja 7E9, ABS 7B8, airbag 772 (solo con filtro amplio), carrocería 7C8 (KWP) | Todos menos el airbag (datos de choque, `22`) | OK (tests + UI Automation, 20/10/2026) |
| (ningún auto real todavía) | | | | No validado |

## Perfiles de actuadores

Hace falta uno por marca/modelo (o familia de ECU). Sin perfil, el auto real tiene lecturas, borrado genérico de motor y el barrido de módulos, pero no pruebas de actuadores.

| Perfil | Vehículos | Módulos | Pruebas | Origen de los datos | Estado |
|---|---|---|---|---|---|
| `simulado` | Solo el auto simulado | Motor 7E0/7E8, Caja 7E1/7E9 | Electro baja/alta, bomba, A/A, purga, EGR, ralentí, luz MIL, inyectores, bobinas, barrido del tablero; borrado de caja | Inventado para la simulación | Probado |
| (ninguno real todavía) | | | | | |

Para armar un perfil real: capturar con un escáner de marca (o documentación) las direcciones y los pedidos `2F`/`31` de cada prueba, cargarlos en `ProfileCatalog`, y probar con el auto estacionado, motor apagado, comparando contra el escáner. Anotar acá qué se verificó.

## Adaptadores

| Modelo | Firmware | Transporte | Resultado |
|---|---|---|---|
| (ninguno todavía) | | | No validado |

Antes de comprar: confirmar que acepte comandos ELM327 estándar (`ATZ`, `ATH1`, `ATDPN`…), que no sea un clon "v2.1" con firmware recortado, y el transporte. Para el iPhone del mecánico más adelante: Wi-Fi o BLE, no Bluetooth clásico.
