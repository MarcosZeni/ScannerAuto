# TallerScan — Plan de desarrollo multiplataforma: Android + Windows

**Fecha de revisión:** 14 de octubre de 2026.  
**Destino:** herramienta interna para un taller mecánico familiar en Argentina.  
**Plataformas obligatorias:** Android (celular y tablet) y Windows, con C# + .NET MAUI.  
**Versión del plan:** 2 — reemplaza la propuesta anterior basada en WPF.  
**iPhone/iPad:** arquitectura preparada; entrega posterior, sujeta a validar hardware y entorno de compilación.  
**Estado:** especificación para programar; no es una aplicación terminada ni una validación sobre vehículos reales.

> **Objetivo:** conectar directamente un celular, una tablet o una computadora a un vehículo mediante una interfaz OBD2, leer información de diagnóstico disponible, mostrar sensores y guardar evidencia por vehículo. El celular debe funcionar por sí solo: no es un control remoto de la PC. Primero, solo lectura. No intentar reemplazar de entrada todas las funciones de un escáner profesional multimarca.

---

## 1. Conclusión del análisis y cambio de plataforma

El proyecto debe empezar como una **aplicación nativa multiplataforma**, no como una aplicación WPF a la que después se le agrega una versión móvil. Se propone **.NET MAUI + C# + XAML**, compartiendo núcleo, reglas, ViewModels y la mayor parte de las pantallas. MAUI permite desarrollar para Android, Windows e iOS, pero mantiene puntos de integración específicos de cada plataforma. [^M01]

La primera versión funcional debe ejecutarse en **Android y Windows**. Tener solamente una ventana de Windows o una API HTTP no cumple el requisito móvil. iOS queda preparado en el diseño, pero no se anuncia como compatible hasta compilarlo y probarlo con un iPhone y un adaptador reales. Para desarrollar iOS desde Windows, Microsoft exige un Mac de compilación en red. [^M02]

**Mi recomendación:** mantener el valor propio del proyecto —historial, comparación, informes y flujo del taller— y no intentar reemplazar desde el comienzo un escáner profesional multimarca. La cobertura genérica sigue siendo motor/emisiones, no todas las centralitas. [^S03]

### Decisiones iniciales

| Tema | Decisión propuesta |
|---|---|
| Aplicación | Un proyecto de UI .NET MAUI nativo, Android + Windows |
| Lenguaje | C#; base .NET 10, con SDK y workloads compatibles fijados |
| Diseño | MVVM; VerticalStackLayout y HorizontalStackLayout; CollectionView para listas largas; sin Grid ni DataGrid |
| Primera conexión real propuesta | BLE con un modelo de interfaz y perfil GATT documentados, probado en Android y Windows |
| Alternativa según hardware disponible | Bluetooth clásico/SPP en Android y Windows mediante implementaciones específicas; no confundir con BLE |
| Conexión cableada | USB/COM en Windows, como ampliación o alternativa del piloto |
| Comunicación | Evaluar Shiny.Obd detrás de contratos propios; elegir un solo motor de diagnóstico |
| Persistencia | SQLite local por instalación; sqlite-net-pcl detrás de repositorios |
| Internet | No obligatorio para consultar OBD, registrar ni ver el historial local |
| Sincronización | No incluida en el MVP; exportación/importación explícita primero |
| Seguridad | Solo consultas aprobadas, sin consola libre ni borrado de fallas |
| Primer entregable | Simulación ejecutable en Android y Windows, sin hardware |

**Mismo código no significa mismos permisos ni mismo controlador.** El acceso Bluetooth, el descubrimiento, los archivos, la suspensión y la distribución necesitan tratamiento por plataforma. No prometer un porcentaje exacto de reutilización antes de implementar.

El runtime .NET y los workloads MAUI tienen ciclos de soporte distintos: registrar ambos en `docs/ENTORNO.md` y revisar el soporte vigente al fijar versiones. No deducir que MAUI tiene automáticamente el mismo plazo de soporte que .NET LTS. [^M14]

### Prioridad para evitar trabajo duplicado

No iniciar un nuevo `TallerScan.Desktop` en WPF. Si ya existe código, conservar y extraer modelos, parsers, tests y casos de uso; migrar la UI a MAUI. El XAML de WPF no se considera directamente reutilizable. En MAUI los paneles elegidos serán `VerticalStackLayout` y `HorizontalStackLayout`. [^M01] [^M10]

## 2. Qué incluir y qué dejar afuera

| Función | Primera versión útil | Condición o límite |
|---|---|---|
| Leer códigos almacenados | Sí | Solo el ámbito OBD2 soportado |
| Leer códigos pendientes | Sí | Diferenciar resultado vacío de consulta fallida |
| Leer códigos permanentes | Sí, cuando existan | No todos los vehículos implementan este servicio |
| RPM, temperaturas, velocidad, carga y otros parámetros | Sí | Consultar únicamente lo que el vehículo declara y responde |
| Estado de la luz de avería y monitores | Sí | No presentarlo como aprobación de VTV |
| VIN | Sí, cuando esté disponible | Permitir identificación manual del vehículo |
| Freeze frame: condiciones guardadas al registrarse una falla | Segunda entrega funcional | Separarlo claramente de los datos actuales |
| Historial, notas, CSV y reporte HTML | Sí | Desarrollo propio de la aplicación |
| Borrar fallas | No en el MVP | Diseñar un flujo separado después |
| Diagnóstico completo de ABS, airbag y carrocería | No | Proyecto de cobertura por marca/módulo |
| Codificación, inyectores, llaves, inmovilizador y reprogramación | No | Fuera de este proyecto inicial |
| Actuadores, regeneración de DPF y funciones de servicio | No | No son lecturas inocuas |

Los servicios y parámetros anteriores están documentados en las referencias OBD y en implementaciones abiertas. Su existencia en una biblioteca no garantiza que estén presentes en cada automóvil. [^S01] [^S08]

**Regla funcional:** un código orienta el diagnóstico, pero no justifica automáticamente cambiar una pieza. Como ejemplo, la guía de Innova sobre P0420 indica comprobar otras causas y fugas antes de reemplazar el catalizador. La aplicación debe separar dato observado, interpretación y conclusión del mecánico. [^S20]

## 3. Compatibilidad: cómo comprobarla en Argentina

No usar como regla universal “todos los autos desde 1996”: ese dato corresponde principalmente al mercado estadounidense. La tabla del fabricante OBDLink distingue mercados y lista como referencias para Argentina 2008 para vehículos nacionales y 2009 para importados. Son referencias del fabricante, no una garantía individual ni una certificación normativa de este proyecto. [^S04]

Antes de comprar una interfaz o prometer cobertura, registrar los vehículos más habituales del taller:

| Marca / modelo | Año / motor | Mercado de origen | Protocolo observado | Interfaz usada | Lecturas comprobadas | Resultado |
|---|---|---|---|---|---|---|
| Completar | Completar | Completar | Pendiente | Pendiente | Pendiente | No validado |

### Criterio de cobertura del proyecto

- **Verificado:** función probada en esa combinación de vehículo, motor, interfaz y versión del programa.
- **Parcial:** algunas consultas funcionan; se documentan las ausentes o fallidas.
- **No validado:** no se ha probado, aunque teóricamente sea compatible.
- **Fuera de alcance:** protocolo o módulo no implementado.

Tener conector de diagnóstico no alcanza para prometer acceso a todos los módulos. Multiecuscan publica una matriz donde el acceso cambia según vehículo, centralita e interfaz/adaptación necesaria. [^S05]

**Recorte propuesto:** comenzar con un automóvil conocido que responda a OBD clásico sobre CAN de 11 bits. Después validar CAN de 29 bits. Incorporar ISO 9141-2, ISO 14230-4/KWP y J1850 únicamente cuando haya pruebas para sus formatos; que el adaptador los soporte no significa que nuestro parser ya los interprete.

OBDonUDS tiene su propia especificación, SAE J1979-2. No se debe asumir que implementar los modos clásicos `01`–`0A` cubre automáticamente ese estándar. Tampoco se promete DoIP, CAN FD ni diagnóstico de baterías de tracción. [^S17]

## 4. Interfaz física y conectividad móvil

El auto no se conecta mágicamente al teléfono: sigue haciendo falta un **adaptador OBD2 programable**. La app reemplaza parte del software de diagnóstico, no la electrónica de comunicación. Mantener el alcance inicial en automóviles de 12 V y en las condiciones admitidas por el fabricante de la interfaz.

### Matriz de transportes: capacidad técnica, no certificación de nuestro producto

