<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>Las Miller Columns de macOS Finder, ahora en Windows.</strong><br>
  Para quienes dejaron macOS pero no pueden renunciar a la vista por columnas de Finder.
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="Sponsor"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Descargar en Microsoft Store"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">中文(简体)</a> | <a href="README.zh-TW.md">中文(繁體)</a> | <a href="README.de.md">Deutsch</a> | Español | <a href="README.fr.md">Français</a> | <a href="README.pt.md">Português</a>
</p>

---

<p align="center">
  <strong>v1.3.1 NUEVO</strong> — <strong>File Shelf</strong>: Estante de arrastrar y soltar estilo macOS <a href="https://www.yoink-app.com/">Yoink</a>. Guarda archivos mientras navegas y suéltalos donde los necesites. (Ctrl+Shift+S)
</p>

---

![SPAN Finder — Navegacion con Miller Columns](miller-columns.gif)

> **Asi es como deberia ser la navegacion de carpetas.**
> Haga clic en una carpeta y su contenido se despliega en la columna siguiente. Donde esta, de donde vino, a donde va — todo visible en una sola pantalla. No mas boton de retroceso.

---

## Por que SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | No disponible | Navegacion jerarquica multicolumna |
| **Multi-pestana** | Solo Windows 11 (basico) | Separacion, re-acoplamiento, duplicacion y restauracion completa de pestanas |
| **Vista dividida** | No disponible | Panel doble con modos de vista independientes |
| **Panel de vista previa** | Basico | 10+ tipos — imagenes, video, audio, codigo, Hex, fuentes, PDF |
| **Navegacion por teclado** | Limitada | 30+ atajos, busqueda con autocompletado, diseno keyboard-first |
| **Renombrado masivo** | No disponible | Regex, prefijo/sufijo, numeracion secuencial |
| **Deshacer/Rehacer** | Limitado | Historial completo de operaciones (profundidad configurable) |
| **Temas personalizados** | No disponible | 10 temas — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord y mas |
| **Integracion Git** | No disponible | Rama, estado y commits de un vistazo |
| **Conexiones remotas** | No disponible | FTP, FTPS, SFTP — con credenciales guardadas |
| **Espacios de trabajo** | No disponible | Guardar y restaurar disposiciones de pestanas |
| **Estado de la nube** | Overlay basico | Insignias de sincronizacion en tiempo real (OneDrive, iCloud, Dropbox) |
| **Velocidad de inicio** | Lento en directorios grandes | Carga asincrona + cancelacion — sin retraso |

---

## Caracteristicas

### Miller Columns — Ver todo de una vez

Navegue jerarquias profundas de carpetas sin perder el contexto. Cada columna representa un nivel de carpeta — haga clic en una carpeta y su contenido aparece en la columna siguiente. Siempre puede ver su ubicacion actual y la ruta completa.

- Separadores de columna arrastrables para ajustar el ancho
- Igualar columnas (Ctrl+Shift+=) o ajustar al contenido (Ctrl+Shift+-)
- Desplazamiento horizontal suave para mantener la columna activa siempre visible

### Cuatro modos de vista

- **Miller Columns** (Ctrl+1) — Navegacion jerarquica, la firma de SPAN Finder
- **Detalles** (Ctrl+2) — Tabla ordenable con columnas de nombre, fecha, tipo y tamano
- **Lista** (Ctrl+3) — Diseno multicolumna de alta densidad para escanear carpetas grandes
- **Iconos** (Ctrl+4) — Vista de cuadricula con miniaturas de hasta 256x256 en 4 niveles de tamano

![Cuatro modos de vista](view-modes.gif)

### Multi-pestana + Restauracion completa de sesion

- Pestanas ilimitadas — cada una con ruta, modo de vista e historial de navegacion independientes
- **Separar y re-acoplar pestanas**: Arrastre una pestana a una nueva ventana para separarla; al arrastrarla de vuelta, una pestana fantasma estilo Chrome y una ventana semitransparente muestran la posicion de acoplamiento — el estado se conserva completamente
- **Duplicar pestanas**: Duplicar con la ruta y configuracion exactas
- Guardado automatico de sesion: cierre y reabra la app — todas las pestanas se mantienen

### Vista dividida — Panel doble real

- Navegacion independiente en paneles izquierdo y derecho
- Modo de vista diferente en cada panel (Miller a la izquierda, Detalles a la derecha)
- Panel de vista previa individual en cada panel
- Arrastrar entre paneles para copiar/mover archivos

