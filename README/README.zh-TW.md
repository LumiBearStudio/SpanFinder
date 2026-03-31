<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>macOS Finder 的 Miller Columns，在 Windows 上重現。</strong><br>
  獻給從 Mac 轉到 Windows、卻無法放棄 Finder 欄位檢視的你。
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="贊助"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/zh-tw%20dark.svg" width="200" alt="從 Microsoft Store 下載"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">中文(简体)</a> | 中文(繁體) | <a href="README.de.md">Deutsch</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.pt.md">Português</a>
</p>

---

![SPAN Finder — Miller Columns 瀏覽](miller-columns.gif)

> **資料夾瀏覽，本來就該這樣。**
> 點擊資料夾，內容隨即展開在旁邊的欄位中。你現在在哪裡、從哪裡來、要去哪裡——一個畫面全部看見。再也不用一直按返回鍵了。

---

## 為什麼選擇 SPAN Finder？

| | Windows 檔案總管 | SPAN Finder |
|---|---|---|
| **Miller Columns** | 無 | 階層式多欄瀏覽 |
| **多分頁** | 僅 Windows 11（基本） | 分頁拖出、複製、工作階段完整還原 |
| **分割檢視** | 無 | 獨立檢視模式的雙窗格 |
| **預覽面板** | 基本 | 10 種以上——圖片、影片、音訊、程式碼、Hex、字型、PDF |
| **鍵盤瀏覽** | 有限 | 30 個以上快捷鍵、自動完成搜尋、鍵盤優先設計 |
| **批次重新命名** | 無 | 正規表示式、前綴/後綴、序號編號 |
| **復原/重做** | 有限 | 完整操作歷史（可設定深度） |
| **自訂主題** | 無 | 10 種主題——Dracula、Tokyo Night、Catppuccin、Gruvbox、Nord 等 |
| **Git 整合** | 無 | 分支、狀態、提交一目了然 |
| **遠端連線** | 無 | FTP、FTPS、SFTP——儲存憑證 |
| **工作區** | 無 | 儲存分頁配置並即時還原 |
| **雲端狀態** | 基本覆蓋層 | 即時同步徽章（OneDrive、iCloud、Dropbox） |
| **啟動速度** | 大型資料夾載入慢 | 非同步載入 + 取消支援——零延遲 |

---

## 功能

### Miller Columns——一覽無遺

瀏覽深層資料夾階層時不會遺失上下文。每一欄代表一個資料夾層級，點擊資料夾後其內容即顯示在下一欄中。隨時都能確認目前位置與路徑。

- 可拖曳的欄位分隔線調整寬度
- 欄位均等化（Ctrl+Shift+=）或依內容調整（Ctrl+Shift+-）
- 作用中欄位始終可見的流暢水平捲動

### 四種檢視模式

- **Miller Columns**（Ctrl+1）——階層式瀏覽，SPAN Finder 的招牌功能
- **詳細資料**（Ctrl+2）——包含名稱、日期、類型、大小欄位的可排序表格
- **清單檢視**（Ctrl+3）——為大量資料夾掃描而設計的高密度多欄版面
- **圖示檢視**（Ctrl+4）——最大 256×256 縮圖的四段大小格狀檢視

![四種檢視模式](view-modes.gif)

### 多分頁 + 完整工作階段還原

- 無限分頁——每個分頁擁有獨立的路徑、檢視模式、瀏覽歷史
- **分頁拖出**：將分頁拖曳至新視窗——狀態完整保留
- **分頁複製**：以完全相同的路徑與設定複製分頁
- 自動儲存工作階段：關閉應用程式後重新開啟——所有分頁原封不動

### 分割檢視——真正的雙窗格

- 左右獨立瀏覽的雙面板檔案瀏覽
- 各面板可使用不同檢視模式（左側 Miller、右側詳細資料）
- 各面板擁有獨立的預覽面板
- 面板間拖曳進行複製/移動操作

![超過 14,000 個項目的分割檢視](2.jpg)

### 預覽面板——開啟前先看

![程式碼預覽 + Git 資訊](5.jpg)

按 **Space** 鍵快速預覽（macOS Finder 風格）：

- **圖片**：JPEG、PNG、GIF、BMP、WebP、TIFF——解析度與中繼資料
- **影片**：MP4、MKV、AVI、MOV、WEBM——播放控制
- **音訊**：MP3、AAC、M4A——演出者、專輯、播放時間資訊
- **文字與程式碼**：30 種以上副檔名——語法醒目提示
- **PDF**：首頁預覽
- **字型**：字符樣本 + 中繼資料
- **Hex 二進位**：為開發者提供的原始位元組檢視
- **資料夾**：大小、項目數、建立日期
- **檔案雜湊**：SHA256 檢查碼顯示 + 一鍵複製（在設定中啟用）

### 鍵盤優先設計

為雙手不離鍵盤的使用者準備的 30 個以上快捷鍵：

| 快捷鍵 | 動作 |
|----------|--------|
| 方向鍵 | 欄位與項目瀏覽 |
| Enter | 開啟資料夾或執行檔案 |
| Space | 切換預覽面板 |
| Ctrl+L / Alt+D | 編輯網址列 |
| Ctrl+F | 搜尋 |
| Ctrl+C / X / V | 複製 / 剪下 / 貼上 |
| Ctrl+Z / Y | 復原 / 重做 |
| Ctrl+Shift+N | 新增資料夾 |
| F2 | 重新命名（多選時批次重新命名） |
| Ctrl+T / W | 新增分頁 / 關閉分頁 |
| Ctrl+1-4 | 切換檢視模式 |
| Ctrl+Shift+S | 儲存工作區 |
| Ctrl+Shift+W | 開啟工作區面板 |
| Ctrl+Shift+E | 切換分割檢視 |
| Delete | 移至資源回收桶 |

