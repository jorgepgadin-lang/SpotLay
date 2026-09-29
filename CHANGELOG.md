# Changelog / Novedades

## 12.1.6

**English**
- Performance: the FPS and hardware charts are now one telemetry-style view: stacked panels on the same timeline, dotted grid, legend on top of each panel and vertical axis titles.
- Hardware chart: two scales, the unit of the first line you tick on the left and a second unit on the right (for example °C and GB). At most two units at a time; other chips are dimmed until you untick one.
- The hardware chart now has the same fade under its main line as the FPS chart, and axes use round numbers (20, 40, 60, 80…).
- Fix: the unit label no longer overlaps the top number of the axis.

**Español**
- Rendimiento: las gráficas de FPS y de hardware son ahora una sola vista estilo telemetría: paneles apilados con el mismo eje de tiempo, rejilla punteada, leyenda arriba de cada panel y títulos de eje en vertical.
- Gráfica de hardware: dos escalas, la unidad de la primera línea que marques a la izquierda y otra unidad a la derecha (por ejemplo °C y GB). Como mucho dos unidades a la vez; los demás chips se atenúan hasta que desmarques uno.
- La gráfica de hardware lleva el mismo difuminado bajo su línea principal que la de FPS, y los ejes usan números redondos (20, 40, 60, 80…).
- Arreglo: la unidad ya no se pisa con el número de arriba del eje.

## 12.1.5

**English**
- Performance: new hardware chart under the FPS chart, on the same timeline. Pick the lines you want (GPU / hot spot / VRAM / CPU temperature, GPU and CPU power, GPU clock, GPU and CPU load, VRAM and RAM); hovering shows that second on both charts. Temperatures, GPU power, clock and load also appear in sessions recorded before this version; the rest only in new sessions.
- Performance: "Fullscreen" button to see both charts full screen (Esc to go back).
- Start with Windows: the startup task is recreated on each new version (older tasks could stop launching SpotLay at sign-in), and running a second copy from another folder no longer takes over the startup task.
- After an update, Windows refreshes SpotLay's icon, so desktop shortcuts no longer turn blank.

**Español**
- Rendimiento: nueva gráfica de hardware debajo de la de FPS, con el mismo eje de tiempo. Eliges qué líneas ver (temperatura de GPU / hot spot / VRAM / CPU, consumo de GPU y CPU, reloj de GPU, uso de GPU y CPU, VRAM y RAM); al pasar el ratón se marca ese segundo en las dos gráficas. Temperaturas, consumo y reloj de GPU y uso de GPU salen también en las partidas grabadas antes de esta versión; lo demás solo en partidas nuevas.
- Rendimiento: botón «Pantalla completa» para ver las dos gráficas en grande (Esc para volver).
- Arrancar con Windows: la tarea de arranque se vuelve a crear en cada versión nueva (las tareas antiguas podían dejar de abrir SpotLay al iniciar sesión), y abrir otra copia desde otra carpeta ya no se queda con el arranque.
- Tras actualizarse, Windows recarga el icono de SpotLay: los accesos directos del escritorio ya no se quedan en blanco.

## 12.1.4

