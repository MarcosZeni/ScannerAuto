# Perfiles de actuadores

Un perfil describe cómo hablarle a los módulos de un modelo concreto para probar actuadores (electro, bomba, inyectores…) y borrar fallas por módulo. Son archivos JSON, uno por modelo o familia, que se importan desde la pestaña **Pruebas** de la app (quedan en la carpeta de datos de la app, `perfiles/`). No hace falta recompilar.

De dónde salen los datos y cómo se verifican: [../docs/PERFILES - Fuentes y plan.md](../docs/PERFILES%20-%20Fuentes%20y%20plan.md). **No se inventan**: cada perfil se arma con una captura o documentación de ese auto y se prueba estacionado comparando con un escáner de referencia.

## Esquema

`ejemplo-simulado.json` es el perfil del auto simulado exportado tal cual (identificadores inventados; sirve solo como plantilla).

| Campo | Qué es |
|---|---|
| `Id` | Único, en minúsculas (`renault-clio4-k9k`). |
| `Name`, `Source` | Nombre visible y de dónde salieron los datos (fuente, fecha, auto con el que se verificó). |
| `VinPrefixes` | Prefijos de VIN con los que el perfil se aplica solo (`"8A1"` = Renault Argentina…). Sin esto, el perfil solo se puede elegir a mano. |
| `Modules` | `Name`, `RequestId`, `ResponseId`: cabeceras CAN de pedido y respuesta (3 dígitos en 11 bits: `7E0`/`7E8`; 8 en 29 bits: `18DA10F1`/`18DAF110`). |
| `SessionOpen`, `TesterPresent` | Cómo abrir la sesión extendida (`1003`, a veces `10C0`) y mantenerla (`3E80`). |
| `Tests[]` | Una prueba por actuador: `Id`, `Name`, `Description`, `ModuleName`, `Precondition` (`EngineOff` o `Any`), `MaxDuration` (`"00:00:30"`), `FeedbackPid` (PID del modo 01 para mirar mientras corre, decimal), `StatusDid` (DID para leer el estado con `22`), y `Steps[]`. |
| `Steps[]` | `Name` (texto del botón), `Payload` (bytes UDS/KWP completos en hex, sin espacios: `2F40010302`), `IsStop` (`true` en el paso que apaga / devuelve el control). Cada prueba necesita uno con `IsStop`. |
| `ModuleClears[]` | `ModuleName` y `Payload` (`14FFFFFF`) para los módulos que ignoran el borrado genérico. Opcional: el barrido de módulos de la pestaña Fallas ya borra por módulo sin perfil con el `14` estándar; esto sirve si un módulo necesita otro pedido. |

Al cargar, la app rechaza lo que no cumpla: solo servicios `2F`, `31` y `30` en los pasos, `14` en los borrados, `10xx` y `3Exx` para la sesión; sin paso de apagado no se carga. Un perfil no puede ampliar lo que la política de comandos deja salir.
