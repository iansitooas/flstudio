# FL-Lite Studio (Aplicación Nativa de Escritorio para Windows)

Aplicación de producción musical nativa en **C# / .NET 10 (WPF)** con motor de audio DSP en tiempo real sobre **WinMM (`winmm.dll`)**, diseñada para ser clara, fácil de usar y ligera en PCs de bajos recursos, sin navegador ni dependencias web.

## Cómo abrir la aplicación
Ejecuta directamente el archivo compilado:
`bin\Release\net10.0-windows\FLLiteStudio.exe`

O para compilar nuevamente desde código fuente:
```powershell
dotnet build -c Release
```

## Flujo de trabajo en 4 pestañas simples
1. **Ritmos (`F6`)**: Secuenciador por pasos (16/32/64) + **Editor de Instrumento** fijo a la derecha con controles claros para cada sintetizador o sampler.
2. **Melodías (`F7`)**: Piano Roll nativo con teclado izquierdo interactivo, ayuda de escalas, acordes con un clic, notas fantasma, carril de fuerza (Velocity) y herramientas MIDI (Cuantizar, Humanizar, Rasgueo/Strum, Arpegio, Transponer).
3. **Canción (`F5`)**: Arreglador multipista para colocar patrones, importar archivos de audio `.wav` con forma de onda y dibujar curvas de automatización (Filtro Master / Volumen).
4. **Mezcla y Efectos (`F9`)**: Consola Master + 12 pistas Insert con medidores en vivo y Rack de Efectos (hasta 6 slots por pista):
   - Parametric EQ 2 (5 bandas con curva visual)
   - Fruity Reverb 2
   - Fruity Delay 3 (sincronizado al BPM)
   - Soundgoodizer (Perfiles A/B/C/D)
   - Fast Dist / Overdrive (Warm, Tube, Hard Clip, Bitcrusher)
   - Fruity Chorus
   - Gross Beat (Sidechain 1/4, Duck 1/2, Trance Gate, Stutter, Trap Bounce)
   - Fruity Limiter

## Optimización para PC de bajos recursos
- Motor de audio nativo con **cero asignaciones de memoria (Zero-GC)** en el hilo de audio y pool de voces preasignado.
- Activa **Modo Ligero** en la barra superior o en `Rendimiento -> Modo Ligero` para limitar la polifonía a 12 voces y bajar el refresco visual.
- Si notas cortes en una PC muy antigua, cambia el buffer en `Rendimiento -> Buffer 2048`.

## Actualizaciones automáticas mediante GitHub
La app ya incluye un sistema de actualización automática bloqueado en compilación exclusivamente al repositorio oficial **`github.com/iansitooas/flstudio`** (`Core/GitHubUpdater.cs`) y un flujo automático de publicación (`.github/workflows/release.yml`).

### Cómo publicar una actualización
Cada vez que quieras lanzar una actualización automática para todos los usuarios, ejecuta:
```powershell
git tag v1.2.0
git push --tags
```
GitHub Actions compilará automáticamente el proyecto en Windows, empaquetará `FLLiteStudio-win-x64.zip` y publicará el Release. La próxima vez que se abra `FLLiteStudio.exe`, detectará la nueva versión sola, descargará el `.zip`, reemplazará los archivos y se reiniciará actualizada.

## Atajos de teclado
- `Espacio`: Reproducir / Pausar
- `F5` / `F6` / `F7` / `F9`: Cambiar entre Canción, Ritmos, Melodías y Mezcla
- `Ctrl + Z` / `Ctrl + Y`: Deshacer / Rehacer
- `Ctrl + S`: Guardar proyecto (`.fls`)
- `Z–M` y `Q–I`: Tocar notas con el teclado de la PC (`-` y `+` cambian octava)
- `Clic derecho`: Borrar pasos, notas, clips o nodos de automatización
