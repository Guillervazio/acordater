# Acordater — Especificación

> *acordate* ("recordá") + *-er*: una herramienta para recordar cosas.

## 1. Problema

En el día a día hacemos muchas cosas en paralelo (compras, pagos, tareas de la casa, mascotas, llamar o escribir a alguien, sacar algo del horno…). Recordarlo todo depende de la memoria de cada uno, y con la sobreinformación eso no escala. Las alarmas y recordatorios tradicionales avisan **una sola vez** y cuesta crearlos, así que en la práctica la persona sigue teniendo que acordarse.

## 2. Propuesta

Una app a la que **le hablás** para decirle qué tenés que recordar y que **insiste activamente** (sonido + voz) hasta que marcás la tarea como hecha. La responsabilidad de recordar pasa de la persona a la herramienta.

## 3. Usuarios y alcance

- Usuarios iniciales: 2 personas (el autor y su esposa), cada uno con su propio teléfono.
- **Sin cuentas, sin servidor, sin sincronización.** Todo se guarda localmente en el dispositivo.
- Dispositivos objetivo: Pixel 8 / 8 Pro con la última versión de Android.
- Distribución: APK instalado manualmente (sideload). Play Store queda fuera de alcance por ahora.
- Idiomas: español (neutro/rioplatense) e inglés.

## 4. Reglas de negocio

### 4.1 Creación y programación

| Lo que dice el usuario | Primer aviso | Repeticiones |
|---|---|---|
| "recordame comprar jabón" (sin tiempo) | ahora + 1 h | cada 1 h |
| "recordame **en 2 horas** limpiar la caja del gato" | ahora + 2 h | cada 1 h |
| "recordame **mañana a las 9** llamar a X" | mañana 09:00 | cada 1 h |
| "recordame **a las 18** regar las plantas" | próximas 18:00 (hoy o mañana) | cada 1 h |

- El intervalo de repetición es siempre **1 hora**. El tiempo que indique el usuario solo afecta al **primer** aviso.
- **Formato de 24 horas.** La app muestra siempre las horas como HH:mm (18:00, nunca 6:00 PM), en ambos idiomas. Al interpretar, la hora dicha se toma literal en 24 h: "a las 9" → 09:00, "a las 3" → 03:00, "a las 21" → 21:00.
- Una hora sin día se refiere a la **próxima** ocurrencia de esa hora: a las 10:00, "a las 9" → mañana 09:00.
- Las expresiones de franja y am/pm sí convierten la hora: "a las 6 de la tarde", "a la tarde a las 6", "at 6 pm" → 18:00.
- Día o franja sin hora: "mañana" → 09:00; "a la mañana" → 09:00; "a la tarde" → 15:00; "a la noche" / "esta noche" → 20:00.
- Cada ítem es un recordatorio independiente (no hay listas en el MVP).

### 4.1.1 Edición

- Tocando un recordatorio pendiente en la lista se puede cambiar su texto y la fecha y hora del próximo aviso.
- Una fecha u hora elegida al editar cuenta como **explícita** (se respeta aunque caiga en el silencio). Si no se toca, se mantiene la que tenía. Si ya pasó, se aplica la regla por defecto (+1 h).
- Las repeticiones siguen siendo cada 1 h a partir de ese aviso. Si el recordatorio estaba sonando, deja de sonar.

### 4.2 Acciones sobre un aviso

- **Hecho ✓**: finaliza el recordatorio; no vuelve a avisar.
- **Posponer**: silencia el aviso actual; el próximo llega 1 h después de pulsarlo.
- Sin respuesta: el próximo aviso llega 1 h después del anterior.
- Insiste **indefinidamente** (respetando el horario de silencio) hasta que se marque como hecho.

### 4.3 Horario de silencio

- Por defecto **22:00 → 08:00**, configurable en la configuración inicial y luego en ajustes.
- El aviso por defecto (+1 h) y las repeticiones que caigan dentro del silencio se mueven al **final del silencio** (08:00 por defecto).
- Un tiempo pedido **explícitamente** ("a las 23", "en 2 horas") se respeta aunque caiga dentro del silencio.
- Varios avisos pendientes al terminar el silencio se entregan juntos, no uno por hora.

### 4.4 Cómo avisa

- Notificación con **sonido** + **lectura en voz alta** (TTS) del texto del recordatorio ("Recuerda: …"). Se lee al empezar a sonar y cada 30 s mientras siga sonando; durante la lectura se pausa el sonido. La voz también usa el canal de alarma.
- Debe sonar **aunque el teléfono esté en silencio** (canal de audio de alarma, como un despertador).
- Presentación tipo alarma (pantalla completa si el teléfono está bloqueado) con los botones **Hecho** y **Posponer**.
- Suena **en bucle hasta que se pulse Hecho o Posponer**, como un despertador. Si suenan varios a la vez, se muestran de a uno; la alarma se calla cuando no queda ninguno sonando.
- Tras reiniciar el teléfono, los recordatorios pendientes se reprograman; los que vencieron con el teléfono apagado avisan al arrancar.

