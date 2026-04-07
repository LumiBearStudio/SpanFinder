<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>macOS Finder 的 Miller Columns，在 Windows 上重新相遇。</strong><br>
  献给那些从 Mac 转到 Windows，却始终放不下 Finder 列视图的你。
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="赞助"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200" alt="从 Microsoft Store 下载"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | 中文(简体) | <a href="README.zh-TW.md">中文(繁體)</a> | <a href="README.de.md">Deutsch</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.pt.md">Português</a>
</p>

---

![SPAN Finder — Miller Columns 文件浏览](miller-columns.gif)

> **文件夹浏览，本该如此。**
> 点击文件夹，内容在旁边的列中展开。你在哪里、从哪里来、要去哪里——一个画面全部呈现。再也不用反复点击"返回"按钮。

---

## 为什么选择 SPAN Finder？

| | Windows 资源管理器 | SPAN Finder |
|---|---|---|
| **Miller Columns** | 无 | 层级式多列导航 |
| **多标签** | 仅 Windows 11（基础） | 标签拖出与重新停靠、复制、会话恢复完整支持 |
| **分屏视图** | 无 | 独立视图模式的双面板 |
| **预览面板** | 基础 | 10 种以上——图片、视频、音频、代码、Hex、字体、PDF |
| **键盘导航** | 有限 | 30+ 快捷键、自动补全搜索、键盘优先设计 |
| **批量重命名** | 无 | 正则表达式、前缀/后缀、序号编号 |
| **撤销/重做** | 有限 | 完整操作历史（可配置深度） |
| **自定义主题** | 无 | 10 种主题——Dracula、Tokyo Night、Catppuccin、Gruvbox、Nord 等 |
| **Git 集成** | 无 | 分支、状态、提交一目了然 |
| **远程连接** | 无 | FTP、FTPS、SFTP——保存凭据 |
| **工作区** | 无 | 保存标签页布局并即时恢复 |
| **云同步状态** | 基础叠加层 | 实时同步徽章（OneDrive、iCloud、Dropbox） |
| **启动速度** | 大文件夹加载缓慢 | 异步加载 + 取消支持——零延迟 |

---

## 功能

### Miller Columns——一览无余

在深层文件夹层级中导航而不丢失上下文。每一列代表一个文件夹层级，点击文件夹即可在下一列中显示其内容。随时可以看到当前位置和完整路径。

- 可拖拽的列分隔线调整宽度
- 列均等化（Ctrl+Shift+=）或适应内容（Ctrl+Shift+-）
- 活动列始终可见的平滑横向滚动

### 四种视图模式

- **Miller Columns**（Ctrl+1）——层级导航，SPAN Finder 的标志性功能
- **详细信息**（Ctrl+2）——带名称、日期、类型、大小列的可排序表格
- **列表视图**（Ctrl+3）——适合大文件夹扫描的高密度多列布局
- **图标视图**（Ctrl+4）——最大 256x256 缩略图的 4 种尺寸网格视图

![四种视图模式](view-modes.gif)

### 多标签 + 完整会话恢复

- 无限标签——每个标签拥有独立的路径、视图模式、导航历史
- **标签拖出与重新停靠**：拖拽标签到新窗口进行拆分，拖回时通过 Chrome 风格的幽灵标签和半透明窗口预览停靠位置——状态完整保留
- **标签复制**：以精确的路径和设置复制标签
- 自动保存会话：关闭应用再次打开——所有标签原样恢复

### 分屏视图——真正的双面板

- 左右独立导航的文件浏览
- 每个面板可使用不同视图模式（左侧 Miller、右侧详细信息）
- 每个面板拥有独立的预览面板
- 面板之间拖拽进行复制/移动操作

![14,000 多个项目的分屏视图](2.jpg)

### 预览面板——打开前先看

![代码预览 + Git 信息](5.jpg)

按 **Space** 键快速预览（macOS Finder 风格）：

