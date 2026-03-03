# SPAN Finder

**為Windows打造的極速Miller Columns檔案瀏覽器，專為不妥協的進階使用者設計。**

[English](README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | 中文(繁體) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder重新定義Windows上的檔案導航。靈感來自macOS Finder的欄位檢視，同時搭載Windows Explorer從未有過的功能——多分頁、分割檢視、非同步操作和鍵盤驅動的工作流程，讓檔案管理變得輕鬆高效。

> **還在用Windows Explorer？是時候升級了。**

---

## 為什麼選擇SPAN Finder？

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | 無 | 有——階層式多欄導航 |
| **多分頁** | 僅Windows 11（基本） | 完整分頁功能：拖出、複製、工作階段還原 |
| **分割檢視** | 無 | 獨立檢視模式的雙窗格 |
| **預覽面板** | 基本 | 10+檔案類型——圖片、影片、音訊、程式碼、Hex、字型、PDF |
| **鍵盤導航** | 有限 | 30+快捷鍵、預先輸入搜尋、鍵盤優先設計 |
| **批次重新命名** | 無 | 正規表示式、前綴/後綴、序號編號 |
| **復原/重做** | 有限 | 完整操作歷史（可設定深度） |
| **自訂主題** | 無 | 10種主題——Dracula、Tokyo Night、Catppuccin、Gruvbox、Nord等 |
| **Git整合** | 無 | 分支、狀態、提交一目了然 |
| **遠端連線** | 無 | FTP、FTPS、SFTP（儲存憑證） |
| **雲端同步狀態** | 基本覆蓋層 | 即時同步徽章（OneDrive、iCloud、Dropbox） |
| **啟動速度** | 大目錄載入慢 | 非同步載入+取消——零延遲 |

---

## 主要功能

### Miller Columns——一覽無遺

在不遺失上下文的情況下導航深層資料夾階層。每欄代表一個層級——點擊資料夾，其內容顯示在下一欄。

### 四種檢視模式

- **Miller Columns** (Ctrl+1)——階層導航
- **詳細資料** (Ctrl+2)——可排序表格
- **清單** (Ctrl+3)——高密度多欄版面配置
- **圖示** (Ctrl+4)——4種大小的格狀檢視

### 預覽面板——開啟前先看

按**Space**鍵快速預覽（macOS Finder風格）：

- 圖片、影片、音訊、文字/程式碼、PDF、字型、Hex二進位、資料夾資訊

### 主題與自訂

- **10種主題**: Light、Dark、Dracula、Tokyo Night、Catppuccin、Gruvbox、Solarized、Nord、One Dark、Monokai
- **6級列高** & **6級字型/圖示大小**
- **9種語言**: English、한국어、日本語、中文(简/繁)、Deutsch、Español、Français、Português

### 開發者工具

- Git狀態徽章、Hex傾印檢視器、終端整合、FTP/SFTP連線

---

## 系統需求

| | |
|---|---|
| **作業系統** | Windows 10 版本 1903+ / Windows 11 |
| **架構** | x64、ARM64 |
| **執行階段** | Windows App SDK 1.8 (.NET 8) |

---

## 從原始碼建置

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **注意**：WinUI 3應用程式無法透過`dotnet run`啟動。請使用**Visual Studio F5**（需要MSIX封裝）。

---

## 貢獻

請參閱 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 授權條款

[GNU General Public License v3.0](LICENSE)（含Microsoft Store散佈例外）

「SPAN Finder」名稱和官方標誌是LumiBear Studio的商標。詳見 [LICENSE.md](LICENSE.md)。