### 4.5 Captura por voz

1. El usuario activa la captura (botón en la app, widget o acceso rápido; más adelante palabra clave).
2. Voz → texto con el reconocimiento de voz **en el dispositivo** (sin conexión), en el idioma del teléfono. Tocar de nuevo el botón termina de escuchar. Si falta el paquete de ese idioma, se pide su descarga y mientras tanto se usa el servicio de reconocimiento por defecto de Android.
3. El intérprete extrae **qué** recordar y **cuándo** (si se dijo): las reglas sin conexión o, si se configuró, un proveedor de IA (sección 5). Mientras espera, el botón muestra "Interpretando…".
4. La app confirma lo que entendió en pantalla y en voz ("Te recuerdo *limpiar la caja del gato* hoy a las 16:30") y permite corregirlo antes de guardar. Tras leerlo, **se guarda solo a los 5 s** si el usuario no toca nada; tocar cualquier campo cancela la cuenta atrás.
5. Si no se reconoce ningún tiempo, todo el texto es la tarea y se aplica la regla por defecto (+1 h).

La creación y edición por texto también debe existir (sirve para corregir y como alternativa a la voz). Por texto, la confirmación se muestra solo en pantalla y se guarda con el botón.

### 4.6 Accesos rápidos

- **Widget** en la pantalla de inicio ("¿Qué te recuerdo?") y **botón en los ajustes rápidos** ("Nuevo recordatorio"). Los dos abren la app escuchando directamente; desde el botón rápido con el teléfono bloqueado, primero se pide desbloquearlo.

## 5. Interpretación del lenguaje

- **Modo sin conexión (MVP):** intérprete basado en reglas para ES/EN. El dominio es acotado (tarea + tiempo relativo o absoluto opcional), así que las reglas lo cubren.
- **Modo IA (opcional), multiproveedor:** para frases más libres. Cada usuario elige su proveedor en Ajustes. Por defecto: **"Sin IA (reglas)"**.
  - Todos los intérpretes implementan la misma interfaz (`IReminderInterpreter`: texto → tarea + primer aviso opcional):
    - `RuleBasedInterpreter`: sin conexión, siempre disponible.
    - `ChatInterpreter`: un único intérprete sobre `IChatClient` (`Microsoft.Extensions.AI`), para cualquier proveedor. Los proveedores solo aportan el cliente:

      | Proveedor | Cliente | Modelo por defecto (barato y rápido) |
      |---|---|---|
      | Claude | SDK oficial `Anthropic` (`AsIChatClient`) | `claude-haiku-4-5` |
      | OpenAI | `Microsoft.Extensions.AI.OpenAI` | `gpt-6-luna` (con razonamiento `none`) |
      | Gemini | el mismo cliente de OpenAI contra el **endpoint compatible con OpenAI** de Gemini | `gemini-3.5-flash-lite` |

  - **Gemini por el endpoint compatible con OpenAI** y no con el SDK `Google.GenAI` (que también implementa `IChatClient`): reutiliza un cliente maduro que ya está en la app, no suma `Google.Apis.Auth` y sus dependencias, y admite el modo JSON.
  - El usuario configura el proveedor, una API key por proveedor (guardada cifrada con `SecureStorage`, nunca escrita en logs) y opcionalmente el modelo. El botón **Probar** interpreta una frase de ejemplo con lo escrito, aunque no esté guardado, y muestra el resultado y el tiempo, o el error.
  - Con un modelo de OpenAI elegido por el usuario no se envía el esfuerzo de razonamiento, porque los modelos sin razonamiento rechazan ese parámetro.
  - Todos usan el mismo prompt: un mensaje de sistema fijo con las reglas de 4.1 (hora literal en 24 h salvo franja o am/pm, próxima ocurrencia, horas por defecto de día y franja) y, en cada frase, la fecha y hora actual, el día de la semana, la zona horaria y el idioma. La respuesta es **JSON estricto**: `{"text": "...", "when": "yyyy-MM-ddTHH:mm" | null}`, en hora local. La app la valida: `text` no vacío y `when` nulo o con ese formato (se tolera JSON envuelto en markdown). Un `when` ya pasado aplica la regla por defecto.
  - **Respaldo:** se usa `RuleBasedInterpreter` si el proveedor falla (API key inválida, modelo inexistente, cuota…), si no hay conexión, si tarda más de **7 s** (el flujo de voz lo espera) o si la respuesta no es válida. No hay reintentos: el timeout acota toda la llamada.
  - La pantalla de confirmación indica discretamente qué intérprete se usó: "Interpretado con: Claude", "Interpretado con: reglas (sin IA)" o "Interpretado con: reglas (respaldo, Claude: no respondió a tiempo)".
  - **Gemini Nano (descartado por ahora):** la API de ML Kit GenAI Prompt / AICore es Kotlin (funciones `suspend` y `Flow`). Usarla desde MAUI exige un módulo Kotlin propio que la envuelva con callbacks y se enlace como AAR, más enlazar sus dependencias (coroutines, AICore), y su lista oficial de dispositivos no garantiza el Pixel 8 Pro (Gemini Nano v1, solo texto). No es viable en el tiempo de esta fase, así que no se muestra en Ajustes. Para retomarlo, el camino es ese módulo Kotlin puente.
  - Las suscripciones de consumo (ChatGPT, Claude Pro/Max/Code) **no** dan acceso a una API y no se pueden usar desde una app propia. Cada proveedor requiere su **API key**, con un coste de céntimos al mes (la capa gratuita de Gemini tiene coste cero).
  - Privacidad: la voz se transcribe en el dispositivo; al proveedor solo llega el texto de la frase.