| Transporte | Android | Windows | iPhone/iPad | Decisión del proyecto |
|---|---|---|---|---|
| Bluetooth LE / BLE | Candidato | Candidato, con radio y controlador compatibles | Candidato | Primera opción si el adaptador tiene GATT documentado |
| Bluetooth clásico / SPP-RFCOMM | Implementación específica posible | Implementación específica o COM virtual, según dispositivo | No asumir acceso SPP genérico | Alternativa si el hardware económico elegido usa este perfil |
| USB con COM virtual | No mediante el COM de Windows | Candidato | No incluido | Ampliación para la PC |
| USB Host / OTG | Necesita soporte del teléfono, permisos y controlador del chip | No corresponde a esta ruta | No incluido | Fuera del MVP |
| Wi-Fi / TCP | Candidato | Candidato | Candidato | Alternativa posterior; revisar red local, permisos y rutas |

Shiny documenta BLE, Wi-Fi/TCP y serial como transportes separados. Su transporte serial no proporciona USB en Android: ese sistema requiere una integración diferente con USB Host. Android documenta RFCOMM para Bluetooth clásico. [^M03] [^M04] [^M05]

**No comprar basándose únicamente en las palabras “ELM327”, “Bluetooth” o “compatible con iPhone”.** Confirmar modelo, firmware, transporte y protocolo expuesto a aplicaciones de terceros. Que funcione en la aplicación del fabricante no demuestra que nosotros podamos acceder a la misma interfaz.

### Elección de la unidad piloto

Propuesta: **un adaptador BLE programable y documentado**, porque permite validar Android y Windows con el mismo tipo de conexión y deja una ruta razonable para iOS. Esto es una elección de arquitectura, no una recomendación de un producto ni una afirmación de precio.

Antes de comprar, comprobar:

- Que el canal acepte comandos ELM/STN y no esté restringido a una app cerrada.
- Servicio GATT, característica de escritura y característica de respuesta, con sus UUID documentados; modo Write/WriteWithoutResponse y Notify/Indicate.
- Protocolos OBD eléctricos soportados por ese modelo. Tener BLE no implica soportar todos los protocolos OBD anteriores a CAN.
- Funcionamiento del modelo concreto en los sistemas operativos previstos y posibilidad de probar/devolver.
- Procedimiento de emparejamiento, recuperación, reposo y desconexión indicado por el fabricante.

La documentación BLE de Shiny permite configurar servicios/características. No existe un UUID OBD único que se deba inventar para todos los dongles. No adoptar el nombre anunciado como única prueba de identidad. [^M06]

**Para cuidar el presupuesto:** si ya se dispone de un buen adaptador Bluetooth clásico, se puede elegir ese para Android + Windows. En ese caso hay que implementar el transporte correspondiente; instalar una biblioteca BLE no lo vuelve compatible. Se pospone iOS hasta elegir una ruta adecuada.

### iPhone: precaución específica

No afirmar que “iPhone no tiene Bluetooth clásico”: esa simplificación es incorrecta. Lo importante es el perfil accesible a nuestra app. Un adaptador SPP genérico de Android no se da por compatible con iOS. Apple distingue Core Bluetooth y el acceso mediante External Accessory/MFi; para este proyecto se priorizaría BLE documentado o, tras validación, Wi-Fi/TCP. [^M07]

### Prueba de aceptación

Descubrir/seleccionar la interfaz → resolver permisos → establecer el transporte → identificar adaptador → obtener respuesta OBD válida → repetir lecturas → recibir respuesta larga → desconectar/reconectar de manera controlada.

**Un teléfono y una PC no consultarán simultáneamente el mismo adaptador en el MVP.** Es una regla del producto, aun si algún hardware ofreciera más posibilidades. Para trabajar en varios autos a la vez, cada sesión tendrá su propia interfaz.

No hay precios ni modelo de compra fijados. El piloto valida una combinación concreta antes de anunciar compatibilidad con otras.

## 5. Biblioteca de comunicación: evaluación multiplataforma de Shiny.Obd

Shiny.Obd sigue siendo candidata: su documentación ofrece núcleo de comandos, BLE, Wi-Fi/TCP, serial y emulador. La ficha NuGet del núcleo consultada el 14/10/2026 muestra la versión **1.4.0**, destino `net10.0` y licencia MIT. Esto no confirma las versiones ni las dependencias transitivas de todos los transportes. [^S07] [^M03]

### Separación obligatoria

- `Shiny.Obd`: posible motor dentro del backend, nunca dependencia del dominio ni de los ViewModels.
- `Shiny.Obd.Ble`: candidato para BLE; evaluar su integración MAUI y sus dependencias por plataforma.
- `Shiny.Obd.Serial`: exclusivamente donde corresponda, inicialmente Windows. No agregarlo indiscriminadamente al destino Android. [^M04]
- `Shiny.Obd.Wifi`: alternativa posterior, no permiso para omitir configuración del sistema operativo.
- Bluetooth clásico: adaptador específico sobre RFCOMM/COM según plataforma; **no asumir que el paquete BLE lo implementa**.

### Prueba de viabilidad antes de integrarla

1. Restaurar y compilar paquetes concretos para Android y Windows, también en Release.
2. Verificar la API efectivamente instalada, licencias y perfiles del adaptador.
3. Comprobar un transporte real en Android y el mismo perfil en Windows.
4. Acceder a trazas crudas, cancelación y recuperación de respuestas tardías.
5. Conservar identidad de la ECU, mensajes largos y estados de error.
6. Interceptar y validar todos los comandos del motor, incluida inicialización, antes de enviarlos.
7. Probar el cierre al pasar a segundo plano y la reconexión sin duplicar tareas.

**Atención al origen:** el descubrimiento documentado de PIDs puede combinar máscaras de varias ECU. El proyecto necesita resultados por origen, no solo una lista agregada. Verificar la API de la versión elegida; no atribuir datos agregados a una ECU inventada. [^S07]

Crear `IReadOnlyObdBackend` y un backend simulado primero. Luego, implementar un `ShinyObdBackend` y una fábrica de transportes específica de plataforma. Si la biblioteca no satisface integridad, origen o política de comandos, documentar el bloqueo y elegir una extensión acotada o un backend propio, no dos motores paralelos.

Guardar versiones exactas y archivos de bloqueo. No se comprobó aquí la compilación ni la operación real de esos paquetes. Que el paquete restaure no certifica que Bluetooth funcione en un teléfono.

## 6. Límite de seguridad del MVP

“Solo lectura” significa que el programa envía consultas, pero no solicita cambios de configuración, borrados ni activaciones en el vehículo. **No significa escucha pasiva del bus ni riesgo cero.**

### Política propuesta

La aplicación expone operaciones tipadas, no un método público `SendAnything(string)`.

| Solicitud | Política inicial |
|---|---|
| Modo `01`, PIDs incluidos en un catálogo aprobado | Permitida tras comprobar soporte |
| Modo `02`, consultas de freeze frame implementadas | Permitida cuando se habilite esa etapa |
| Modos `03`, `07`, `0A` | Lectura de DTC, con resultado independiente |
| Modo `09`, descubrimiento y VIN implementados | Permitida |
| Comandos AT de identificación/configuración del adaptador | Lista cerrada, revisada por perfil |
| Modo `04` | Bloqueado |
| Modo `08`, UDS y comandos específicos de fabricante | Bloqueados en el MVP |
| Entrada arbitraria de hexadecimal o scripts descargados | No existe en la interfaz |

La lista de comandos permitidos debe validarse cerca de la salida al transporte, incluyendo los comandos de inicialización que genere una biblioteca externa. Registrar las denegaciones sin enviar nada al adaptador.

La política debe validar comando completo, longitud, parámetros y contexto. No basta con revisar que una cadena empiece con `01` o `AT`. Rechazar saltos de línea incrustados y comandos vacíos.

No implementar bypass de gateways ni desbloqueos improvisados. Bosch documenta que determinados vehículos requieren acceso autorizado para funciones protegidas; disponer de un conector y un adaptador no concede ese acceso. La disponibilidad regional debe verificarse antes de planear integraciones para Argentina. [^S16]

## 7. Arquitectura propuesta

```text
TallerScan.slnx
├── src/
│   ├── TallerScan.Core/                 # net10.0, sin MAUI ni SDK de plataforma
│   │   ├── Models/
│   │   ├── Contracts/
│   │   ├── Diagnostics/
│   │   └── Safety/
│   ├── TallerScan.Application/          # sesiones, casos de uso, coordinación
│   ├── TallerScan.Infrastructure/       # implementaciones portables
│   │   ├── Obd/                        # backend y parsing, sin abrir COM aquí
│   │   ├── Simulation/
│   │   ├── Persistence/
│   │   └── Reporting/
│   └── TallerScan.App/                  # MAUI: Android + Windows
│       ├── Views/
│       ├── ViewModels/
│       ├── Resources/
│       ├── Services/
│       ├── Platforms/
│       │   ├── Android/                # permisos, BLE/SPP según transporte elegido
│       │   └── Windows/                # permisos, BLE/serial según transporte elegido
│       └── MauiProgram.cs              # composición e inyección de dependencias
├── tests/
│   └── TallerScan.Tests/               # pruebas net10.0 sin emulador Android
│       ├── Fixtures/
│       ├── Protocol/
│       ├── Safety/
│       └── Persistence/
└── docs/
    ├── TALLERSCAN_PLAN.md
    ├── COMPATIBILIDAD.md
    ├── DECISIONES.md
    └── ENTORNO.md
```

