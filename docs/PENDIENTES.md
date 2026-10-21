# Pendientes (si se retoma)

Lo que figuraba en la lista de tareas del taller hasta el 20/10/2026, cuando se pausó el proyecto. Al terminar algo, se borra.

- [ ] Elegir y comprar el adaptador del piloto. Para el iPhone del mecánico sirve Wi-Fi o BLE, no Bluetooth clásico. Candidatos vistos: ELM327 Wi-Fi o Bluetooth de la tienda Autech en Mercado Libre (~$10.000, retiro en Florida, Vicente López; no dicen chip ni versión, probable clon). Si resulta un clon que trunca comandos, para el barrido y los actuadores hace falta uno con PIC18F25K80, VLinker MC+ u OBDLink. Ver [COMPATIBILIDAD.md](COMPATIBILIDAD.md).
- [ ] Probar el APK en un celular Android real (`generar-apk.bat`) con el emulador OBD por Wi-Fi (`emulador-obd.bat`, IP de la PC, puerto 35000).
- [ ] Primera prueba con un auto conocido (CAN 11 bits), comparando con un escáner comercial. Cargar el resultado en `COMPATIBILIDAD.md`.
  - Borrado de fallas de motor (modo 04) con el motor apagado.
  - Barrido de módulos (Fallas → "Buscar todos los módulos"): qué módulos aparecen, si el adaptador aceptó el filtro amplio (`ATCF`/`ATCM` en la traza técnica), comparar con lo que lista el escáner comercial.
  - Prueba rápida del adaptador en la traza: respuesta a `ATZ` (versión que declara), `ATCF600` (`OK` o `?`), y si `19022F` a `7E0` contesta en un auto donde `03` sí contesta. Si no, el clon trunca los pedidos de más de 2 bytes.
- [ ] Idea: chequeo automático del adaptador al conectar, que diga en Conexión "adaptador completo" o "adaptador recortado: solo fallas de motor".
- [ ] Barrido de módulos: si aparecen autos con módulos que el barrido no ve (GM con pedidos `2xx`, módulos detrás de puerta de enlace), agregar esas direcciones a `ModuleAddressBook` con la fuente anotada. Tabla de convenciones en `COMPATIBILIDAD.md`.
- [ ] **Perfiles de actuadores** (análisis y plan en [PERFILES - Fuentes y plan.md](PERFILES%20-%20Fuentes%20y%20plan.md)): solo hacen falta para activar actuadores. Decidir con el mecánico qué modelos ve más el taller. Luego, en este orden:
  - [ ] Herramienta de captura: sniffer CAN (CANable o similar, USD 20–40) + cable OBD en Y; SavvyCAN en la PC. Convertidor "captura → perfil".
  - [ ] Peugeot/Citroën primero: importador PSA-RE → perfil (Apache 2.0), seguridad `27` con el algoritmo PSA reimplementado, transporte KWP-PSA2000 sobre CAN-TP. Verificar en un 208/308/C4/Partner 1.6 (ECU `ME745_EC5`) contra un escáner.
  - [ ] Renault/Dacia/Nissan: base DDT2000, importador de comandos → perfiles, verificar en un Clio/Sandero/Kangoo contra ddt4all.
  - [ ] Toyota: direcciones desde opendbc; pruebas de motor por captura con un bidireccional (o Techstream por suscripción).
  - [ ] Fiat moderno, Chevrolet, Ford: captura, un modelo por vez.
  - [ ] VW: evaluar TP2.0 como transporte nuevo; K-Line viejos solo si vale la pena el cable KKL.
  - Regla: nada derivado de software o bases filtradas dentro del repo ni de la app; no instalar escáneres crackeados en ninguna PC (malware).
- [ ] Firma del APK: hoy usa la clave de depuración. Para Play Store hace falta un keystore propio, guardado fuera del repo.