**English**
- Layout hotkeys are now **Ctrl + Shift + 1…5** and **Ctrl + Shift + 0** (back to the game's layout). The old Ctrl + Alt + number is AltGr + number on many keyboards, so it blocked typing @, #, € and others while SpotLay was running. If you kept the default keys they change automatically; keys you chose yourself are not touched.
- If you pick a Ctrl + Alt combination that types a character with AltGr on your keyboard, SpotLay now warns "Clashes with AltGr (@)".
- Exclusive fullscreen: the overlay cannot be drawn over it, and now SpotLay tells you. Home shows a warning while that game is open (use borderless window instead).

**Español**
- Las teclas de los diseños pasan a **Ctrl + Mayús + 1…5** y **Ctrl + Mayús + 0** (volver al diseño del juego). Ctrl + Alt + número es AltGr + número en muchos teclados, así que con SpotLay abierto no dejaba escribir la @, #, € y otros. Si tenías las teclas de fábrica cambian solas; las que elegiste tú no se tocan.
- Si eliges una combinación Ctrl + Alt que escribe un carácter con AltGr en tu teclado, SpotLay avisa «Choca con AltGr (@)».
- Pantalla completa exclusiva: el overlay no se puede dibujar encima, y ahora SpotLay te lo dice. Inicio muestra un aviso mientras ese juego está abierto (usa ventana sin bordes).

## 12.1.3

**English**
- Translation fixes: a few texts on Home and Cooling were still in Spanish with the app in English.

**Español**
- Arreglos de traducción: algunos textos de Inicio y Refrigeración seguían en español con la app en inglés.

## 12.1.2

**English**
- Cooling: always shows the full view (no Essential/Advanced) and a new "Hide headers without a fan" option at the top.
- Calibration goes down to 0 % and waits at 100 % until the fan reaches its real maximum (graphics card fans need several seconds).
- Graphics card fans: when the curve asks for less than the card's minimum (30 % on many NVIDIA cards), SpotLay hands them back to the driver so they can stop at idle (0 RPM).
- Curve edits apply immediately.
- Copy / paste fan settings between fans, and "Paste to all".
- Fixes: temperature lists no longer go blank or keep showing "(no reading)"; the fan control notice is translated.

**Español**
- Refrigeración: siempre en vista completa (sin Esencial/Avanzado) y nueva opción «Ocultar conectores sin ventilador» arriba.
- La calibración baja hasta 0 % y espera al 100 % hasta que el ventilador llega a su máximo real (los de la gráfica tardan varios segundos).
- Ventiladores de la gráfica: cuando la curva pide menos que el mínimo de la tarjeta (30 % en muchas NVIDIA), SpotLay se los devuelve al driver para que puedan pararse en reposo (0 RPM).
- Los cambios en la curva se aplican al momento.
- Copiar / pegar ajustes entre ventiladores, y «Pegar en todos».
- Arreglos: las listas de temperaturas ya no se quedan en blanco ni con «(sin lectura)»; el aviso del control de ventiladores está traducido.

## 12.1.1

**English**
- SpotLay no longer starts PresentMon for apps that are not games (browsers, Claude, Discord, Spotify, VS Code…). It kept relaunching it in the background for no reason.

**Español**
- SpotLay ya no lanza PresentMon con apps que no son juegos (navegadores, Claude, Discord, Spotify, VS Code…). Lo relanzaba una y otra vez en segundo plano sin motivo.

## 12.1.0 — first public release / primera versión pública

**English**
- Overlay with real, generated and total FPS, Frame Generation multiplier and mode (FIXED / DYNAMIC), 1% and 0.1% lows, frametime and any hardware sensor.
- Live overlay editor: drag the overlay, Ctrl + drag to reorder cards, multi-select, copy and paste styles, per-metric colors and units, arcs and rings that always fit their number.
- 5 global layouts with hotkeys and per-game profiles.
- Automatic session recording with FPS, lows, stutters and temperatures.
- Fan control with curves, identify and calibrate.
- Per-sensor alerts and emergency shutdown with cancellable countdown.
- English and Spanish interface.
- Automatic updates from GitHub.

**Español**
- Overlay con FPS reales, generados y totales, multiplicador y modo de Frame Generation (FIJO / DINÁMICO), 1% y 0,1% low, frame time y cualquier sensor del equipo.
- Editor del overlay en directo: arrastrar el overlay, Ctrl + arrastrar para cambiar de sitio las tarjetas, selección múltiple, copiar y pegar estilo, colores y unidad por métrica, arcos y anillos en los que la cifra siempre cabe.
- 5 diseños globales con tecla y perfiles por juego.
- Grabación automática de partidas con FPS, lows, tirones y temperaturas.
- Control de ventiladores con curvas, identificar y calibrar.
- Avisos por sensor y apagado de emergencia con cuenta atrás cancelable.
- Interfaz en español e inglés.
- Actualizaciones automáticas desde GitHub.
