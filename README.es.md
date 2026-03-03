# SPAN Finder

**Un explorador de archivos Miller Columns ultrarrápido para Windows, diseñado para usuarios avanzados que no aceptan compromisos.**

[English](README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | Español | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder reinventa la navegación de archivos en Windows. Inspirado en la elegancia de la vista por columnas de macOS Finder y potenciado con funciones que Windows Explorer nunca tuvo — múltiples pestañas, vista dividida, operaciones asíncronas y flujos de trabajo con teclado.

> **¿Por qué conformarse con Windows Explorer?**

---

## ¿Por qué SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | No | Sí — navegación jerárquica multicolumna |
| **Multi-pestaña** | Solo Windows 11 (básico) | Pestañas completas con separación, duplicación y restauración |
| **Vista dividida** | No | Panel doble con modos de vista independientes |
| **Panel de vista previa** | Básico | 10+ tipos — imágenes, video, audio, código, Hex, fuentes, PDF |
| **Navegación por teclado** | Limitada | 30+ atajos, búsqueda predictiva, diseño keyboard-first |
| **Renombrado masivo** | No | Regex, prefijo/sufijo, numeración secuencial |
| **Deshacer/Rehacer** | Limitado | Historial completo de operaciones (profundidad configurable) |
| **Temas personalizados** | No | 10 temas — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord y más |
| **Integración Git** | No | Rama, estado, commits de un vistazo |
| **Conexiones remotas** | No | FTP, FTPS, SFTP con credenciales guardadas |
| **Estado de la nube** | Overlay básico | Insignias de sincronización en tiempo real (OneDrive, iCloud, Dropbox) |
| **Velocidad de inicio** | Lento en directorios grandes | Carga asíncrona + cancelación — sin retraso |

---

## Características principales

### Miller Columns — Ver todo de una vez

Navegue jerarquías profundas de carpetas sin perder el contexto. Cada columna representa un nivel — haga clic en una carpeta y su contenido aparece en la siguiente columna.

### Cuatro modos de vista

- **Miller Columns** (Ctrl+1) — Navegación jerárquica
- **Detalles** (Ctrl+2) — Tabla ordenable
- **Lista** (Ctrl+3) — Diseño multicolumna de alta densidad
- **Iconos** (Ctrl+4) — Vista de cuadrícula con 4 opciones de tamaño

### Panel de vista previa — Ver antes de abrir

Presione **Espacio** para Quick Look (estilo macOS Finder):

- Imágenes, video, audio, texto/código, PDF, fuentes, Hex binario, información de carpetas

### Temas y personalización

- **10 Temas**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 niveles de altura de fila** y **6 niveles de tamaño de fuente/icono**
- **9 idiomas**: English, 한국어, 日本語, 中文(简/繁), Deutsch, Español, Français, Português

### Herramientas para desarrolladores

- Insignias de estado Git, visor Hex dump, integración de terminal, conexiones FTP/SFTP

---

## Requisitos del sistema

| | |
|---|---|
| **SO** | Windows 10 versión 1903+ / Windows 11 |
| **Arquitectura** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |

---

## Compilar desde el código fuente

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **Nota**: Las aplicaciones WinUI 3 no se pueden iniciar con `dotnet run`. Use **Visual Studio F5** (requiere empaquetado MSIX).

---

## Contribuir

Consulte [CONTRIBUTING.md](CONTRIBUTING.md).

## Licencia

[GNU General Public License v3.0](LICENSE.md) (con excepción de distribución en Microsoft Store)

El nombre "SPAN Finder" y el logotipo oficial son marcas registradas de LumiBear Studio. Ver [LICENSE.md](LICENSE.md).