![Vista dividida con mas de 14,000 elementos](2.jpg)

### Panel de vista previa — Ver antes de abrir

![Vista previa de codigo + Informacion Git](5.jpg)

Presione **Espacio** para Quick Look (estilo macOS Finder):

- **Navegacion con flechas y Espacio**: Cambie de archivo dentro de Quick Look usando las teclas de flechas y Espacio
- **Persistencia del tamano de Quick Look**: El tamano de la ventana se restaura automaticamente en la siguiente apertura

- **Imagenes**: JPEG, PNG, GIF, BMP, WebP, TIFF — resolucion y metadatos
- **Video**: MP4, MKV, AVI, MOV, WEBM — controles de reproduccion
- **Audio**: MP3, AAC, M4A — artista, album, duracion
- **Texto y codigo**: 30+ extensiones — resaltado de sintaxis
- **PDF**: Vista previa de la primera pagina
- **Fuentes**: Muestra de glifos + metadatos
- **Hex binario**: Vista de bytes sin procesar para desarrolladores
- **Carpetas**: Tamano, numero de elementos, fecha de creacion
- **Hash de archivo**: Suma de verificacion SHA256 + copia con un clic (activar en Configuracion)

### Diseno keyboard-first

Mas de 30 atajos de teclado para quienes no quieren soltar el teclado:

| Atajo | Accion |
|----------|--------|
| Flechas | Navegar por columnas y elementos |
| Enter | Abrir carpeta o ejecutar archivo |
| Espacio | Alternar panel de vista previa |
| Ctrl+L / Alt+D | Editar barra de direcciones |
| Ctrl+F | Buscar |
| Ctrl+C / X / V | Copiar / Cortar / Pegar |
| Ctrl+Z / Y | Deshacer / Rehacer |
| Ctrl+Shift+N | Nueva carpeta |
| F2 | Renombrar (renombrado masivo con seleccion multiple) |
| Ctrl+T / W | Nueva pestana / Cerrar pestana |
| Ctrl+1-4 | Cambiar modo de vista |
| Ctrl+Shift+S | Guardar espacio de trabajo |
| Ctrl+Shift+W | Abrir paleta de espacios de trabajo |
| Ctrl+Shift+E | Alternar vista dividida |
| Delete | Enviar a la papelera de reciclaje |
| Ctrl+Tab / Ctrl+Shift+Tab | Cambiar de pestana (siguiente/anterior) |
| F6 | Cambiar panel en vista dividida |

### Temas y personalizacion

![Temas y personalizacion](themes.gif)

- **10 temas**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 niveles de altura de fila** y **6 niveles de tamano de fuente/icono** — control independiente
- **10 fuentes**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Code y mas — cadena de fuentes de respaldo CJK
- **3 paquetes de iconos**: Remix Icon, Phosphor Icons, Tabler Icons
- **9 idiomas**: English, 한국어, 日本語, 中文(简体/繁體), Deutsch, Español, Français, Português

### Herramientas para desarrolladores

![Visor Hex binario](4.jpg)

- **Insignias de estado Git**: Modified, Added, Deleted, Untracked por archivo
- **Visor de volcado Hex**: Los primeros 512 bytes en hexadecimal + ASCII
- **Integracion de terminal**: Ctrl+` para abrir terminal en la ruta actual
- **Conexiones remotas**: FTP/FTPS/SFTP — credenciales cifradas guardadas

### Almacenamiento en la nube

- **Insignias de estado de sincronizacion**: Solo en la nube, sincronizado, pendiente de subida, sincronizando
- **OneDrive, iCloud, Dropbox** deteccion automatica
- **Miniaturas inteligentes**: Usa vistas previas en cache — evita descargas innecesarias

### Busqueda inteligente

- **Consultas estructuradas**: `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **Autocompletado**: Empiece a escribir en cualquier columna para filtrar instantaneamente
- **Procesamiento en segundo plano**: La busqueda no bloquea la interfaz

### Espacios de trabajo — Guardar y restaurar disposiciones de pestanas *(v1.2.1.0)*

- **Guardar pestanas actuales**: Clic derecho en pestana → "Guardar disposicion de pestanas..." o Ctrl+Shift+S
- **Restauracion instantanea**: Boton de espacios de trabajo en la barra lateral o Ctrl+Shift+W
- **Gestion de espacios de trabajo**: Restaurar, renombrar y eliminar desde el menu de espacios de trabajo
- Ideal para cambiar de contexto — "Desarrollo", "Edicion de fotos", "Organizacion de documentos"