## 6. Arquitectura

- **.NET 10 + .NET MAUI** (C#). Android primero; Windows después con el mismo proyecto.
- **SQLite + EF Core** para la persistencia local.

```
src/Acordater.Core         Lógica pura, sin dependencias de plataforma:
                           modelo, reglas de programación (4.1–4.3), IReminderInterpreter
                           + intérprete de reglas ES/EN.
                           El tiempo se inyecta (TimeProvider) para poder testear.
tests/Acordater.Core.Tests xUnit sobre reglas e intérprete.
src/Acordater.App          MAUI: UI, persistencia y servicios de plataforma detrás de interfaces
                           (programador de alarmas, notificaciones, voz a texto, texto a voz).
```

Notas técnicas de Android:
- Alarmas exactas con `AlarmManager` (`USE_EXACT_ALARM` / `SCHEDULE_EXACT_ALARM`).
- `POST_NOTIFICATIONS`, `USE_FULL_SCREEN_INTENT`, `RECEIVE_BOOT_COMPLETED` (reprogramar tras reiniciar).
- Canal de notificación con `AudioAttributes.USAGE_ALARM` para sonar en modo silencio.
- Voz a texto: `SpeechRecognizer` en el dispositivo. Texto a voz: `TextToSpeech`.

## 7. Fases

| Fase | Contenido | Resultado verificable | Estado |
|---|---|---|---|
| 0 | Entorno, esqueleto de la solución, tests, CLAUDE.md | `dotnet test` en verde; la app arranca en el Pixel | Hecha |
| 1 | Core: modelo, reglas de programación, horario de silencio, intérprete ES/EN | Tests cubriendo la tabla 4.1 y los casos de silencio | Hecha |
| 2 | App MVP por texto: crear/listar/editar, SQLite, alarmas, notificación con Hecho/Posponer, sonido en silencio, reprogramar tras reiniciar | Recordatorio real que insiste cada hora hasta marcarlo | Hecha |
| 3 | Voz: captura por voz sin conexión, confirmación, lectura en voz alta del aviso | Crear un recordatorio solo hablando | Hecha |
| 4 | Accesos rápidos: widget y botón en ajustes rápidos (4.6) | Capturar sin abrir la app | Hecha |
| 5 | **Spike** de palabra clave ("hey Cordie") con el teléfono bloqueado: Porcupine u openWakeWord + servicio en primer plano; medir batería | Decisión: sí/no y cómo | |
| 6 | Modo IA multiproveedor (Claude, OpenAI, Gemini con API key propia) con respaldo en reglas (sección 5). Gemini Nano descartado por ahora | Frases libres interpretadas correctamente con cada proveedor | Implementada; falta probar en el Pixel |
| 7 | Versión Windows | La app corre en Windows | |
| Futuro | Listas (p. ej. compras como un único recordatorio) | — | |

## 8. Riesgos

- **Palabra clave con el teléfono bloqueado:** Android restringe el micrófono en segundo plano (hace falta un servicio en primer plano con notificación permanente) y el consumo de batería es real. Por eso está aislado en la fase 5 y el MVP no depende de ello.
- **Modo IA:** depende de servicios externos y de sus nombres de modelo, que cambian (los modelos por defecto se pueden cambiar en Ajustes sin recompilar). El respaldo en reglas hace que nunca bloquee la creación de recordatorios.
- **Optimización de batería / Doze:** puede retrasar las alarmas si no son exactas; hay que verificarlo en el dispositivo real.
- **Asperezas de MAUI** en herramientas y en el acceso a algunas APIs nativas (binding de librerías Java como Porcupine).

## 9. Preguntas abiertas

- ¿Se puede ver y reactivar el historial de recordatorios hechos?