- **方向键和空格键导航**：在 Quick Look 窗口中使用方向键和空格键切换文件
- **Quick Look 窗口尺寸记忆**：自动恢复上次使用的窗口大小

- **图片**：JPEG、PNG、GIF、BMP、WebP、TIFF——分辨率及元数据
- **视频**：MP4、MKV、AVI、MOV、WEBM——播放控制
- **音频**：MP3、AAC、M4A——艺术家、专辑、时长信息
- **文本和代码**：30 种以上扩展名——语法高亮显示
- **PDF**：首页预览
- **字体**：字形样本 + 元数据
- **Hex 二进制**：开发者专用的原始字节视图
- **文件夹**：大小、项目数、创建日期
- **文件哈希**：SHA256 校验和显示 + 一键复制（在设置中启用）

### 键盘优先设计

为双手不离键盘的用户打造的 30 多个快捷键：

| 快捷键 | 操作 |
|----------|--------|
| 方向键 | 列和项目导航 |
| Enter | 打开文件夹或运行文件 |
| Space | 切换预览面板 |
| Ctrl+L / Alt+D | 编辑地址栏 |
| Ctrl+F | 搜索 |
| Ctrl+C / X / V | 复制 / 剪切 / 粘贴 |
| Ctrl+Z / Y | 撤销 / 重做 |
| Ctrl+Shift+N | 新建文件夹 |
| F2 | 重命名（多选时批量重命名） |
| Ctrl+T / W | 新建标签 / 关闭标签 |
| Ctrl+1-4 | 切换视图模式 |
| Ctrl+Shift+S | 保存工作区 |
| Ctrl+Shift+W | 打开工作区面板 |
| Ctrl+Shift+E | 切换分屏视图 |
| Delete | 移至回收站 |
| Ctrl+Tab / Ctrl+Shift+Tab | 切换标签页（下一个/上一个） |
| F6 | 切换分屏视图面板 |

### 主题和自定义

![主题和自定义](themes.gif)

- **10 种主题**：Light、Dark、Dracula、Tokyo Night、Catppuccin、Gruvbox、Solarized、Nord、One Dark、Monokai
- **6 级行高**及**6 级字体/图标大小**——独立控制
- **10 种字体**：Segoe UI Variable、Consolas、Cascadia Code/Mono、D2Coding、JetBrains Mono、Fira Code 等——CJK 备用字体链
- **3 种图标包**：Remix Icon、Phosphor Icons、Tabler Icons
- **9 种语言**：中文(简体)、English、한국어、日本語、中文(繁體)、Deutsch、Español、Français、Português

### 开发者工具

![Hex 二进制查看器](4.jpg)