### Funciones avanzadas

- **Pegado de archivos virtuales**: Pegue con Ctrl+V desde sesiones remotas RDP, archivos adjuntos de Outlook y otras fuentes de archivos virtuales

### UX de arrastrar y soltar pestanas *(v1.2.13.0)*

![Separar y re-acoplar pestanas](tab-drag.gif)

- Indicador de pestana fantasma estilo Chrome para visualizar la posicion de acoplamiento
- Retroalimentacion de acoplamiento semitransparente para confirmar la posicion de insercion
- Efectos visuales al separar pestanas (desactivado para pestana unica)
- Pestanas de ancho fijo y estable — sin saltos de diseno durante el arrastre

---

## Rendimiento

Disenado para la velocidad. Probado con mas de 14,000 elementos por carpeta.

- E/S asincrona — no bloquea el hilo de la interfaz
- Actualizaciones de propiedades por lotes con minima sobrecarga
- Seleccion con rebote para evitar trabajo redundante durante la navegacion rapida
- Cache por pestana — cambio instantaneo entre pestanas, sin re-renderizado
- Carga concurrente de miniaturas con limitacion mediante SemaphoreSlim

---

## Requisitos del sistema

| | |
|---|---|
| **SO** | Windows 10 version 1903+ / Windows 11 |
| **Arquitectura** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |
| **Recomendado** | Windows 11 para fondo Mica |

---

## Compilar desde el codigo fuente

```bash
# Requisitos previos: Visual Studio 2022 + .NET Desktop + carga de trabajo WinUI 3

# Clonar
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# Compilar
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Ejecutar pruebas unitarias
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **Nota**: Las aplicaciones WinUI 3 no se pueden iniciar con `dotnet run`. Use **Visual Studio F5** (requiere empaquetado MSIX).

---

## Contribuir

Encontro un error? Tiene una solicitud de funcion? [Abra un issue](https://github.com/LumiBearStudio/SpanFinder/issues) — todo tipo de comentarios son bienvenidos.

Consulte [CONTRIBUTING.md](../CONTRIBUTING.md) para la configuracion de compilacion, convenciones de codigo y guias para Pull Requests.

---

## Apoyar el proyecto

Si SPAN Finder le resulta util:

- **[Patrocinar en GitHub](https://github.com/sponsors/LumiBearStudio)** — invitenos a un cafe, una hamburguesa o un bistec
- **Dele una estrella a este repositorio** para ayudar a que mas personas lo descubran
- **Compartalo** con colegas que extrana macOS Finder
- **Reporte errores** — cada informe hace que SPAN Finder sea mas estable
- **[Descargar en Microsoft Store](https://apps.microsoft.com/detail/9P7NJ351X9TL)** — las resenas en la Store ayudan mucho con la visibilidad

---

## Privacidad y telemetria

SPAN Finder usa [Sentry](https://sentry.io) **solo para informes de errores** — y puede desactivarlo.

- **Lo que recopilamos**: Tipo de excepcion, traza de pila, version del SO, version de la app
- **Lo que NO recopilamos**: Nombres de archivos, rutas de carpetas, historial de navegacion, informacion personal
- **Sin analisis de uso, sin rastreo, sin publicidad**
- Todas las rutas de archivos en los informes de errores se eliminan automaticamente antes del envio
- `SendDefaultPii = false` — no se recopilan direcciones IP ni identificadores de usuario
- **Desactivable**: Configuracion > Avanzado > desactivar "Informes de errores"
- El codigo fuente es abierto — verificalo en [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs)

Mas detalles en la [Politica de Privacidad](../PRIVACY.md).

---

## Licencia

Este proyecto esta licenciado bajo la [GNU General Public License v3.0](../LICENSE).

**Excepcion de Microsoft Store**: El titular de los derechos de autor (LumiBear Studio) puede distribuir binarios oficiales a traves de Microsoft Store bajo sus terminos de servicio, los cuales no se consideran "restricciones adicionales" segun la seccion 7 de GPL v3. Esta excepcion solo aplica a la distribucion oficial y no a forks de terceros.

**Marca registrada**: El nombre "SPAN Finder" y el logotipo oficial son marcas registradas de LumiBear Studio. Los forks deben usar un nombre y logotipo diferentes. Consulte la politica completa de marcas en [LICENSE.md](../LICENSE.md).

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">Politica de Privacidad</a> ·
  <a href="../OpenSourceLicenses.md">Licencias de codigo abierto</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">Reportar errores y solicitar funciones</a>
</p>
