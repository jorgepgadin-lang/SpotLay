# Changelog / Novedades

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