`Platforms/iOS` se incorpora cuando se habilite esa entrega; no crear un destino que obligue a disponer de Mac para compilar Android + Windows.

Flujo de cada instalación:

```text
UI MAUI + ViewModels
  → coordinador de sesión / casos de uso
  → backend de diagnóstico de solo lectura
  → política de solicitudes + registro TX/RX
  → transporte de la plataforma
  → adaptador OBD2
  → vehículo

Resultados normalizados → SQLite local → historial / gráficos / exportaciones
```

El celular accede directamente al adaptador. **No interviene un servidor ni tiene que estar encendida la computadora.** Esa independencia es un requisito del producto.

### Dependencias y límites

`Core` no depende de MAUI, Android, WinUI, SQLite ni Shiny. `Application` depende de contratos del núcleo. `Infrastructure` implementa persistencia, simulación y backend portable. `App` compone lo anterior y aporta los transportes/permisos nativos. Si una dependencia concreta exige multitargeting, aislarla en App o en un proyecto específico, no contaminar el núcleo.

La referencia a un paquete serial y los archivos que usan `System.IO.Ports` se condicionan al destino Windows. El destino Android no debe terminar ejecutando esa ruta. Evitar que un `using` de plataforma rompa la compilación de otra.

Un único backend es dueño de la sesión. Definir con precisión qué capa encuadra respuestas ELM y cuál interpreta OBD: no hacer el reensamblado dos veces si la biblioteca ya entrega mensajes completos.

### Target frameworks y entorno

Propuesta de destinos: `net10.0-android` y `net10.0-windows10.0.19041.0`, confirmados contra la plantilla y el workload instalados. Las bibliotecas de dominio y tests siguen en `net10.0`. No agregar iOS, Mac Catalyst ni destinos preview al primer entregable.

No confundir el TFM del SDK de Windows con la versión mínima de Windows que se decida admitir. Fijar los mínimos de Android y Windows después de revisar los equipos reales del taller y las dependencias; registrarlos en `ENTORNO.md`. Microsoft publica por separado los requisitos de plataforma y los del entorno de compilación. [^M02]

### Contratos iniciales propuestos

Este bloque define el límite de la aplicación; no implementa el protocolo ni depende de una biblioteca externa. Los nombres se pueden ajustar durante la primera etapa.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TallerScan.Core;

public enum ReadStatus
{
    Success,
    NoData,
    Unsupported,
    Timeout,
    InvalidResponse,
    CommunicationError,
    Cancelled,
    NotQueried
}

public enum DtcKind { Stored, Pending, Permanent }

public enum TransportKind { Simulation, Ble, BluetoothClassic, Serial, Wifi }

// Contratos propios: representar opciones NO significa implementarlas todas.
public abstract record AdapterEndpoint;
public sealed record SimulatedEndpoint(string ScenarioId) : AdapterEndpoint;
public sealed record BleEndpoint(string DeviceId, string GattProfileId) : AdapterEndpoint;
public sealed record ClassicEndpoint(string DeviceId, Guid ServiceId) : AdapterEndpoint;
public sealed record SerialEndpoint(string PortName, int BaudRate) : AdapterEndpoint;
public sealed record WifiEndpoint(string Host, int Port) : AdapterEndpoint;

public sealed record AdapterSettings(
    AdapterEndpoint Endpoint,
    string AdapterProfileId);

public sealed record DiscoveredAdapter(
    string DisplayName,
    AdapterEndpoint Endpoint,
    bool IsValidated);

public interface IAdapterDiscovery
{
    IAsyncEnumerable<DiscoveredAdapter> DiscoverAsync(
        TransportKind kind, CancellationToken cancellationToken);
}

// Address puede ser null si no se pudo identificar el emisor.
// Nunca convertir ese null automáticamente en "motor" o "7E8".
public sealed record EcuOrigin(string? Address, string? Name);

public sealed record PidReading(
    EcuOrigin Origin,
    byte Pid,
    double? Value,
    string Unit,
    ReadStatus Status,
    DateTimeOffset ReceivedAtUtc,
    string? RawTraceId);

public sealed record EcuCapabilities(
    EcuOrigin Origin,
    IReadOnlySet<byte> SupportedMode01Pids,
    ReadStatus Status);

public sealed record DtcModuleResult(
    EcuOrigin Origin,
    DtcKind Kind,
    IReadOnlyList<string> Codes,
    ReadStatus Status,
    DateTimeOffset ReceivedAtUtc,
    string? RawTraceId);

public sealed record VinResult(
    EcuOrigin Origin,
    string? Vin,
    ReadStatus Status,
    string? RawTraceId);

public sealed record ConnectionReport(
    bool AdapterConnected,
    bool VehicleResponding,
    string? AdapterIdentifier,
    string? Protocol,
    string? Message);

