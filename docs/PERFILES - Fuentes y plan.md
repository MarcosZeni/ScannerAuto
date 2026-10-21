# Perfiles de actuadores: de dónde salen los códigos y plan para armar la base

Investigado el 20/10/2026. Responde a "¿cómo se consiguen los códigos de cada modelo, están liberados?".

## 1. La verdad corta

- **Leer y borrar fallas de cualquier módulo no necesita perfil** (agregado el 20/10 a la noche): el barrido de módulos (`docs/DECISIONES.md`, "Barrido de módulos sin perfil") descubre las direcciones preguntando y usa servicios estándar (`19 02`, `18`, `14`). Lo que sigue necesitando perfil por modelo es lo de abajo: **activar actuadores**.
- **Las pruebas de actuadores no son OBD2.** OBD2 estandariza lecturas de motor/emisiones y el borrado de fallas de motor (modo 04). Activar el electro, la bomba o un inyector va por el protocolo de diagnóstico de cada fábrica (UDS sobre CAN en los modernos, KWP2000 o protocolos propios en los viejos) con **identificadores que cada marca define y no publica**.
- **No hay ninguna base pública completa.** Ni liberada por los fabricantes ni armada por la comunidad. Lo que existe son tres cosas:
  1. **Bases internas de concesionario que circulan** (Renault DDT2000/CLIP, Toyota Techstream, PSA Diagbox…). Son propiedad de la fábrica; los proyectos abiertos las *leen* pero no las redistribuyen.
  2. **Proyectos abiertos con definiciones reconstruidas** para marcas o ECU concretas (hechas a mano, por ingeniería inversa o extraídas de 1).
  3. **Suscripciones oficiales** (J2534): la fábrica vende su software a talleres, con precios pensados para EE. UU./Europa; en Argentina no hay ley que obligue a darlo.
- **La vía que siempre funciona es capturar**: tomar un escáner que sí haga la prueba en ese auto y grabar lo que manda por el conector. Eso da un perfil verificado para ese modelo, y es legal para uso propio.
- Argentina: hay **proyectos de ley** de derecho a reparar (nacional 2336-D-2022, provincial de Buenos Aires 2018) y un reclamo de FAATRA en 2025, pero **nada sancionado** que obligue a las terminales a dar información de diagnóstico. No contar con eso.

## 2. Qué forma tienen los códigos (para saber qué buscar)

Un pedido de actuador por UDS es un mensaje de pocos bytes a la dirección CAN del módulo. Ejemplo real de PSA (pantalla del telemático, documentado en arduino-psa-diag): `2F D600 03 00` = servicio `2F` (InputOutputControl), identificador `D600`, `03` (forzar), valor `00`. Para volver a dejarlo a la ECU: `2F D600 00`. Las rutinas van con `31 01 RID` (iniciar) y `31 02 RID` (parar). En KWP2000 el equivalente es `30 LID …`.

Es exactamente lo que el Scanner ya ejecuta con un perfil: dirección del módulo + lista de pedidos por botón (`docs/DECISIONES.md`, "Escritura al auto"). Un perfil para un modelo real es una lista de esos bytes, con nombre, precondición y tiempo máximo.

Lo que hace falta por modelo: (a) dirección de pedido/respuesta de cada módulo, (b) cómo se abre la sesión (`10 03`, a veces `10 C0` en PSA), (c) si pide desbloqueo de seguridad (`27`, con algoritmo seed/key propio: hoy **no implementado**), (d) los pedidos de cada prueba.

## 3. Fuentes, por tipo

### A. Proyectos abiertos con definiciones (lo que se puede usar ya)