### 主題與自訂

![主題與自訂](themes.gif)

- **10 種主題**：Light、Dark、Dracula、Tokyo Night、Catppuccin、Gruvbox、Solarized、Nord、One Dark、Monokai
- **6 級列高**及 **6 級字型/圖示大小**——獨立控制
- **10 種字型**：Segoe UI Variable、Consolas、Cascadia Code/Mono、D2Coding、JetBrains Mono、Fira Code 等——CJK 備用字型鏈
- **3 種圖示包**：Remix Icon、Phosphor Icons、Tabler Icons
- **9 種語言**：中文（繁體）、English、한국어、日本語、中文（简体）、Deutsch、Español、Français、Português

### 開發者工具

![Hex 二進位檢視器](4.jpg)

- **Git 狀態徽章**：各檔案的 Modified、Added、Deleted、Untracked
- **Hex 傾印檢視器**：以十六進位 + ASCII 顯示前 512 位元組
- **終端機整合**：Ctrl+` 在目前路徑開啟終端機
- **遠端連線**：FTP/FTPS/SFTP——加密儲存憑證

### 雲端儲存空間整合

- **同步狀態徽章**：僅雲端、同步完成、等待上傳、同步中
- **OneDrive、iCloud、Dropbox** 自動偵測
- **智慧縮圖**：使用快取預覽——避免不必要的下載

### 智慧搜尋

- **結構化查詢**：`type:image`、`size:>100MB`、`date:today`、`ext:.pdf`
- **自動完成**：在任何欄位中開始輸入即可即時篩選
- **背景處理**：搜尋不會凍結 UI

### 工作區——儲存與還原分頁配置 *(v1.2.1.0)*

- **儲存目前分頁**：右鍵點擊分頁 → 「儲存分頁配置...」或 Ctrl+Shift+S
- **即時還原**：側邊欄工作區按鈕或 Ctrl+Shift+W
- **工作區管理**：在工作區選單中還原、重新命名、刪除
- 最適合切換工作情境——「開發」、「修圖」、「文件整理」

### 進階使用者功能

- **虛擬檔案貼上**：從 RDP 遠端工作階段、Outlook 附件等虛擬檔案來源使用 Ctrl+V 貼上

---

## 效能

為速度而生。已通過每個資料夾超過 14,000 個項目的測試。

- 非同步 I/O——不阻塞 UI 執行緒
- 以最小負擔進行批次屬性更新
- 快速瀏覽時透過防抖選擇避免重複作業
- 分頁快取——即時分頁切換，無需重新渲染
- 透過 SemaphoreSlim 節流進行並行縮圖載入

---

## 系統需求

| | |
|---|---|
| **作業系統** | Windows 10 版本 1903 以上 / Windows 11 |
| **架構** | x64、ARM64 |
| **執行階段** | Windows App SDK 1.8 (.NET 8) |
| **建議** | Windows 11 以獲得 Mica 背景效果 |

---

## 從原始碼建置

```bash
# 先決條件：Visual Studio 2022 + .NET Desktop + WinUI 3 工作負載

# 複製
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# 建置
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# 執行單元測試
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **注意**：WinUI 3 應用程式無法透過 `dotnet run` 啟動。請使用 **Visual Studio F5**（需要 MSIX 封裝）。

---

## 貢獻

發現了 Bug？有功能建議？請[開啟 Issue](https://github.com/LumiBearStudio/SpanFinder/issues)——我們歡迎所有意見回饋。

建置設定、程式碼規範、PR 指南請參閱 [CONTRIBUTING.md](../CONTRIBUTING.md)。

---

## 支持此專案

如果 SPAN Finder 對你有幫助：

- **[在 GitHub 上贊助](https://github.com/sponsors/LumiBearStudio)** ——請我們喝杯咖啡、吃個漢堡或一頓牛排
- **為此儲存庫點 Star**，讓更多人能發現它
- **分享**給懷念 macOS Finder 的朋友
- **回報 Bug**——每一份 Issue 回報都讓 SPAN Finder 更穩定
- **[從 Microsoft Store 下載](https://apps.microsoft.com/detail/9P7NJ351X9TL)** ——Store 評論對曝光率幫助很大

---

## 隱私權與遙測

SPAN Finder 僅將 [Sentry](https://sentry.io) 用於**崩潰報告**，且可以關閉。

- **收集的內容**：例外類型、堆疊追蹤、作業系統版本、應用程式版本
- **不收集的內容**：檔案名稱、資料夾路徑、瀏覽記錄、個人資訊
- **無使用分析、無追蹤、無廣告**
- 崩潰報告中的所有檔案路徑在傳送前會自動清除
- `SendDefaultPii = false`——不收集 IP 位址或使用者識別資訊
- **可關閉**：設定 > 進階 > 「崩潰報告」開關即可完全停用
- 原始碼已公開——可在 [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs) 中自行驗證

詳情請參閱[隱私權政策](../PRIVACY.md)。

---

## 授權條款

本專案依據 [GNU General Public License v3.0](../LICENSE) 授權。

**Microsoft Store 例外**：著作權人（LumiBear Studio）得依據 Microsoft Store 條款散佈官方二進位檔案，該條款不視為 GPL v3 第 7 條之「額外限制」。此例外僅適用於官方散佈，不適用於第三方分支。

**商標**：「SPAN Finder」名稱及官方標誌為 LumiBear Studio 之商標。分支專案須使用不同名稱及標誌。完整商標政策請參閱 [LICENSE.md](../LICENSE.md)。

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">隱私權政策</a> ·
  <a href="../OpenSourceLicenses.md">開放原始碼授權</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">回報 Bug 與功能建議</a>
</p>