public interface IReadOnlyObdBackend : IAsyncDisposable
{
    Task<ConnectionReport> ConnectAsync(
        AdapterSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<EcuCapabilities>> DiscoverAsync(
        CancellationToken cancellationToken);

    // Una consulta funcional puede producir respuestas de varias ECU.
    Task<IReadOnlyList<PidReading>> ReadPidAsync(
        byte pid, CancellationToken cancellationToken);

    Task<IReadOnlyList<DtcModuleResult>> ReadDtcsAsync(
        DtcKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<VinResult>> ReadVinAsync(
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
```

Reglas para la implementación:

- Validar configuración y entradas antes de abrir el transporte.
- DeviceId es un identificador opaco de la plataforma, no una MAC universal ni una identidad compartida entre equipos.
- GattProfileId referencia un catálogo local revisado con UUID y modo de escritura del adaptador concreto; no permite scripts.
- Un transporte no implementado se muestra como no disponible, nunca como opción que falla silenciosamente.
- Separar permisos denegados, Bluetooth apagado, dispositivo ausente y vehículo sin respuesta.
- Cancelar el descubrimiento y desechar sus recursos al salir de la pantalla.
- Una lectura fallida devuelve estado explícito y valor nulo, no cero.
- Una falla global antes de recibir respuestas genera un resultado de error de origen desconocido o un error de operación tipado; nunca una lista vacía que parezca éxito.
- Para fallos de programación/configuración usar excepciones; para respuestas esperables del vehículo, estados del dominio.
- Ninguna referencia al backend concreto debe llegar a los ViewModels.

## 8. Sesión y conexión

Estado propuesto:

```text
Disconnected → CheckingPermissions → OpeningTransport → InitializingAdapter
             → DetectingVehicle → DiscoveringCapabilities → Ready
Ready → Reading / Recording → Ready
Cualquier estado → Recovering o Faulted → Disconnected
```

**No confundir:** transporte conectado, adaptador identificado y vehículo respondiendo son tres hitos distintos. Un enlace BLE establecido no significa que la ECU respondió.

Flujo de conexión:

1. Elegir transporte y comprobar disponibilidad/permisos; descubrir interfaces y pedir selección explícita. En Windows/USB, listar COM sin sondear todos los equipos seriales del taller.
2. Abrir el transporte del adaptador elegido. Para BLE, descubrir servicios y suscribirse a respuestas antes de enviar comandos. Para COM, usar su configuración serial.
3. Ejecutar el perfil de inicialización aprobado, esperando el final de cada respuesta.
4. Solicitar una consulta OBD de descubrimiento y verificar una respuesta válida.
5. Registrar protocolo, origen de las respuestas y capacidades.
6. Habilitar únicamente funciones verificadas por esa sesión.

Para un backend ELM propio, una secuencia de referencia es `ATZ`, `ATE0`, `ATL0`, `ATS1`, `ATH1`, `ATSP0`, seguida de `0100` y consultas de identificación de protocolo. Debe adaptarse al dispositivo y sus respuestas. Reiniciar el adaptador con `ATZ` no equivale a borrar fallas con modo `04`. [^S01]

Esta secuencia **no se superpone** a otra ejecutada por Shiny: un único componente controla la sesión. En un backend propio, los ajustes CAN de formato y flow control se aplican mediante un perfil documentado; no se copian a protocolos K-Line indiscriminadamente.

### Planificador único

Una única cola/worker posee la conexión. Las lecturas de sensores, el VIN y los DTC pasan por ese mismo coordinador; no ejecutar varios `Execute` en paralelo contra el mismo adaptador.

La recepción debe acumular fragmentos hasta completar la respuesta y reconocer el prompt `>`. Una lectura de puerto, recepción TCP o notificación BLE no tiene por qué coincidir con una respuesta completa. El comando enviado se termina con retorno de carro. Un retorno vacío puede repetir el comando anterior, por lo que no debe usarse como “ping” genérico. [^S01]

Propuesta de recuperación:

- Distinguir timeout de conexión, de comando y de operación global.
- Tras cancelar o vencer un comando, no atribuir su respuesta tardía a la siguiente consulta.
- Esperar y drenar de forma acotada hasta recuperar sincronía; si no se recupera, cerrar/reinicializar la sesión.
- No borrar el buffer de entrada rutinariamente antes de cada comando.
- Limitar tamaño de respuesta, longitud de línea y tiempo total.
- No realizar reintentos infinitos ni reconexiones invisibles a otro vehículo.

### 8.1. Permisos de Android

La ruta exacta depende del SDK objetivo, de la versión del dispositivo y del transporte. Para Bluetooth en Android 12 o posterior, revisar `BLUETOOTH_SCAN` para descubrimiento y `BLUETOOTH_CONNECT` para conexión, con autorización en tiempo de ejecución. No pedir `BLUETOOTH_ADVERTISE` si la app solo actúa como cliente. [^M08]

Para versiones anteriores, revisar los permisos históricos de Bluetooth y ubicación que correspondan al descubrimiento. No copiar un manifest único sin analizar versiones. `neverForLocation` se puede declarar solo si realmente no se deduce ubicación; Android advierte que puede filtrar ciertos anuncios BLE. Probar el adaptador objetivo. [^M08]

El modo simulado y el historial deben abrir sin conceder Bluetooth. La app debe explicar permisos denegados, permitir reintentar y ofrecer acceso a ajustes cuando el sistema ya no muestre el diálogo. No intentar habilitar Bluetooth por la fuerza ni exigir permisos ajenos a la función.

No se requieren ubicación continua, cámara, micrófono ni acceso general a todos los archivos para el MVP propuesto.

### 8.2. Recepción BLE y pérdida de conexión

Propuesta de implementación, a contrastar con el perfil del hardware:

- Suscribirse a notificaciones/indicaciones antes de la primera consulta.
- Respetar tamaño de escritura permitido y fragmentar cuando corresponda; no fijar un MTU universal.
- Serializar escrituras y consultas OBD. No confundir confirmación GATT con respuesta de la ECU.
- Separar buffers por sesión/conexión y limitar su tamaño.
- Cancelar suscripciones y operaciones pendientes al perder el enlace.
- Incrementar un identificador de generación de conexión para descartar callbacks tardíos de una conexión anterior.
- Reconectar solo al dispositivo seleccionado y con una nueva validación del vehículo; no reutilizar ciegamente VIN ni capacidades.

Los UUID y modos de característica se obtienen de documentación o de un perfil probado; no se generan por conjetura. [^M06]

### 8.3. Ciclo de vida móvil y suspensión

**MVP: diagnóstico y registro en primer plano.** No se promete grabación continua con la app minimizada o el teléfono bloqueado.

Cuando la ventana deja de estar visible (`Stopped`, según el mapeo de la plataforma), cancelar nuevas consultas, intentar guardar el lote pendiente y cerrar el transporte de forma acotada. Guardar periódicamente durante la operación normal: el cierre definitivo puede no dar tiempo para ejecutar limpieza. Microsoft recomienda cancelar procesos largos al entrar en estado detenido y advierte que la app puede no volver de él. [^M09]

Al volver, mostrar “sesión pausada/interrumpida” y pedir reanudar conexión. Revalidar adaptador, vehículo y capacidades; no duplicar el worker. En Windows, perder foco no es lo mismo que suspender: cambiar a otra ventana no debe por sí solo cortar una sesión visible.

El diálogo de permisos o el selector de compartir pueden provocar cambios de foco. Probarlos sin crear ciclos de conexión/desconexión. El registro con pantalla apagada, servicios de primer plano de Android o modos especiales de iOS se estudia como una etapa separada.

## 9. Procesamiento de respuestas: dónde hay que ser riguroso

Pipeline propuesto:

```text
Bytes/notificaciones del transporte
 → respuesta textual completa del adaptador
 → clasificación de eco, avisos, errores y líneas de datos
 → interpretación del formato según protocolo/perfil
 → mensajes completos por ECU
 → validación de servicio, PID y longitud
 → decodificación
 → resultado con unidad, hora, origen y calidad
```

### Reglas de diseño

**No concatenar todos los números hexadecimales del texto.** Mantener separados cabeceras, longitudes, índices de trama, padding y datos útiles.

**No tomar siempre “la primera respuesta”.** Dos centralitas pueden contestar a la misma consulta. Conservar cada respuesta y su origen; no usar el orden de llegada para identificar el motor.

**No asumir que `ATH1` soluciona el parser.** Mostrar cabeceras aporta información, pero obliga a interpretarlas. Los formatos CAN con cabeceras, los mensajes largos y los protocolos anteriores requieren tratamiento diferente. El manual ELM presenta ejemplos de respuestas intercaladas de varias ECU. [^S01]

**No implementar ISO-TP dos veces.** Determinar qué entrega el perfil del adaptador: tramas con información de transporte o mensajes ya formateados. El backend es responsable de entregar mensajes lógicos completos una sola vez.

**No decodificar respuestas incompletas como valores parciales.** Una secuencia faltante, una longitud incoherente o un origen ambiguo deben invalidar el mensaje correspondiente, no provocar una lectura aparentemente válida.

**Los errores también son información.** Conservar el texto original y clasificar `NO DATA`, `UNABLE TO CONNECT`, `STOPPED`, `CAN ERROR`, `BUS ERROR`, `BUFFER FULL` y `?`. Una respuesta negativa tampoco se convierte en dato de sensor. Las referencias de los adaptadores describen errores y condiciones de comunicación; el mapeo a estados de la aplicación es nuestro. [^S18]

## 10. Catálogo inicial de sensores

En este cuadro, `A` y `B` son bytes de **datos útiles** después de retirar servicio y PID, no los primeros bytes de una trama completa.

| PID modo 01 | Parámetro | Bytes | Conversión | Unidad |
|---|---|---:|---|---|
| `04` | Carga calculada | 1 | `A * 100.0 / 255` | % |
| `05` | Temperatura refrigerante | 1 | `A - 40` | °C |
| `06` | Corrección combustible corta, banco 1 | 1 | `(A - 128) * 100.0 / 128` | % |
| `07` | Corrección combustible larga, banco 1 | 1 | `(A - 128) * 100.0 / 128` | % |
| `0B` | Presión absoluta admisión | 1 | `A` | kPa |
| `0C` | RPM | 2 | `(256 * A + B) / 4.0` | rpm |
| `0D` | Velocidad | 1 | `A` | km/h |
| `0F` | Temperatura aire admisión | 1 | `A - 40` | °C |
| `10` | Caudal de aire MAF | 2 | `(256 * A + B) / 100.0` | g/s |
| `11` | Posición mariposa | 1 | `A * 100.0 / 255` | % |
| `42` | Tensión informada por módulo | 2 | `(256 * A + B) / 1000.0` | V |

Las tablas de comandos y decodificadores abiertos permiten contrastar identificadores, unidades y escalas. No se propone copiar una base completa de códigos o de reparaciones. [^S13] [^S14]

### Catálogo y descubrimiento

Cada definición debe contener PID, nombre en español, unidad, longitud esperada, decodificador identificado por nombre, categoría, revisión y fuente.

Usar decodificadores compilados o identificadores cerrados: no ejecutar expresiones C# o scripts arbitrarios provenientes de JSON.

Consultar los bloques de soporte desde `0100` y continuar según los bits de continuación. Mantener las capacidades por ECU. No consultar todas las combinaciones posibles ni asumir que el mapa de modo `01` garantiza soporte de modo `02` o `09`.

Para un bloque de cuatro bytes `A B C D`, se puede construir una máscara unsigned en orden de red:

```csharp
uint mask = ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;
// Para offset 1..32 respecto del PID base:
bool supported = (mask & (1u << (32 - offset))) != 0;
```

La interpretación anterior debe quedar cubierta por pruebas de límites y orientación de bits. El descubrimiento por bloques forma parte de las implementaciones documentadas. [^S08]

### Advertencias para mostrar datos

No presentar `ATRV` y PID `0142` como idéntica medición: etiquetar por separado tensión observada por la interfaz y tensión reportada por el módulo. [^S01] [^S13]

No llamar “presión de turbo” directamente al PID de presión absoluta. No llamar “pedal acelerador” a posición de mariposa. No prometer kilometraje total, presión de aceite ni todos los sensores físicos: la aplicación solo puede mostrar las lecturas que consigue interpretar con una definición válida.

Los rangos de codificación no son límites de funcionamiento normal. No pintar un sensor como averiado solo por usar un umbral genérico sin contexto de motor, estado de marcha y documentación.

## 11. Códigos de falla, estado y freeze frame

Leer almacenados, pendientes y permanentes como consultas independientes. El mismo código puede aparecer en más de una categoría; no eliminar esa distinción. La API de Shiny documenta los modos `03`, `07` y `0A`. [^S08]

Guardar por resultado: sesión, ECU/origen, categoría, código, estado de consulta, fecha, referencia a la traza y descripción utilizada con su fuente/revisión.

**Un listado vacío solo significa “sin códigos en esta consulta” cuando hubo respuesta válida.** `NO DATA`, timeout, módulo no consultado y cero códigos son situaciones distintas.

La normalización de DTC debe conocer el formato del protocolo. No asumir que todos los mensajes tienen o carecen del byte de cantidad. No interpretar `00 00` de relleno como una falla `P0000`. Los decodificadores abiertos muestran estas diferencias de procesamiento. [^S07] [^S14]

Los DTC permanentes no se borran con una orden convencional del escáner ni desconectando la batería; el sistema OBD los elimina al verificar las condiciones correspondientes tras resolver la causa. No ofrecer un botón “forzar eliminación”. [^S15]

### Freeze frame

Implementarlo después de las lecturas básicas. Verificar que exista una captura válida y asociar su identificador y DTC causal cuando estén disponibles. No confundir valores vacíos/relleno con mediciones reales, ni asumir una captura por cada código. La documentación de Shiny advierte específicamente sobre lecturas engañosas si se consulta una captura inexistente. [^S08]

### Descripciones

Empezar con un catálogo reducido de definiciones verificadas y procedencia documentada. Cuando falte una definición, mostrar el código con “descripción no disponible”. No inventar una explicación usando únicamente su prefijo.

## 12. Lecturas en vivo y registro

Configuración de partida propuesta, a ajustar con mediciones:

| Grupo | Objetivo inicial |
|---|---|
| RPM y parámetros seleccionados | Aproximadamente una muestra por segundo por PID |
| Temperaturas y tensión | Cada 2–5 segundos |
| Estado general / monitores | Bajo demanda o cada 15–30 segundos |
| DTC y VIN | Bajo demanda; no dentro del sondeo rápido |
| Refresco de pantalla | Hasta 5 veces por segundo, independiente del muestreo |

Estos intervalos son **objetivos de diseño**, no tasas garantizadas del adaptador. La frecuencia efectiva depende de la latencia y de cuántas consultas se hagan. Si se seleccionan más PIDs, reducir la frecuencia y mostrar la tasa real; no crear concurrencia artificial para aparentar velocidad.

Registrar la hora de recepción y, cuando sea útil, el inicio de la solicitud. No afirmar que todos los sensores se midieron simultáneamente ni que la hora de recepción sea exactamente la de muestreo interno de la ECU.

Al perder comunicación, conservar el último dato como histórico con su antigüedad y cambiar el indicador a “sin actualización”. Nunca dibujar un cero nuevo para representar desconexión.

Usar almacenamiento y gráficos con límites de memoria. Las muestras históricas no deben crecer indefinidamente en una `ObservableCollection`.

## 13. Persistencia local

Propuesta: **SQLite en cada instalación**, con `sqlite-net-pcl` detrás de repositorios propios. Microsoft documenta esta integración para MAUI y también menciona `Microsoft.Data.Sqlite` como alternativa. Se elige un solo proveedor para el MVP; no combinar varios ORM sin necesidad. [^M11]

Esto reemplaza la decisión inicial de EF Core para escritorio. EF Core no queda prohibido, pero incorporarlo al cliente móvil exige una prueba específica de publicación Release, linking/AOT y migraciones. No es una dependencia obligatoria de este plan.

La ruta de almacenamiento se resuelve en la capa MAUI con `FileSystem.AppDataDirectory` y se inyecta al repositorio. No usar rutas fijas `C:\...` ni guardar la base como un recurso de solo lectura dentro del APK. [^M11]

### Modelo mínimo

| Entidad | Datos principales |
|---|---|
| `Vehicles` | Id, patente, VIN opcional, marca/modelo/motor declarados y notas |
| `Sessions` | UUID, vehículo, DeviceId local, inicio/fin, operador, versión app, sistema operativo, transporte, interfaz, protocolo, estado final |
| `SessionModules` | Sesión, origen observado, nombre opcional y capacidades |
| `DiagnosticQueries` | Sesión, tipo de consulta, comienzo/fin, resultado global |
| `DtcModuleResults` | Consulta, módulo/origen y estado, incluso cuando hay cero códigos |
| `DtcObservations` | Resultado, categoría, código y definición aplicada |
| `PidSamples` | Sesión, origen, PID, valor nullable, unidad, calidad y tiempo |
| `FreezeFrames` | Identificador, origen, DTC causal y lecturas asociadas |
| `WorkshopNotes` | Observaciones humanas, pruebas y reparación realizada |
| `RawExchanges` | Solicitud, respuesta, tiempos, estado y configuración de interpretación |

Separar “el vehículo que seleccionó el operador” del VIN efectivamente leído. Una discrepancia requiere confirmación, no sobrescribir la ficha automáticamente.

### Reglas propuestas

- Marcas de tiempo persistidas como UTC Unix en milisegundos; mostrar hora local en pantalla.
- Guardado en lotes desde un worker, con transacciones cortas.
- Un servicio de persistencia coordina operaciones y lotes; no escribir desde cada callback de Bluetooth ni compartir transacciones abiertas con la UI.
- Migraciones versionadas; no borrar y recrear la base al actualizar.
- Índices sobre sesión/origen/PID/tiempo y sobre vehículo/inicio.
- Copia de seguridad consistente y prueba de restauración.
- Retención configurable de trazas y muestras; cierre explícito de sesiones interrumpidas.
- Mantener scripts de migración versionados, transacciones y pruebas. Crear tablas iniciales no reemplaza diseñar futuras migraciones.

No enviar VIN, patentes, nombres ni trazas a servicios externos automáticamente. El diagnóstico básico debe seguir funcionando sin red.

### Exportaciones

Primero CSV de lecturas y HTML de informe. Ambos deben indicar alcance, origen y consultas fallidas. Escapar el HTML; en CSV, tratar como texto los campos del usuario y neutralizar fórmulas que pudieran ejecutarse al abrirlo en una planilla.

Leyenda propuesta del informe:

> Se consultaron las funciones OBD2 indicadas. No constituye un escaneo completo de todos los módulos del vehículo. Las consultas fallidas o no soportadas se informan por separado.

### Compartir datos entre el celular y la PC

**Compartir código no comparte automáticamente la base de datos.** Cada instalación mantiene su propio historial. No prometer que un diagnóstico del teléfono aparecerá en Windows sin una transferencia.

Primera entrega de intercambio propuesta: paquete de sesión JSON versionado, exportado por decisión del usuario e importado en otro equipo. Incluir UUID de sesión, revisión de esquema, procedencia, lecturas y estados; validar tamaños, tipos y referencias. Reimportar la misma sesión no debe duplicarla. No sobrescribir notas de otro operador ni datos existentes silenciosamente.

Exportar un informe HTML/CSV permite leerlo; para incorporarlo al historial de otra instalación se necesita el importador estructurado. No confundir ambos usos.

Para sincronización automática futura: API ASP.NET Core por HTTPS, autenticación, base central, cola local de cambios y resolución de conflictos. Esa etapa puede usar SQL Server y EF Core en el servidor. **No conectar los celulares directamente a un SQL Server expuesto, ni compartir el archivo SQLite por una carpeta sincronizada.** Son restricciones de arquitectura del proyecto.

Proteger y probar las copias de seguridad. SQLite local no implica cifrado integral automático: decidir protección de datos, exportaciones y respaldo del sistema antes de guardar datos personales adicionales. No subir patentes, VIN ni trazas sin una acción explícita.

## 14. Pantallas MAUI para celular, tablet y PC

Una sola base de pantallas, con distribución adaptable. No diseñar una tabla de escritorio y achicarla al ancho del teléfono.

1. **Conexión:** simulación/real, transportes disponibles, permisos, adaptadores descubiertos, perfil y estado de vehículo.
2. **Vehículo y sesión:** ficha, VIN leído, observaciones y apertura/cierre de diagnóstico.
3. **Fallas:** tarjetas por código, categoría, origen y estado; mostrar también consultas fallidas.
4. **Sensores:** parámetros elegidos, unidades, antigüedad y botón iniciar/detener registro.
5. **Historial:** sesiones, notas, comparación e importación/exportación.
6. **Diagnóstico técnico:** trazas de solo lectura, con límites de tamaño y opción de quitar identificadores al compartir.

### Controles y distribución

Usar `VerticalStackLayout` y `HorizontalStackLayout` dentro de formularios, encabezados y tarjetas. Son layouts de MAUI; no usar `StackPanel`, que pertenecía al planteo WPF. Mantener la preferencia de no usar `Grid` ni `DataGrid`. [^M10]

Para listas extensas, usar `CollectionView` con `LinearItemsLayout`. Colocarla como contenido principal de la página y usar su `Header`/`Footer` para los controles que deban compartir el desplazamiento. Así se evita depender de un panel vertical que le mida una altura ilimitada.

Microsoft advierte que alojar `CollectionView` dentro de un StackLayout/VerticalStackLayout puede impedir el scroll, y desaconseja anidarla en un ScrollView. Por eso no envolver el historial completo en varios contenedores desplazables ni reemplazar la virtualización por miles de controles. [^M12]

Propuesta de presentación: teléfono en una columna, navegación clara y botones táctiles; tablet/Windows con más ancho, tarjetas aprovechando el espacio y detalle de la selección. Ajustar distribución según ancho real de ventana, orientación y tamaño de texto, no solo según una etiqueta “Desktop”.

### Contratos de UI

Los ViewModels dependen de interfaces para diagnóstico, historial, exportación y navegación. No abren sockets ni decodifican bytes. No usar `.Wait()`/`.Result`. Las actualizaciones de colecciones visibles se despachan al hilo de UI y se agrupan; no redibujar una pantalla por cada fragmento BLE.

Botones del MVP: conectar, desconectar, consultar fallas, iniciar registro, detener, exportar e importar. No agregar borrar, activar ni programar, ni siquiera ocultos.

Distinguir: simulación visible, interfaz no compatible, permiso denegado, conexión perdida, respuesta inválida y valor antiguo. Nunca mostrar un error como cero ni mezclar datos simulados con diagnósticos reales.

Las acciones de compartir/importar usan servicios de plataforma detrás de interfaces. Cancelar el selector no debe cerrar la sesión ni considerarse un error del vehículo.

## 15. Pruebas automatizadas y datos simulados

El backend simulado debe funcionar sin automóvil. Para pruebas de transporte se puede evaluar el emulador TCP de Shiny; un emulador del mismo proveedor no es un oráculo independiente, por lo que también hacen falta fixtures propios y capturas reales revisadas. [^S21]

### Vectores de decodificación

Estos ejemplos son **mensajes lógicos ya normalizados**, no texto completo recibido del adaptador. Los resultados numéricos se obtienen de las conversiones de la sección 10.

| Mensaje lógico | Resultado esperado |
|---|---|
| `41 0C 1A F8` | 1726 rpm |
| `41 0C 00 00` | 0 rpm válido, no “sin dato” |
| `41 05 7B` | 83 °C |
| `41 0D 28` | 40 km/h |
| `41 10 01 F4` | 5 g/s |
| `41 06 80` | 0 % |
| `41 42 36 B0` | 14 V |
| `41 0C 1A` | Respuesta incompleta; sin valor |
| `41 05 7B` para una solicitud de RPM | PID incorrecto; no decodificar como RPM |

### Ejemplos de encuadre CAN

Fixtures sintéticos propuestos para un perfil con cabeceras y sin columna DLC adicional:

```text
# Mismo PID, dos emisores; no sobrescribir uno con el otro.
7E8 04 41 0C 1A F8 00 00 00
7E9 04 41 0C 0F A0 00 00 00
>
```

Esperado: conservar 1726 rpm del origen `7E8` y 1000 rpm de `7E9`. No inferir nombres de módulos solo por este ejemplo.

```text
# DTC en respuesta CAN de una trama; 06 es longitud del payload,
# 43 el servicio de respuesta y 02 la cantidad en este formato.
7E8 06 43 02 03 01 04 20 00
>
```

Esperado: códigos `P0301` y `P0420`, origen `7E8`. Agregar otro fixture con el formato anterior a CAN para impedir que el parser imponga la misma disposición a todos los protocolos.

Las direcciones de ejemplo y el formato elegido no son constantes universales. Estos fixtures se deben contrastar con el perfil del adaptador y las capturas reales antes de declarar compatibilidad.

### Pruebas móviles y multiplataforma adicionales

- [ ] La misma sesión simulada funciona en Windows y Android.
- [ ] La app abre y permite consultar historial sin permisos Bluetooth ni Internet.
- [ ] Permiso denegado, revocado y Bluetooth apagado tienen mensajes distintos.
- [ ] Descubrimiento cancelado no deja callbacks ni conexiones activas.
- [ ] Respuesta dividida entre varias notificaciones BLE o con varias líneas juntas.
- [ ] Salir, volver, bloquear y desbloquear no duplican el sondeo.
- [ ] Interrupción o muerte del proceso conserva los lotes ya confirmados.
- [ ] Cambiar orientación, tamaño de fuente o ventana no pierde datos ni controles.
- [ ] Colecciones largas se desplazan sin crecer indefinidamente en memoria.
- [ ] Compilación y ejecución Release en teléfono físico; Debug no es suficiente.
- [ ] Importar dos veces la misma sesión no genera duplicados.
- [ ] Una actualización instalada preserva la base local y aplica migraciones.
- [ ] Un transporte ausente no aparece como compatible por tener su enum definido.

### Pruebas imprescindibles

- [ ] Respuesta dividida en fragmentos arbitrarios; `>` llega por separado.
- [ ] Eco activado y desactivado; CR/LF, espacios y mayúsculas/minúsculas.
- [ ] Aviso de búsqueda seguido de datos válidos.
- [ ] `NO DATA`, error de adaptador, respuesta negativa y timeout diferenciados.
- [ ] Respuesta tardía de un comando cancelado no contamina el siguiente.
- [ ] Dos solicitudes desde pantallas distintas quedan serializadas.
- [ ] Múltiples ECU, orden variable y respuestas largas intercaladas.
- [ ] VIN de varias tramas; secuencia incompleta o longitud inválida rechazada.
- [ ] CAN de 11 y 29 bits comprobados de forma independiente.
- [ ] Máscaras de soporte con bits extremos y bloques de continuación.
- [ ] Lectura legítima cero diferenciada de valor ausente.
- [ ] DTC sin códigos diferenciado de DTC no consultados o fallidos.
- [ ] Límites de tamaño y tiempo impiden consumo ilimitado de memoria.
- [ ] Todas las escrituras y comandos no aprobados son bloqueados antes del transporte.
- [ ] Reinicio de la app conserva sesiones y datos ya guardados.
- [ ] Exportaciones escapan correctamente contenido introducido por usuarios.

## 16. Etapas de implementación

### Etapa 0 — Decisiones y entorno multiplataforma

Crear solución y registrar SDK, workloads MAUI, Android SDK/JDK, SDK Windows y versiones exactas. Elegir teléfono Android y PC de prueba. Anotar modelo del adaptador/vehículo o dejarlos expresamente pendientes.

Hacer la prueba de dependencias en un proyecto aislado, sin conectar a un automóvil. Registrar qué transportes se pueden compilar y cuáles realmente se probaron.

**Terminado cuando:** hay una base que compila para Android y Windows en entornos adecuados, tests de dominio ejecutables y una lista honesta de pendientes. No confundir cross-targeting con haber ejecutado en ambos equipos.

### Etapa 1 — Núcleo, simulación y UI MAUI

Implementar contratos independientes de transporte, estados, catálogo, seguridad y simulación. Pantallas de conexión simulada, sensores y DTC en Android + Windows. Datos sintéticos claramente identificados.

**Terminado cuando:** ambas plataformas permiten iniciar/detener sesiones simuladas, diferenciar errores de valores cero y volver de una interrupción sin duplicar workers. No se envían comandos a ningún auto.

### Etapa 2 — Primer transporte real y permisos

Ruta propuesta: BLE con un solo modelo de interfaz, primero en teléfono Android físico y después en Windows. Implementar descubrimiento, selección, permisos, perfil GATT, inicialización, trazas y política de comandos.

Si el adaptador disponible es Bluetooth clásico, documentar la decisión y sustituir la ruta por SPP/RFCOMM/COM donde corresponda. No implementar BLE, Wi-Fi, serial y SPP simultáneamente “por las dudas”.

**Terminado cuando:** una sesión controlada de 20 minutos permite consultar los PIDs disponibles, detectar desconexión y recuperar sin mezclar respuestas. Repetir en ambas plataformas; es un criterio interno propuesto, no una certificación.

### Etapa 3 — DTC, VIN y origen

Completar categorías, mensajes largos y atribución por ECU. Comparar con una herramienta de referencia, usada por separado, en condiciones similares. Resolver pérdida de origen o mensajes incompletos antes de usar resultados del programa para el trabajo del taller.

**Terminado cuando:** quedan registrados resultados, discrepancias y límites por plataforma, vehículo e interfaz.

### Etapa 4 — Historial local e intercambio

Agregar SQLite, sesiones, notas, registro y reportes. Implementar JSON versionado para exportación/importación entre teléfono y PC, sin servidor. Probar backup, restauración, duplicados, cierre inesperado y migraciones.

**Terminado cuando:** un diagnóstico realizado en Android puede guardarse allí y trasladarse explícitamente al historial de Windows conservando origen, estados y referencias.

### Etapa 5 — Publicación interna y piloto

Generar una aplicación Android Release firmada y un paquete Windows según el canal interno elegido. Microsoft distingue APK para instalación Android y AAB para publicación en Google Play; no hace falta elegir una tienda para definir el funcionamiento del MVP. [^M13]

Custodiar keystore, certificados y contraseñas fuera del repositorio. Documentar instalación y actualizaciones. Probar actualización sobre una instalación anterior sin desinstalarla ni perder el historial.

**Terminado cuando:** se prueba la versión distribuible real en los dispositivos del taller; no solo desde Visual Studio. No afirmar que los emuladores verifican un enlace OBD físico.

### Etapa 6 — Ampliaciones independientes

Después del piloto, elegir una necesidad concreta: USB para Windows, otro transporte, freeze frame, más PIDs/protocolos, sincronización automática o iOS. No incluir todo en una misma entrega.

Para iOS: entorno Mac/Xcode compatible, firma, permisos, transporte probado y ejecución en iPhone físico. Declararlo compatible solo cuando pase sus pruebas. Para registro en segundo plano: analizar restricciones actuales de cada sistema y diseñarlo expresamente.

Las funciones específicas por marca, ABS/airbag, gateways y escritura siguen siendo proyectos separados, no consecuencias automáticas de tener una app móvil. [^S05] [^S16]

## 17. Pruebas sobre vehículos y operación del taller

Plan propuesto de validación:

1. Usar un vehículo autorizado, estacionado, con freno aplicado y transmisión en posición segura. Revisar conexión e instrucciones del fabricante de la interfaz.
2. Empezar con contacto dado y motor apagado para las consultas que lo permitan.
3. Para datos con motor encendido, trabajar con extracción de gases o ventilación adecuada y personal del taller a cargo. No manejar mientras se opera el celular, tablet o computadora.
4. Comparar primero con una herramienta conocida para detectar errores de nuestro software.
5. Probar pérdida de enlace Bluetooth o desconexión USB, cancelación, suspensión y cierre de la aplicación sin manipular sensores ni generar fallas deliberadamente.
6. Guardar las capturas, resultados y condiciones de prueba; quitar identificadores personales antes de compartirlas.

No incluir pruebas de actuadores, puentes en el conector, intervención en airbags, programación de módulos ni trabajos sobre alta tensión. Ante un comportamiento inesperado, detener la sesión y revisar el problema antes de continuar.

**Condición de uso interno:** el programa sigue siendo experimental hasta completar la validación del alcance concreto. La aprobación del mecánico no se sustituye por un indicador verde de la interfaz.

## 18. Qué podría agregarse después

Prioridad propuesta: ampliar la matriz de compatibilidad → comparar sesiones antes/después → reportes más cómodos → cobertura de una marca → recién entonces evaluar funciones de escritura controladas.

Un futuro borrado de fallas debe ser una operación independiente: guardar evidencia previa, mostrar efectos y alcance, exigir confirmación y volver a consultar. Nunca enviar borrado automático al conectar o ante un error. La documentación de Shiny advierte que el borrado también afecta monitores de emisiones. [^S07]

El sistema puede incorporar asistencia para interpretar datos, pero separando hipótesis de resultados comprobados. No usar una IA para inventar comandos, definir direcciones de módulos, decidir reemplazos automáticamente o enviar mensajes arbitrarios al auto.

**Criterio económico:** después del primer piloto, comparar costo de interfaz, horas invertidas, mantenimiento y cobertura lograda con las alternativas existentes. Seguir desarrollando cuando las funciones propias aporten valor; no perseguir un clon completo de un equipo profesional solo por haber conseguido leer las RPM.

## 19. Primer encargo para Codex — Android + Windows

Guardar este archivo como `docs/TALLERSCAN_PLAN.md`. Esta versión reemplaza la planificación WPF anterior. Implementar etapas pequeñas y comprobar cada una.

```text
Leé docs/TALLERSCAN_PLAN.md y comenzá TallerScan, una app de diagnóstico
OBD2 de solo lectura para un taller mecánico en Argentina.

REQUISITO OBLIGATORIO:
La aplicación debe funcionar en celular/tablet Android y en Windows.
Usá C# + .NET MAUI nativo + XAML. No hagas un proyecto WPF nuevo.
El celular se conectará directamente al adaptador OBD2; no es un control
remoto ni depende de una PC encendida.

Implementá SOLO etapas 0 y 1. No conectes ni envíes comandos a autos.

Arquitectura:
- Core net10.0: dominio y contratos, sin MAUI, Android, WinUI, SQLite o Shiny.
- Application: casos de uso y un único coordinador de sesión.
- Infrastructure: simulación y servicios portables detrás de contratos.
- App MAUI: Android + Windows; UI, permisos y composición por plataforma.
- Tests net10.0: reglas, decodificación, estados y simulación.
- MVVM; VerticalStackLayout/HorizontalStackLayout y CollectionView.
  Sin Grid ni DataGrid. No escribir StackPanel de WPF dentro de MAUI.
- Listas largas con CollectionView como contenido principal y Header/Footer;
  no anidarlas en ScrollView ni darles altura ilimitada.
- Nada de servidores, cuentas externas ni telemetría automática.
- SQLite por instalación será la persistencia de la etapa 4, con sqlite-net-pcl.
  No agregar varios proveedores ni conectar móviles a SQL Server directamente.

Conectividad:
- El dominio usa AdapterEndpoint y AdapterSettings, no PortName obligatorio.
- Tipos previstos: Simulado, BLE, clásico/SPP, serial Windows y Wi-Fi.
- En esta entrega SOLO funciona Simulado. Mostrar el resto como pendientes.
- La primera ruta real propuesta es BLE documentado en Android + Windows.
- No confundir Bluetooth clásico con BLE.
- No agregar System.IO.Ports ni Shiny.Obd.Serial a la ruta Android.
- No inventar UUID GATT ni compatibilidad con todos los ELM327.
- iOS queda documentado como futuro, no implementado ni anunciado compatible.

Tareas:
1. Revisá repositorio y entorno. Conservá el trabajo existente. Si ya hay WPF,
   extraé lógica reutilizable sin borrar pantallas existentes sin explicación.
2. Registrá dotnet --info, workloads disponibles y limitaciones del entorno.
   Fijá SDK/paquetes reales. No uses versiones preview por conveniencia.
3. Creá solución, proyectos, referencias, nullable y configuración de tests.
4. Implementá contratos propios con origen ECU, estado, hora, valor nullable,
   referencia de traza y configuración de transporte discriminada.
5. Implementá catálogo inicial de PIDs con pruebas de fórmulas y máscaras.
6. Implementá simulación determinista con varias ECU, DTC por categoría,
   desconexión, permisos simulados, no-data, timeout y respuestas tardías.
7. Implementá un coordinador que serialice consultas, cancele y descarte
   callbacks de generaciones anteriores de sesión/conexión.
8. Implementá política de solicitudes aprobadas. Bloquear borrado, actuadores,
   programación, comandos libres y cualquier operación no autorizada.
9. Creá pantallas MAUI de simulación: conectar/desconectar, consultar fallas,
   ver sensores con unidad/antigüedad/calidad e iniciar/detener una sesión.
10. Implementá tratamiento de suspensión/reanudación del modo simulado.
    No prometer sondeo en segundo plano. El historial y la simulación deben
    abrir sin permisos Bluetooth ni Internet.
11. Evaluá Shiny.Obd en una prueba de restauración/compilación aislada para
    Android y Windows: API instalada, dependencias, licencia, trazas, origen
    de ECU y posibilidad de interceptar comandos. No inventes métodos.
    No conectes hardware ni conviertas esta prueba en el backend productivo.
12. Documentá decisiones, matriz de compatibilidad y entorno. Todos los
    vehículos y adaptadores reales siguen como NO VALIDADOS.
13. Ejecutá tests y builds por destino. Registrá Debug/Release y resultados.
    Si el entorno no dispone de Windows, Android SDK, workloads o red,
    indicá exactamente qué pruebas no pudiste ejecutar. No afirmes éxito
    en Android o Windows basándote solo en que se generaron archivos.

Entregables:
- Código y tests SOLO de etapas 0 y 1.
- README con comandos verificados, según los TFM realmente configurados.
- docs/DECISIONES.md, docs/COMPATIBILIDAD.md y docs/ENTORNO.md.
- Resumen de resultados: generado / compilado / ejecutado / probado en real.
- Próximo paso acotado para el transporte elegido de la etapa 2.

No implementes fases posteriores sin una nueva instrucción.
```

## 20. Fuentes y alcance de la revisión

Esta revisión del **14 de octubre de 2026** vuelve a comprobar la arquitectura móvil, transportes, permisos, ciclo de vida, almacenamiento, UI y distribución con las fuentes M01–M14 y la ficha NuGet indicada. Conserva las secciones técnicas OBD y las referencias S01–S21 del plan anterior; no representa una nueva validación completa de cada una de esas fuentes.

Las APIs y versiones deben contrastarse contra los paquetes instalados. Este documento no contiene una aplicación compilada: no se realizaron pruebas eléctricas, compilación MAUI/C# ni diagnósticos sobre automóviles. No se adquirieron ni revisaron íntegramente normas de pago.

Base de edición: `TallerScan_Plan_Desarrollo.md`, documento de 752 líneas suministrado en esta conversación. Cambios principales: WPF → MAUI, Android obligatorio, endpoint independiente de COM, BLE/SPP diferenciados, ciclo de vida móvil, SQLite por dispositivo, intercambio explícito y plan de publicación.

Las URL se dejan explícitas para poder conservar y revisar las referencias desde el repositorio. Las secciones de arquitectura, etapas, límites de seguridad y criterios de aceptación son propuestas de diseño para este proyecto, no certificaciones de los fabricantes.

[^S01]: Elm Electronics, **ELM327 Datasheet**, edición ELM327DSJ. Consultadas las secciones de comunicación, comandos, ejemplos de lectura y respuestas con cabeceras/multilínea. Documento técnico histórico, no garantía de conformidad de clones actuales. `https://www.elmelectronics.com/wp-content/uploads/2016/07/ELM327DS.pdf`

[^S02]: OBD Solutions, **Standard OBD Adapters**. Interfaces con comandos ELM327 y puertos COM virtuales. `https://www.obdsol.com/solutions/obd-adapters/standard-obd-adapters/`

[^S03]: OBDLink, **Is my vehicle supported?** Diferencia entre diagnóstico OBD2 y diagnóstico ampliado de fabricante. `https://support.obdlink.com/support/solutions/articles/43000709048-is-my-vehicle-supported-`

[^S04]: OBDLink, **OBD-II Compliant Vehicles**. Tabla de referencias de compatibilidad por mercado; no sustituye comprobación individual. `https://www.obdlink.com/obd-ii-compliant-vehicles/`

[^S05]: Multiecuscan, **Supported Vehicles List**. Matriz de centralitas, vehículos e interfaces. `https://www.multiecuscan.net/SupportedVehiclesList.aspx`

[^S06]: FORScan, artículo del equipo **ELM327-compatible adapters — how to choose**. Referencia técnica sobre interfaces y limitaciones; sus comparaciones históricas no son una cotización actual. `https://forum.forscan.org/viewtopic.php?t=6142`

[^S07]: Shiny.Obd, ficha publicada por sus responsables en NuGet. Versión del núcleo observada: **1.4.0, 12/10/2026**, destino .NET 10, licencia MIT. Incluye notas sobre descubrimiento agregado, DTC y borrado. `https://www.nuget.org/packages/Shiny.Obd/1.4.0`

[^S08]: Shiny.NET, **Commands**. Documentación de lecturas, descubrimiento, DTC, monitores y freeze frame. `https://shinylib.net/obd/commands/`

[^S09]: Shiny.NET, **Serial Transport (USB / UART)**. Transporte, configuración y restricciones por plataforma. `https://shinylib.net/obd/serial/`

[^S10]: Microsoft, **.NET releases, patches, and support**. Estado de soporte LTS. `https://learn.microsoft.com/en-us/dotnet/core/releases-and-support`

[^S11]: Microsoft, **Introduction to the MVVM Toolkit**. `https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/`

[^S12]: Microsoft, **SQLite EF Core provider — limitations**. `https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations`

[^S13]: python-OBD, documentación oficial del proyecto, **Command Tables**. `https://python-obd.readthedocs.io/en/latest/Command%20Tables/`

[^S14]: python-OBD, código fuente de los autores, **obd/decoders.py**. Referencia para contrastar conversiones; no se incorpora su código como dependencia en esta especificación. `https://raw.githubusercontent.com/brendan-w/python-OBD/master/obd/decoders.py`

[^S15]: California Bureau of Automotive Repair, **On-Board Diagnostic Test Reference**, apartado de DTC permanentes. Se utiliza por la explicación técnica, no para aplicar sus reglas de inspección a Argentina. `https://www.bar.ca.gov/obd-test-reference`

[^S16]: Bosch Diagnostics, **Secure Gateway**. Ejemplo de acceso autorizado a funciones protegidas; no implica disponibilidad automática para toda marca o región. `https://boschdiagnostics.com/ads-software/secure-gateway`

[^S17]: SAE International, ficha pública **J1979-2 — E/E Diagnostic Test Modes: OBDonUDS**. Se consultó la descripción pública del alcance, no el texto completo de pago. `https://www.sae.org/standards/j1979-2_202104-e-e-diagnostic-test-modes-obdonuds`

[^S18]: OBD Solutions, **OBDLink Family Reference and Programming Manual**, revisión F. Referencia de comunicación y mensajes de los adaptadores; verificar aplicación al modelo utilizado. `https://www.scantool.net/scantool/downloads/682/obdlink_frpm_f.pdf`

[^S19]: OBDLink, **OBDwiz for Windows**. Referencia para comparar desarrollo propio con software existente. `https://www.obdlink.com/obd-apps/obdwiz-app/`

[^S20]: Innova, **Car repair guides & OBD-II codes**, ejemplo de P0420 y comprobaciones previas al reemplazo. `https://guides.innova.com/`

[^S21]: Shiny.NET, **Adapter Emulator**. Pruebas con un adaptador emulado a través de TCP. `https://shinylib.net/obd/emulator/`


### Fuentes incorporadas para la revisión móvil

[^M01]: Microsoft, **What is .NET MAUI?** C# y XAML, código compartido e integración específica de plataforma. `https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui?view=net-maui-10.0`

[^M02]: Microsoft, **Supported platforms for .NET MAUI apps**. Plataformas y requisito de Mac para desarrollo iOS desde Windows; verificar la vista y SDK elegidos. `https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0`

[^M03]: Shiny.NET, **OBD — Getting Started**. Núcleo y paquetes de transporte separados. La disponibilidad documental no equivale a validación de TallerScan. `https://shinylib.net/obd/`

[^M04]: Shiny.NET, **Serial Transport (USB / UART)**. Diferencia entre serial de escritorio y USB Host de Android. `https://shinylib.net/obd/serial/`

[^M05]: Android Developers, **Connect Bluetooth devices**. BluetoothSocket, RFCOMM, emparejamiento y conexión. `https://developer.android.com/develop/connectivity/bluetooth/connect-bluetooth-devices`

[^M06]: Shiny.NET, **BLE Transport**. Descubrimiento y configuración de servicios/características GATT. `https://shinylib.net/obd/ble/`

[^M07]: Apple, **Core Bluetooth** y referencia histórica **QA1657 — Using External Accessory framework with Bluetooth devices**. No inferir compatibilidad SPP genérica en iOS a partir de compatibilidad Android; contrastar el accesorio y SDK concretos. `https://developer.apple.com/documentation/corebluetooth` ; `https://developer.apple.com/library/archive/qa/qa1657/_index.html`

[^M08]: Android Developers, **Bluetooth permissions**. Permisos según versiones, autorización en ejecución y limitación de neverForLocation. `https://developer.android.com/develop/connectivity/bluetooth/bt-permissions`

[^M09]: Microsoft, **App lifecycle**. Estados de ventana y acciones ante Stopped/Resumed. La política de interrumpir la sesión del MVP es una decisión del proyecto. `https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0`

[^M10]: Microsoft, **Layouts**. Layouts nativos MAUI y diferencias de disposición. `https://learn.microsoft.com/en-us/dotnet/maui/user-interface/layouts/?view=net-maui-10.0`

[^M11]: Microsoft, **.NET MAUI local databases**. sqlite-net-pcl, ruta AppDataDirectory, alternativa Microsoft.Data.Sqlite y consideraciones de copias. `https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0`

[^M12]: Microsoft, **Control scrolling in a CollectionView** y **ScrollView**. Advertencias sobre anidación y medida de listas. `https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/scrolling?view=net-maui-10.0` ; `https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/scrollview?view=net-maui-10.0`

[^M13]: Microsoft, **Publish a .NET MAUI app for Android** y **Publish an Android app using the command line**. APK/AAB y firma. Los requisitos de distribución deben revisarse al publicar. `https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/?view=net-maui-10.0` ; `https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0`

[^M14]: Microsoft, **.NET MAUI support policy**. Ciclo de soporte del workload separado del runtime .NET. `https://dotnet.microsoft.com/en-us/platform/support/policy/maui`