| Proyecto | Marcas | Qué trae | Formato | Licencia | Cómo se usa desde el Scanner |
|---|---|---|---|---|---|
| [ddt4all](https://github.com/cedricp/ddt4all) | Renault, Dacia, Nissan (y Renault-based) | Lee la base **DDT2000** de Renault: pantallas por ECU con parámetros y botones de comando (actuadores). Pide ELM327 con control de flujo (OBDLink, VLinker, ELM original). | XML (DDT2000) convertido a JSON interno por el programa | Código GPL-3. **La base no viene en el repo**: hay que conseguirla aparte (es de Renault) | Importador DDT→perfil: tomar las ECU de los modelos que ve el taller y convertir los comandos. Primero verificar en un Clio/Sandero/Kangoo contra lo que hace ddt4all. |
| [pyren](https://gitlab.com/py_ren/pyren) | Renault, Dacia | Lo mismo con la base CLIP/DDT2000; modo CLIP | Base CLIP (carpetas EcuRenault/Vehicles) | Abierto; base aparte | Igual que ddt4all; menos cómodo. |
| [toyota-diagnostics](https://github.com/kaikozlov/toyota-diagnostics) | Toyota/Lexus | DID por ECU, DTC y **planes de Active Test** (`2F` directos y rutinas `31`) extraídos de Techstream/GTS por ingeniería inversa. Validado solo en un Camry 2026 de EE. UU.; muchas pruebas marcadas como "ejecutables" sin probar. Corre con panda o J2534, no con ELM327. | Zip/JSON en `toyota_diag/data/` | **Sin licencia declarada**; datos derivados de Techstream | Mirar si los planes incluyen Hilux/Yaris/Corolla/Etios regionales. Si sí: importador + verificación con el auto. Tratar como **pista**, no como verdad. |
| [ddb2cat](https://github.com/brlumen/ddb2cat) | Toyota | Catálogo de ECU, direcciones CAN, parámetros y DTC a partir de **una instalación propia** de Techstream | — | Abierto; base aparte | Si se tiene Techstream (suscripción o instalación), sirve para las direcciones y DID de lectura. |
| [arduino-psa-diag](https://github.com/ludwig-v/arduino-psa-diag) | Peugeot, Citroën, DS, Opel | Lista de ECU con sus pares de ID CAN (`ECU_LIST.md`), sesiones, DTC (`19 02 09` / `17 FF 00`), borrado (`14 FF FF FF`), escritura de configuración, **algoritmo seed/key** ([psa-seedkey-algorithm](https://github.com/ludwig-v)), y pruebas documentadas del telemático/pantalla (`2F D6xx…`, `30 8x…`). **No trae** pruebas de motor (electro, bomba). | Markdown + sketch Arduino | GPL-3 | Direcciones y sesión listas para 208/2008/Partner/C3/C4 Cactus/Berlingo. Las pruebas de motor hay que capturarlas. El seed/key sirve para implementar `27` si hace falta. |
| [marelli-iaw-ecu-studio](https://github.com/emanuelemerlo/marelli-iaw-ecu-studio) | Fiat/Lancia/Alfa de los 90–2000 (Palio 1.0/1.2, Siena 1.4, Punto, Uno con IAW) | Definiciones JSON con **pruebas de actuadores** (bomba, inyectores, bobinas), heredadas de IES2 | JSON en `Definitions/` | GPL-3 (partes BSD) | **No va por ELM327**: protocolo Fiat propio a 1200/7680 baudios por K-Line, cable KKL + adaptador de 3 pines. Si el taller ve muchos Fiat viejos, es un transporte nuevo para el Scanner (serie en Windows), no un perfil. |
| [FordScan / FordDiag](https://github.com/ChrisKayAyling/FordScan) | Ford | Herramienta .NET estilo FORScan: módulos, DTC, datos, codificación. **Sin pruebas de actuadores**: el autor dice que los identificadores de Ford no están publicados en ninguna fuente abierta. Trae una función de **captura**: se pone entre FORScan y el adaptador y decodifica los pedidos UDS. | JSON | GPL-3 | Confirma el diagnóstico: para Ford (Ka, Fiesta, EcoSport, Ranger, Focus) hay que capturar. Su capturador es el modelo de lo que conviene hacer. |
| [OBDb](https://github.com/OBDb) | Casi todas | Definiciones de **lecturas** (modo 22) por marca/modelo | JSON, CC BY-SA 4.0 | Abierta | No es de actuadores, pero sirve para sumar sensores propios de cada marca (temperatura de aceite, presión de turbo…). Para más adelante. |
| [kw1281test](https://github.com/gmenounos/kw1281test) | VW viejos (K-Line, KW1281/KWP2000) | `ActuatorTest`, `BasicSetting`, adaptaciones, EEPROM | — | MIT | En VAG las pruebas de salida son **secuenciales y genéricas del protocolo** (la ECU las recorre sola), no por modelo. Para Gol G3/G4, Polo Classic, Senda con K-Line hace falta cable KKL (serie), no ELM327. |
| [PyVCDS](https://github.com/baconwaifu/PyVCDS), [vwdiag](https://github.com/yonghan/Vaw-diagnosis-), [pq-flasher](https://github.com/pd0wm/pq-flasher) | VW por CAN (TP2.0 + KWP2000; UDS en los nuevos) | Transporte TP2.0, bloques de medida, identificación. **Sin pruebas de salida** documentadas byte a byte. | Python | Abierto | Sirven para implementar TP2.0 (Gol Trend, Fox, Suran, Voyage, Amarok 2010–2022). La prueba de salida secuencial existe en el protocolo (función 03 de VAG 1551, servicio `30` en KWP) pero hay que capturarla una vez para tener los bytes. |
| [gocan/gmlan](https://pkg.go.dev/github.com/roffe/gocan/pkg/gmlan) | GM (GMLAN) | Servicio **DeviceControl `AE`** implementado | Go | Abierto | Chevrolet (Onix, Prisma, Cruze, Tracker, Corsa, Agile, Classic): el servicio es conocido, los paquetes de control por ECU no. Capturar. |
| [OpenVehicleDiag](https://github.com/rnd-ash/openvehiclediag) | Mercedes | Parser de bases CBF/SMR-D a JSON | JSON | Abierto | Poco Mercedes en el taller; referencia de formato. |

### A2. Segunda búsqueda (GitHub, foros, Stack Overflow): lo nuevo

| Hallazgo | Qué es | Valor para nosotros |
|---|---|---|
| [**PSA-RE**](https://github.com/prototux/PSA-RE) (Peugeot, Citroën, DS, Opel sobre plataforma PSA) | **Lo más "liberado" que existe.** Base en YAML con esquema JSON, licencia **Apache 2.0**: 486 modelos de ECU, 11.938 DID de UDS, 4.215 identificadores KWP, **1.472 rutinas y 2.888 controles de entrada/salida (pruebas de actuadores)**, códigos de falla de 363 ECU. Arquitecturas AEE2001 (206, 307 viejo), AEE2004 (207, 308 I, C4 I, C5 II) y AEE2010 (208, 2008, 308 II, 3008 II, C3, C4 Cactus, Partner/Berlingo nuevos). Carpetas `FIAT.full`, `MMC.full` y `TOYOTA.full` para los compartidos con esas marcas. Navegable en [explorer.openleo.org](https://explorer.openleo.org). | **Verificado el 20/10** en el repo: `diag/AEE2010.full/CMM/ME745_EC5/` (Bosch ME7.4.5 del motor EC5 1.6 16v, el de 208/308/C4 Lounge/Partner nacionales) tiene `ecu.yml` (UDS y KWP-PSA2000 por CAN-TP, pedido `6A8`, respuesta `688`, seguridad nivel `83` con algoritmo `PSA_SEED_KEY` y clave `F228`, tester present cada 1000 ms), 16 pruebas de actuadores en `ioctls/*_LID.yml` (inyectores 1–4, calefactores de sondas, etc.; servicio KWP `30 LID …`, con la tabla de resultados "en curso / OK / problema / cortocircuito a positivo / a masa / circuito abierto" y `max_duration_ms`), rutinas y `dtcs.yml`. Otros modelos de motor: `MEVD17_4_2` (1.2 PureTech / 1.6 THP, 25 pruebas), `CMM_EDC_17C81` y `DCM62` (diésel), `EDC16C39`, `ME745`/`ME747`/`ME749` (AEE2004). **Lo que falta:** el byte de "parámetro de control" de cada prueba no está definido (hay que capturarlo o deducirlo una vez por familia) y las pruebas piden **desbloqueo de seguridad** (`27 83` + clave calculada): el algoritmo está publicado en [psa-seedkey-algorithm](https://github.com/ludwig-v/psa-seedkey-algorithm) (código GPL-3: reimplementar a partir de la descripción, no copiar). |
| [opendbc](https://github.com/commaai/opendbc) (comma.ai, MIT) | `opendbc/car/<marca>/values.py` y `fingerprints.py`: direcciones CAN de las ECU, pedidos de sesión (`10 01`/`10 03`), tester present y pedidos de identificación por marca, con respuestas reales de miles de autos (Toyota, VW, Ford, GM, Hyundai/Kia, Honda, Nissan, Chrysler…). Verificado en Toyota: motor `7E0`, híbrido `7E2` (KWP) / `7D2` (UDS), airbag `780`, caja `701`/`7E1`, A/A `7C4`, EPS `7A0/7A1`. | Direcciones y sesión listas para casi todas las marcas: ahorra la mitad de una captura. No trae actuadores. |
| [uds-address](https://github.com/mdabrowski1990/uds-address) (MIT) | Paquete con direcciones de ECU por fabricante/modelo/año, colaborativo; hoy pocos autos (ej. Hyundai i20 2021). | Referencia menor. |
| [DP-Reverser](https://github.com/yulele/DP-Reverser) (USENIX Security 2022) | Herramienta académica que **reconstruye automáticamente** mensajes de actuadores: encontró 124 comandos de control en 10 autos (incluido un Kia). | Los resultados por auto **solo se entregan a investigadores académicos** con formulario. Útil como método, no como base. |
| [OBDCracker](https://github.com/DaSonOfPoseidon/OBDCracker/issues/106) y [telltale](https://github.com/aa22396584/telltale/issues/85) | Otros proyectos abiertos diseñando lo mismo que nosotros (listas blancas de `2F`/`31`, perfiles que no pueden ampliar la política, nada de enumerar DID). | Confirman el enfoque. Sin datos de autos. |
| Reddit r/CarHacking, Stack Overflow | Nada de listas de DID de actuadores por modelo; las respuestas apuntan siempre a "los DID son del fabricante: ODX/CDD o captura del escáner". | Nada nuevo. |
| Brasil (Onix, Gol, Palio, Cronos compartidos) | Buscado en portugués ("teste de atuadores"): **ningún repositorio** con datos de esos modelos. | Para GM y Fiat moderno sigue siendo captura. |
| [odxtools](https://github.com/mercedes-benz/odxtools) (Mercedes-Benz, MIT) | Parser oficial de archivos ODX/PDX (el formato estándar ASAM con el que las fábricas describen el diagnóstico de cada ECU). | Si alguna vez llega un ODX legítimo (suscripción, proveedor), se lee con esto. No existe colección pública de ODX. |

### A3. Filtraciones: qué circula, de dónde sale y qué hacemos

Qué hay dando vueltas (foros tipo MHH Auto, Telegram, "obdii365" y similares):

1. **Bases de concesionario**: DDT2000 y CLIP (Renault), Techstream (Toyota), Diagbox/Lexia (PSA), IDS/FDRS (Ford), GDS2/Tech2Win (GM), ODIS/VCDS con sus archivos de etiquetas (VAG). Son el origen real de casi todos los datos "abiertos": ddt4all y pyren las leen; toyota-diagnostics extrajo Techstream con Ghidra; PSA-RE agradece a "la gente que filtró partes de los diseños de PSA".
2. **Software de escáner crackeado**: Launch X431 "Diagzone", Autel, Delphi/Autocom "2017" con keygen. Es lo que más se vende en Argentina como "escáner con todas las marcas".
3. **Algoritmos seed/key** filtrados o recuperados por ingeniería inversa (PSA, VAG, algunos GM): hacen posibles las funciones protegidas.
4. **Documentos internos** (ODX, CDD, manuales de protocolo) sueltos en los mismos foros.

Riesgos, en orden de importancia para el taller:

- **Malware**: el software crackeado de diagnóstico es un vector clásico (ejecutables modificados, "activadores"); además no se actualiza y la marca puede bloquearlo a distancia. Una PC de taller comprometida expone todo (clientes, cuentas, el servidor del sistema de turnos).
- **Legal**: en Argentina la Ley 11.723 protege el software y las bases de datos; usar una copia pirata es infracción, redistribuirla más. No hay ley de derecho a reparar que lo ampare. Riesgo práctico bajo para un taller chico, pero real si TallerScan algún día se distribuye como producto: **nada derivado de una base filtrada puede ir adentro de la app.**
- **Confiabilidad**: una base filtrada no dice para qué versión de ECU vale ni qué fue probado. Un comando equivocado activa otra cosa.

Regla del proyecto (ya está en `CLAUDE.md`):

- Datos filtrados: solo para **entender** un protocolo o **confirmar** una captura, nunca como fuente de un perfil ni dentro del repo. No instalar software crackeado en la PC del taller ni en la de desarrollo.
- Fuentes abiertas con licencia (PSA-RE Apache 2.0, opendbc MIT, OBDb CC BY-SA): se pueden usar y citar, con la licencia respetada. Lo que no tiene licencia (toyota-diagnostics) o es derivado directo de una base de fábrica (DDT2000 en ddt4all) se usa como pista para verificar, y el perfil queda escrito con nuestros propios datos y la fuente anotada.
- Cada perfil real dice en `Source` de dónde salió y con qué auto se verificó.

### B. Bases de fábrica que circulan

DDT2000 (Renault), Techstream (Toyota), Diagbox/Lexia (PSA), Multiecuscan (Fiat, comercial €50), VCDS (VAG, comercial), FORScan (Ford, gratis con licencia), GDS2/Tech2Win (GM). Son el origen de la mayoría de los datos de A. Reglas para el proyecto:

- Usarlas **para verificar y aprender**, no para copiarlas dentro del Scanner ni redistribuirlas: son propiedad de la fábrica. Un perfil del Scanner es nuestra descripción de lo que *nosotros* verificamos en un auto, con la fuente anotada.
- Las versiones "crackeadas" de software de fábrica son ilegales y suelen venir con malware: no.

### C. Suscripciones oficiales (J2534)

Las terminales venden su software de diagnóstico con un pass-thru J2534 (USD 300–2.000 el cable). Referencias de EE. UU.: Ford FDRS/FJDS ~USD 165 (paquete corto), Hyundai USD 60/semana–1.680/año, Kia USD 150/mes, Nissan Consult USD 73/día, Subaru USD 2.900/año. Toyota TIS/Techstream y GM vuelven a vender por suscripción. **No encontré precios ni disponibilidad para Argentina**; muchas están limitadas a vehículos vendidos en su mercado. Puede servir para una marca puntual (Toyota, Ford) y, de paso, para capturar los perfiles con software legítimo.

### D. Captura propia (la vía universal)

Sirve para cualquier marca y deja un perfil **verificado** para ese modelo.

1. Un escáner que haga la prueba en ese auto: un bidireccional económico (Launch/ThinkDiag/Autel básico con licencia de esa marca, USD 100–300) o el software oficial de C.
2. Un sniffer CAN: **CANable** o similar (USD 20–40) + cable OBD en Y, con [SavvyCAN](https://www.csselectronics.com/screen/page/can-bus-interface-savvycan) (gratis, Windows). El ELM327 **no** sirve para esto: su modo monitor pierde tramas.
3. Con el auto estacionado y el motor apagado: grabar, disparar cada prueba desde el escáner (electro baja, alta, apagar…), marcar la hora. Luego, en la grabación, buscar los pedidos a `7E0` (o la dirección del módulo) con `10 03`, `2F …`, `31 …` y las respuestas `6F`/`71`.
4. Cargar el perfil en `perfiles/<marca>-<modelo>.json` con la fuente ("captura Launch X431 el dd/mm en Gol Trend 2014 patente…"), y probar desde el Scanner comparando con el escáner.
5. Caso especial: si el escáner manda `27 01` antes (seed/key), la prueba exige desbloqueo. Sin el algoritmo de esa marca no se puede reproducir; anotarlo y seguir con otro módulo.

Tiempo estimado por modelo: medio día con el auto a mano, una vez que la herramienta de captura está armada.

### E. Lo que no sirve

- Apps tipo Car Scanner/Torque: lecturas y algo de codificación, sin pruebas de actuadores exportables.
- Preguntarle a una IA los códigos de un modelo: no los sabe; inventa.
- Fuzzing (Caring Caribou, mandar `2F` a todos los DID): encuentra qué responde, no qué hace cada cosa, y puede activar cualquier salida. No sobre autos de clientes.

## 4. Parque argentino: vía recomendada por marca

Ranking de patentamientos 2025 (ACARA): Hilux, Yaris, Cronos, 208, Ranger, Amarok, Polo, Corolla Cross, Tracker, Taos. Toyota y VW son las marcas 1 y 2; Fiat tercera. El parque usado agrega Gol/Gol Trend, Palio/Siena/Uno, Corsa/Classic/Agile, Clio/Kangoo/Sandero/Logan, 206/207/307, Fiesta/Ka/EcoSport, Corolla/Etios, C3/C4.

| Marca / modelos | Protocolo de diagnóstico | Fuente de perfil | Esfuerzo | Transporte en el Scanner |
|---|---|---|---|---|
| **Renault** Clio, Sandero, Logan, Kangoo, Duster, Kwid, Captur, Oroch; **Nissan** compartidos | UDS/KWP sobre CAN (ELM con control de flujo) | **A: DDT2000 vía ddt4all** + verificación | Bajo-medio: importador + probar | ELM327 bueno (OBDLink/VLinker) |
| **Toyota** Hilux, Yaris, Corolla, Etios, Corolla Cross, SW4 | UDS sobre CAN | A: toyota-diagnostics (pista) + **D: captura** o **C: Techstream** | Medio | ELM327 bueno |
| **Peugeot/Citroën** 208, 2008, 206/207, 307, 308, Partner, C3, C4, Berlingo | KWP-PSA2000 y UDS sobre CAN-TP con IDs propios (motor `6A8/688`); sesión `10 C0`; **seguridad `27` obligatoria** para actuadores (algoritmo publicado) | **A: PSA-RE** (Apache 2.0: direcciones, sesión, seguridad, lista de pruebas con nombre, resultado y duración) + captura corta para el byte de control | **Bajo-medio: la mejor fuente que hay.** Pide implementar `27` con el algoritmo PSA y el transporte KWP sobre CAN-TP | ELM327 bueno (`ATSH 6A8`, `ATCRA 688`, control de flujo `ATFCSH 6A8`) |
| **VW** Gol Trend, Fox, Suran, Voyage, Amarok 2010–22, Polo/Virtus/T-Cross/Nivus/Taos (nuevos), Gol G3/G4 (viejos) | TP2.0+KWP2000 (medios), UDS (nuevos), KW1281/KWP K-Line (viejos) | Pruebas de salida **genéricas del protocolo** + **D: captura** de una vez por protocolo | Medio-alto: TP2.0 es un transporte nuevo; K-Line pide cable KKL | ELM327 para UDS; TP2.0 por ELM es frágil (mejor OBDLink/J2534); K-Line con KKL (Windows) |
| **Fiat** Cronos, Argo, Toro, Strada, Pulse, Mobi, Palio/Siena/Uno 2010+ | UDS sobre CAN (Marelli/Bosch modernos) | **D: captura** (Multiecuscan es la referencia comercial, €50) | Medio | ELM327 bueno |
| **Fiat** viejos (Palio/Siena/Uno/Punto con IAW 1G7/4AF/59F) | Protocolo Fiat por K-Line | A: marelli-iaw-ecu-studio (definiciones listas) | Alto: transporte serie nuevo | Cable KKL FTDI + adaptador 3 pines (solo Windows) |
| **Chevrolet** Onix, Prisma, Cruze, Tracker, Corsa, Classic, Agile, S10 | GMLAN (DeviceControl `AE`) y UDS en los nuevos | **D: captura** | Medio-alto | ELM327 (11 bits); algunos GM usan 29 bits |
| **Ford** Ka, Fiesta, EcoSport, Focus, Ranger, Territory | UDS sobre CAN (HS/MS: hace falta adaptador con llave HS/MS para algunos módulos) | **D: captura** (FORScan como referencia, su captura lo hace fácil) | Medio | ELM327 con llave HS/MS o OBDLink EX |

## 5. Plan propuesto

1. **Herramienta de captura (una sola vez):** CANable + cable en Y + SavvyCAN; y un pequeño convertidor "captura → perfil" (leer el log de SavvyCAN, filtrar pedidos al módulo, generar el JSON). Vale para todas las marcas.
2. **Formato de perfil en archivos** (hecho en esta versión): `perfiles/*.json`, importables desde la app sin recompilar. Esquema en `perfiles/README.md`.
3. **Peugeot/Citroën primero** (cambio después de la segunda búsqueda): importador **PSA-RE → perfil** (YAML con licencia Apache 2.0, se puede incluir en el repo citando la fuente), implementar seguridad `27` con el algoritmo PSA reimplementado y el transporte KWP-PSA2000 sobre CAN-TP. Verificar en un 208/308/C4/Partner con motor EC5 contra un escáner. Con eso se cubren 208, 2008, 308, C3, C4, Partner y Berlingo de 2010 en adelante, más 207/307/C4 viejos (AEE2004).
4. **Renault segundo:** conseguir la base DDT2000, escribir el importador de comandos para las ECU de los modelos del taller, verificar en un auto contra ddt4all. (Base propiedad de Renault: los perfiles se escriben con nuestros datos verificados, no se copia la base.)
5. **Toyota:** direcciones desde opendbc; pruebas de motor por captura con un bidireccional, o Techstream por suscripción. toyota-diagnostics como pista.
6. **Fiat moderno, Chevrolet y Ford:** captura con bidireccional, un modelo por vez según lo que entre al taller (direcciones de ECU desde opendbc).
7. **VW:** evaluar TP2.0 como transporte nuevo cuando haya un Gol Trend/Amarok a mano; los K-Line viejos solo si vale la pena el cable.
8. **Seguridad `27` (seed/key):** solo con algoritmos publicados (PSA primero) y solo para las pruebas que lo exijan; el perfil declara nivel y algoritmo, nunca una clave arbitraria.

Cada perfil verificado se anota en `COMPATIBILIDAD.md` con auto, fecha, fuente y qué se comparó.

## 6. Fuentes consultadas

- ddt4all: https://github.com/cedricp/ddt4all · base DDT2000 (no incluida): https://github.com/cedricp/ddt4all/discussions/912
- pyren: https://gitlab.com/py_ren/pyren
- toyota-diagnostics: https://github.com/kaikozlov/toyota-diagnostics · ddb2cat: https://github.com/brlumen/ddb2cat
- arduino-psa-diag: https://github.com/ludwig-v/arduino-psa-diag (ECU_LIST.md, actuators/TELEMAT.md) · seed/key: https://github.com/ludwig-v/psa-seedkey-algorithm
- PSA-RE: https://github.com/prototux/PSA-RE (`diag/AEE2010.full/CMM/ME745_EC5/ecu.yml`, `ioctls/11_LID.yml`, `schemas/dbmuxev.schema.json`) · explorador: https://explorer.openleo.org
- opendbc: https://github.com/commaai/opendbc (`opendbc/car/toyota/values.py`) · uds-address: https://github.com/mdabrowski1990/uds-address
- DP-Reverser (USENIX 2022): https://github.com/yulele/DP-Reverser · paper: https://www.usenix.org/system/files/sec22-yu-le.pdf
- odxtools: https://github.com/mercedes-benz/odxtools · OBDCracker (diseño de 0x2F): https://github.com/DaSonOfPoseidon/OBDCracker/issues/106
- Riesgo de herramientas pirateadas: https://www.vehicleservicepros.com/home/article/21193092/is-your-auto-repair-shop-at-risk-for-cyberattacks · https://www.truckpartsandservice.com/maintenance/maintenance/article/15739200/as-diagnostic-software-price-tags-increase-so-do-scams
- marelli-iaw-ecu-studio: https://github.com/emanuelemerlo/marelli-iaw-ecu-studio
- FordScan/FordDiag: https://github.com/ChrisKayAyling/FordScan
- OBDb: https://github.com/OBDb
- kw1281test: https://github.com/gmenounos/kw1281test · PyVCDS: https://github.com/baconwaifu/PyVCDS · vwdiag: https://github.com/yonghan/Vaw-diagnosis- · TP2.0: https://jazdw.net/tp20 · Ross-Tech output tests: https://www.ross-tech.com/vag-com/tour/out_test.html
- gocan GMLAN: https://pkg.go.dev/github.com/roffe/gocan/pkg/gmlan
- OpenVehicleDiag: https://github.com/rnd-ash/openvehiclediag
- Caring Caribou: https://github.com/CaringCaribou/caringcaribou · SavvyCAN: https://www.csselectronics.com/screen/page/can-bus-interface-savvycan
- Suscripciones J2534 (EE. UU.): https://aesolutions.us/products/ford-diagnostic-subscription-includes-ids-fdrs-fjds-diagnow · https://www.hyundaitechinfo.com/external/files/HyundaiTechInfo_Service_Information_Subscriptions.pdf · https://kia.oemdtc.com/114/j2534-repragramming-kia/
- Patentamientos 2025: https://www.ambito.com/autos/cuales-fueron-las-marcas-y-los-modelos-mas-vendidos-2025-n6229566 · https://elcerokm.com/patentamientos/2025
- Derecho a reparar en Argentina: proyecto 2336-D-2022 https://www4.hcdn.gob.ar/dependencias/dsecretaria/Periodo2022/PDF2022/TP2022/2336-D-2022.pdf · reclamo FAATRA 2025 https://www.conclusion.com.ar/info-general/mecanicos-independientes-piden-acceso-a-sistemas-de-reparacion-de-marcas-oficiales-no-podemos-competir/07/2025/
