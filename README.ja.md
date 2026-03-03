# SPAN Finder

**Windows向けの超高速ミラーカラム型ファイルエクスプローラー。妥協を許さないパワーユーザーのために。**

[English](README.md) | [한국어](README.ko.md) | 日本語 | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN FinderはWindows上のファイルナビゲーションを再定義します。macOS Finderのカラムビューの優雅さにインスパイアされ、Windows Explorerにはない機能を搭載 — マルチタブ、分割ビュー、非同期操作、キーボード駆動のワークフローでファイル管理を快適に。

> **Windows Explorerで満足していますか？**

---

## なぜSPAN Finder？

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **ミラーカラム** | なし | あり — 階層型マルチカラムナビゲーション |
| **マルチタブ** | Windows 11のみ（基本） | タブの切り離し、複製、セッション復元 |
| **分割ビュー** | なし | 独立したビューモードのデュアルペイン |
| **プレビューパネル** | 基本 | 10+ファイル形式 — 画像、動画、音声、コード、Hex、フォント、PDF |
| **キーボードナビ** | 限定的 | 30+ショートカット、先行入力検索、キーボードファースト設計 |
| **一括リネーム** | なし | 正規表現、プレフィックス/サフィックス、連番 |
| **元に戻す/やり直し** | 限定的 | 完全な操作履歴（深度設定可能） |
| **カスタムテーマ** | なし | 10テーマ — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nordなど |
| **Git連携** | なし | ブランチ、ステータス、コミットを一目で確認 |
| **リモート接続** | なし | FTP, FTPS, SFTP（保存された認証情報） |
| **クラウド状態** | 基本オーバーレイ | リアルタイム同期バッジ（OneDrive, iCloud, Dropbox） |
| **起動速度** | 大容量ディレクトリで遅い | 非同期ロード＋キャンセル — ラグなし |

---

## 主な機能

### ミラーカラム — すべてを一度に見る

深いフォルダ階層をコンテキストを失わずにナビゲート。各カラムは1つの階層を表し、フォルダをクリックすると次のカラムにその内容が表示されます。

### 4つのビューモード

- **ミラーカラム** (Ctrl+1) — 階層ナビゲーション
- **詳細** (Ctrl+2) — ソート可能なテーブル
- **リスト** (Ctrl+3) — 高密度マルチカラムレイアウト
- **アイコン** (Ctrl+4) — 4サイズオプションのグリッドビュー

### プレビューパネル — 開く前に確認

**Space**キーでクイックルック（macOS Finder方式）：

- 画像、動画、音声、テキスト/コード、PDF、フォント、Hexバイナリ、フォルダ情報

### テーマ & カスタマイズ

- **10テーマ**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6段階の行高さ** & **6段階のフォント/アイコンサイズ**
- **9言語対応**: English, 한국어, 日本語, 中文(简/繁), Deutsch, Español, Français, Português

### 開発者ツール

- Gitステータスバッジ、Hexダンプビューア、ターミナル連携、FTP/SFTP接続

---

## システム要件

| | |
|---|---|
| **OS** | Windows 10 バージョン 1903+ / Windows 11 |
| **アーキテクチャ** | x64, ARM64 |
| **ランタイム** | Windows App SDK 1.8 (.NET 8) |

---

## ビルド

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **注意**: WinUI 3アプリは`dotnet run`で起動できません。**Visual Studio F5**（MSIXパッケージ必須）を使用してください。

---

## コントリビューション

[CONTRIBUTING.md](CONTRIBUTING.md)をご覧ください。

## ライセンス

[GNU General Public License v3.0](LICENSE.md)（Microsoft Store配布例外あり）

「SPAN Finder」の名称とロゴはLumiBear Studioの商標です。詳細は[LICENSE.md](LICENSE.md)をご覧ください。
