# SPAN Finder

**为Windows打造的极速Miller Columns文件浏览器，专为不妥协的高级用户设计。**

[English](../README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | 中文(简体) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder重新定义Windows上的文件导航。灵感来自macOS Finder的列视图，同时搭载Windows Explorer从未有过的功能——多标签、分屏视图、异步操作和键盘驱动的工作流，让文件管理变得轻松高效。

> **还在用Windows Explorer？是时候升级了。**

---

## 为什么选择SPAN Finder？

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | 无 | 有——层级式多列导航 |
| **多标签** | 仅Windows 11（基础） | 完整标签功能：拖出、复制、会话恢复 |
| **分屏视图** | 无 | 独立视图模式的双窗格 |
| **预览面板** | 基础 | 10+文件类型——图片、视频、音频、代码、Hex、字体、PDF |
| **键盘导航** | 有限 | 30+快捷键、预输入搜索、键盘优先设计 |
| **批量重命名** | 无 | 正则表达式、前缀/后缀、序号编号 |
| **撤销/重做** | 有限 | 完整操作历史（可配置深度） |
| **自定义主题** | 无 | 10种主题——Dracula、Tokyo Night、Catppuccin、Gruvbox、Nord等 |
| **Git集成** | 无 | 分支、状态、提交一目了然 |
| **远程连接** | 无 | FTP、FTPS、SFTP（保存凭据） |
| **云同步状态** | 基础叠加层 | 实时同步徽章（OneDrive、iCloud、Dropbox） |
| **启动速度** | 大目录加载慢 | 异步加载+取消——零延迟 |

---

## 主要功能

### Miller Columns——一览无余

在不丢失上下文的情况下导航深层文件夹层级。每列代表一个层级——点击文件夹，其内容显示在下一列。

### 四种视图模式

- **Miller Columns** (Ctrl+1)——层级导航
- **详细信息** (Ctrl+2)——可排序表格
- **列表** (Ctrl+3)——高密度多列布局
- **图标** (Ctrl+4)——4种尺寸的网格视图

### 预览面板——打开前先看

按**Space**键快速预览（macOS Finder风格）：

- 图片、视频、音频、文本/代码、PDF、字体、Hex二进制、文件夹信息

### 主题与自定义

- **10种主题**: Light、Dark、Dracula、Tokyo Night、Catppuccin、Gruvbox、Solarized、Nord、One Dark、Monokai
- **6级行高** & **6级字体/图标大小**
- **9种语言**: English、한국어、日本語、中文(简/繁)、Deutsch、Español、Français、Português

### 开发者工具

- Git状态徽章、Hex转储查看器、终端集成、FTP/SFTP连接

---

## 系统要求

| | |
|---|---|
| **操作系统** | Windows 10 版本 1903+ / Windows 11 |
| **架构** | x64、ARM64 |
| **运行时** | Windows App SDK 1.8 (.NET 8) |

---

## 从源码构建

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **注意**：WinUI 3应用无法通过`dotnet run`启动。请使用**Visual Studio F5**（需要MSIX打包）。

---

## 贡献

请查看 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

[GNU General Public License v3.0](LICENSE)（含Microsoft Store分发例外）

"SPAN Finder"名称和官方标志是LumiBear Studio的商标。详见 [LICENSE.md](LICENSE.md)。