- **Git 状态徽章**：按文件显示 Modified、Added、Deleted、Untracked
- **Hex 转储查看器**：以十六进制 + ASCII 显示前 512 字节
- **终端集成**：Ctrl+` 在当前路径打开终端
- **远程连接**：FTP/FTPS/SFTP——加密存储凭据

### 云存储集成

- **同步状态徽章**：仅云端、同步完成、等待上传、同步中
- **OneDrive、iCloud、Dropbox** 自动检测
- **智能缩略图**：使用缓存预览——避免不必要的下载

### 智能搜索

- **结构化查询**：`type:image`、`size:>100MB`、`date:today`、`ext:.pdf`
- **自动补全**：在任意列中开始输入即可即时过滤
- **后台处理**：搜索不会冻结 UI

### 工作区——保存和恢复标签页布局 *(v1.2.1.0)*

- **保存当前标签**：右键标签 > "保存标签页布局..." 或 Ctrl+Shift+S
- **即时恢复**：侧边栏工作区按钮或 Ctrl+Shift+W
- **工作区管理**：在工作区菜单中恢复、重命名、删除
- 最适合工作场景切换——"开发"、"照片编辑"、"文档整理"

### 高级用户功能

- **虚拟文件粘贴**：从 RDP 远程会话、Outlook 附件等虚拟文件源通过 Ctrl+V 粘贴

### 标签拖放体验 *(v1.2.13.0)*

![标签分离与重新停靠](tab-drag.gif)

- Chrome 风格的幽灵标签指示器，直观显示停靠位置
- 半透明停靠反馈，确认插入位置
- 标签拆分视觉效果（单标签时自动禁用）
- 稳定的固定宽度标签，拖拽过程中布局不会跳动

---

## 性能

为速度而生。已在每个文件夹 14,000 多个项目的条件下通过测试。

- 异步 I/O——不阻塞 UI 线程
- 以最小开销批量更新属性
- 快速导航时防止重复操作的防抖选择
- 按标签缓存——即时标签切换，无需重新渲染
- 通过 SemaphoreSlim 节流实现并发缩略图加载

---

## 系统要求

| | |
|---|---|
| **操作系统** | Windows 10 版本 1903 及以上 / Windows 11 |
| **架构** | x64、ARM64 |
| **运行时** | Windows App SDK 1.8（.NET 8） |
| **推荐** | Windows 11 以获得 Mica 背景效果 |

---

## 从源代码构建

```bash
# 前置条件：Visual Studio 2022 + .NET 桌面开发 + WinUI 3 工作负载

# 克隆
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# 构建
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# 运行单元测试
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **注意**：WinUI 3 应用无法通过 `dotnet run` 启动。请使用 **Visual Studio F5**（需要 MSIX 打包）。

---

## 贡献

发现了 Bug？有功能建议？[请提交 Issue](https://github.com/LumiBearStudio/SpanFinder/issues)——我们欢迎一切反馈。

构建设置、编码规范、PR 指南请参阅 [CONTRIBUTING.md](../CONTRIBUTING.md)。

---

## 支持本项目

如果 SPAN Finder 对你有帮助：

- **[在 GitHub 上赞助](https://github.com/sponsors/LumiBearStudio)**——请我们喝杯咖啡、吃个汉堡或一顿牛排
- **给这个仓库点 Star**，帮助更多人发现它
- **分享**给那些怀念 macOS Finder 的同事
- **提交 Bug 报告**——每一份 Issue 都让 SPAN Finder 更加稳定
- **[从 Microsoft Store 下载](https://apps.microsoft.com/detail/9P7NJ351X9TL)**——Store 评价对曝光度帮助巨大

---

## 隐私和遥测

SPAN Finder 仅将 [Sentry](https://sentry.io) 用于**崩溃报告**，并且可以关闭。

- **收集的内容**：异常类型、堆栈跟踪、操作系统版本、应用版本
- **不收集的内容**：文件名、文件夹路径、浏览记录、个人信息
- **无使用分析、无追踪、无广告**
- 崩溃报告中的所有文件路径在发送前会自动清除
- `SendDefaultPii = false`——不收集 IP 地址或用户标识符
- **可关闭**：设置 > 高级 > "崩溃报告"开关即可完全禁用
- 源代码已公开——可在 [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs) 中自行验证

详情请参阅[隐私政策](../PRIVACY.md)。

---

## 许可证

本项目基于 [GNU General Public License v3.0](../LICENSE) 许可。

**Microsoft Store 例外**：版权所有者（LumiBear Studio）可根据 Microsoft Store 条款分发官方二进制文件，该条款不视为 GPL v3 第 7 条下的"附加限制"。此例外仅适用于官方发行版，不适用于第三方分支。

**商标**："SPAN Finder"名称和官方标志是 LumiBear Studio 的商标。分支项目须使用不同的名称和标志。完整商标政策请参阅 [LICENSE.md](../LICENSE.md)。

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">隐私政策</a> ·
  <a href="../OpenSourceLicenses.md">开源许可证</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">Bug 报告和功能建议</a>
</p>
