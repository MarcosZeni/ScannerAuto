# Scanner — TallerScan

App de diagnóstico OBD2. .NET 10 + MAUI (Android y Windows), sin servidor: el celular habla directo con el adaptador ELM327. Plan y razones: `../Docs/TallerScan_Plan_Desarrollo_Multiplataforma.md`. Estado y comandos: `README.md`. Decisiones: `docs/DECISIONES.md`.

## Reglas
- **Lectura por defecto; escritura solo por flujos confirmados.** Todo comando al adaptador pasa por `Core/Safety/CommandPolicy` justo antes del transporte. Lo que escribe (borrar fallas, pruebas de actuadores) abre un `CommandScope` con la lista exacta de comandos que necesita y lo cierra al terminar (`Elm327Backend.OpenScope`). Nada de modo 08, seguridad (`27`), programación ni consola libre. Agregar comandos a la lista base se consulta antes.
- **Perfiles de actuadores solo con datos verificados.** Las direcciones y códigos UDS de un modelo real salen de documentación o capturas de ese auto; no se adivinan ni se copian "porque suelen ser así". Hasta que exista un perfil, el auto real queda sin pruebas de actuadores (y lo dice en pantalla).
- **El barrido de módulos no adivina nada por marca.** `Infrastructure/Modules/ModuleScanner` le pregunta a cada dirección CAN con servicios estándar (`19 02`, `18`, `14`, `10 03`/`10 01`) y muestra lo que contesta: dirección, idioma (UDS/KWP), códigos y estado. No pone nombres de módulo ni descripciones que no vengan de la norma (`ModuleAddressBook.Hint` solo dice lo que ISO 15765-4 define). Si un día se agregan direcciones conocidas por marca, van con fuente y licencia, como los perfiles.
- **Nada derivado de bases o software filtrados** (DDT2000, Techstream, Diagbox, escáneres crackeados…) entra en el repo ni en la app: sirven para entender o confirmar, no como fuente. Fuentes abiertas con licencia (PSA-RE, opendbc, OBDb) sí, citadas y con la licencia respetada. Fuentes y plan: `docs/PERFILES - Fuentes y plan.md`.
- Antes de cualquier escritura al auto: guardar evidencia en el historial, pedir confirmación con el efecto explicado, releer después. Todo lo que se le hizo al auto queda en `Actions` del registro y en el informe.
- **Un error nunca es un cero.** Lecturas fallidas: `Value = null` + `ReadStatus`. "NO DATA" no es "sin fallas".
- **Origen por ECU.** Cada respuesta conserva quién respondió (cabecera); no inventar "motor" a partir de 7E8.
- **Un solo dueño del adaptador:** `ElmLink` (un comando a la vez, respuestas hasta `>`, descarte de respuestas tardías). El armado ISO-TP vive solo en `FrameAssembler`.
- `Core` no depende de MAUI ni de plataforma. Lo de Android/Windows va en `App/Platforms/*`.
- Layouts: `VerticalStackLayout`/`HorizontalStackLayout`/`FlexLayout` y `CollectionView` como contenido principal (controles en Header/Footer). **Sin Grid.** Bindings compilados (`x:DataType`) en cada página y template.
- Mínimo de dependencias: hoy solo `Microsoft.Maui.Controls` (y xunit en tests). MVVM propio en `App/Mvvm`.
- Los datos simulados se marcan siempre como SIMULACIÓN en pantalla, historial e informe.
- Identificadores en inglés; comentarios, textos y mensajes en español rioplatense.
- `.bat` en primer plano, con `pause`, sin acentos y con CRLF.
- Antes de cerrar un cambio: `dotnet test tests/TallerScan.Tests` y compilar los dos destinos.

No hay changelog: cada cambio se explica en el mensaje de commit (qué, por qué, cómo volver atrás). Commits solo cuando el usuario lo pide.
