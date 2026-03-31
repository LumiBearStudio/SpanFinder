<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>macOS Finderのミラーカラム、Windowsで再び。</strong><br>
  Windowsに移行してもFinderのカラムビューが忘れられないあなたのために。
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="Sponsor"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Microsoft Storeからダウンロード"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | 日本語 | <a href="README.zh-CN.md">中文(简体)</a> | <a href="README.zh-TW.md">中文(繁體)</a> | <a href="README.de.md">Deutsch</a> | <a href="README.es.md">Español</a> | <a href="README.fr.md">Français</a> | <a href="README.pt.md">Português</a>
</p>

---

![SPAN Finder — ミラーカラムナビゲーション](miller-columns.gif)

> **フォルダ探索、本来こうあるべきです。**
> フォルダをクリックすると隣のカラムに内容が展開されます。今どこにいるか、どこから来たか、どこへ向かうか — 一画面ですべて確認できます。もう「戻る」ボタンを押す必要はありません。

---

## なぜSPAN Finderなのか？

| | Windowsエクスプローラー | SPAN Finder |
|---|---|---|
| **ミラーカラム** | なし | 階層型マルチカラムナビゲーション |
| **マルチタブ** | Windows 11のみ（基本） | タブ切り離し、複製、セッション復元を完全サポート |
| **分割ビュー** | なし | 独立ビューモードのデュアルパネル |
| **プレビューパネル** | 基本 | 10種以上 — 画像、動画、音声、コード、Hex、フォント、PDF |
| **キーボードナビゲーション** | 限定的 | 30以上のショートカット、オートコンプリート検索、キーボードファースト設計 |
| **一括リネーム** | なし | 正規表現、プレフィックス/サフィックス、連番 |
| **元に戻す/やり直し** | 限定的 | 完全な操作履歴（深度設定可能） |
| **カスタムテーマ** | なし | 10テーマ — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nordなど |
| **Git連携** | なし | ブランチ、ステータス、コミットを一目で確認 |
| **リモート接続** | なし | FTP, FTPS, SFTP — 認証情報の保存 |
| **ワークスペース** | なし | タブレイアウトの保存 & 即座に復元 |
| **クラウド状態** | 基本オーバーレイ | リアルタイム同期バッジ（OneDrive, iCloud, Dropbox） |
| **起動速度** | 大容量フォルダで遅い | 非同期ロード＋キャンセル対応 — 遅延なし |

---

## 機能

### ミラーカラム — すべてを一目で

深いフォルダ階層をナビゲートしてもコンテキストを見失いません。各カラムが一つのフォルダレベルを表し、フォルダをクリックすると次のカラムに内容が表示されます。現在位置と経路を常に確認できます。

- ドラッグ可能なカラム区切り線で幅を調整
- カラム均等化（Ctrl+Shift+=）または内容に合わせる（Ctrl+Shift+-）
- アクティブなカラムが常に見えるようスムーズな水平スクロール

### 4つのビューモード

- **ミラーカラム**（Ctrl+1） — 階層型ナビゲーション、SPAN Finderのシグネチャー
- **詳細ビュー**（Ctrl+2） — 名前、日付、種類、サイズカラムのあるソート可能なテーブル
- **リストビュー**（Ctrl+3） — 大量フォルダスキャンのための高密度マルチカラムレイアウト
- **アイコンビュー**（Ctrl+4） — 最大256×256サムネイルまで4段階サイズのグリッドビュー

![4つのビューモード](view-modes.gif)

### マルチタブ + 完全なセッション復元

- 無制限タブ — 各タブに独立したパス、ビューモード、ナビゲーション履歴
- **タブ切り離し**: タブをドラッグして新しいウィンドウへ — 状態を完全に保持
- **タブ複製**: 正確なパスと設定でタブを複製
- セッション自動保存: アプリを閉じて再度開いても — すべてのタブがそのまま

### 分割ビュー — 真のデュアルパネル

- 独立ナビゲーションが可能な左右ファイルブラウジング
- 各パネルに異なるビューモードを使用可能（左側ミラー、右側詳細）
- 各パネルの個別プレビューパネル
- パネル間ドラッグでコピー/移動操作

![14,000以上のアイテムの分割ビュー](2.jpg)

### プレビューパネル — 開く前に確認

![コードプレビュー + Git情報](5.jpg)

**Space**キーでQuick Look（macOS Finderスタイル）：

- **画像**: JPEG, PNG, GIF, BMP, WebP, TIFF — 解像度およびメタデータ
- **動画**: MP4, MKV, AVI, MOV, WEBM — 再生コントロール
- **音声**: MP3, AAC, M4A — アーティスト、アルバム、再生時間の情報
- **テキスト & コード**: 30以上の拡張子 — シンタックスハイライト
- **PDF**: 最初のページのプレビュー
- **フォント**: グリフサンプル + メタデータ
- **Hexバイナリ**: 開発者のためのバイトビュー
- **フォルダ**: サイズ、アイテム数、作成日
- **ファイルハッシュ**: SHA256チェックサム表示 + ワンクリックコピー（設定で有効化）

### キーボードファースト設計

キーボードから手を離さないユーザーのための30以上のショートカット：

| ショートカット | 動作 |
|----------|--------|
| 矢印キー | カラムおよびアイテムのナビゲーション |
| Enter | フォルダを開くまたはファイルを実行 |
| Space | プレビューパネルのトグル |
| Ctrl+L / Alt+D | アドレスバーの編集 |
| Ctrl+F | 検索 |
| Ctrl+C / X / V | コピー / 切り取り / 貼り付け |
| Ctrl+Z / Y | 元に戻す / やり直し |
| Ctrl+Shift+N | 新規フォルダ |
| F2 | 名前変更（複数選択時は一括変更） |
| Ctrl+T / W | 新規タブ / タブを閉じる |
| Ctrl+1-4 | ビューモード切り替え |
| Ctrl+Shift+S | ワークスペース保存 |
| Ctrl+Shift+W | ワークスペースパレットを開く |
| Ctrl+Shift+E | 分割ビュートグル |
| Delete | ごみ箱に移動 |

### テーマ & カスタマイズ

![テーマ & カスタマイズ](themes.gif)

- **10テーマ**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6段階の行高さ** および **6段階のフォント/アイコンサイズ** — 独立制御
- **10種フォント**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Codeなど — CJK代替フォントチェーン
- **3種アイコンパック**: Remix Icon, Phosphor Icons, Tabler Icons
- **9言語**: 한국어, English, 日本語, 中文(简体/繁體), Deutsch, Español, Français, Português

### 開発者ツール

![Hexバイナリビューア](4.jpg)

- **Gitステータスバッジ**: ファイルごとのModified, Added, Deleted, Untracked
- **Hexダンプビューア**: 最初の512バイトを16進数 + ASCIIで表示
- **ターミナル連携**: Ctrl+`で現在のパスからターミナルを起動
- **リモート接続**: FTP/FTPS/SFTP — 暗号化された認証情報の保存

### クラウドストレージ連携

- **同期状態バッジ**: クラウド専用、同期完了、アップロード待ち、同期中
- **OneDrive, iCloud, Dropbox** を自動検出
- **スマートサムネイル**: キャッシュされたプレビューを使用 — 不要なダウンロードを防止

### スマート検索

- **構造化クエリ**: `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **オートコンプリート**: どのカラムでも入力を始めると即座にフィルタリング
- **バックグラウンド処理**: 検索がUIをブロックしない

### ワークスペース — タブレイアウトの保存 & 復元 *(v1.2.1.0)*

- **現在のタブを保存**: タブ右クリック → 「タブレイアウトを保存...」または Ctrl+Shift+S
- **即座に復元**: サイドバーのワークスペースボタンまたは Ctrl+Shift+W
- **ワークスペース管理**: 復元、名前変更、削除をワークスペースメニューから実行
- 作業コンテキストの切り替えに最適 — 「開発」「写真編集」「ドキュメント整理」

### 上級ユーザー機能

- **仮想ファイル貼り付け**: RDPリモートセッション、Outlook添付ファイルなど仮想ファイルソースからCtrl+Vで貼り付け

---

## パフォーマンス

速度を重視した設計。フォルダあたり14,000以上のアイテムでテスト済み。

- 非同期I/O — UIスレッドをブロックしない
- 最小オーバーヘッドでバッチプロパティ更新
- 高速ナビゲーション時の重複作業を防ぐデバウンス選択
- タブごとのキャッシュ — 即座のタブ切り替え、再レンダリングなし
- SemaphoreSlimスロットリングによる並行サムネイルロード

---

## システム要件

| | |
|---|---|
| **OS** | Windows 10 バージョン 1903以上 / Windows 11 |
| **アーキテクチャ** | x64, ARM64 |
| **ランタイム** | Windows App SDK 1.8 (.NET 8) |
| **推奨** | Mica背景のためWindows 11 |

---

## ソースからビルド

```bash
# 前提条件: Visual Studio 2022 + .NET Desktop + WinUI 3 ワークロード

# クローン
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# ビルド
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# ユニットテスト実行
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **注意**: WinUI 3アプリは`dotnet run`で起動できません。**Visual Studio F5**（MSIXパッケージングが必要）を使用してください。

---

## コントリビューション

バグを見つけましたか？機能のリクエストがありますか？[イシューを作成してください](https://github.com/LumiBearStudio/SpanFinder/issues) — すべてのフィードバックを歓迎します。

ビルド設定、コーディング規約、PRガイドラインについては[CONTRIBUTING.md](../CONTRIBUTING.md)をご参照ください。

---

## プロジェクトを支援する

SPAN Finderが役に立ったなら：

- **[GitHubでスポンサーになる](https://github.com/sponsors/LumiBearStudio)** — コーヒー、ハンバーガー、またはステーキ一食おごってください
- **このリポジトリにStar**を付けてより多くの人に見つけてもらえるようにしてください
- macOS Finderが恋しい同僚に**シェア**してください
- **バグを報告**してください — すべてのイシューレポートがSPAN Finderをより安定させます
- **[Microsoft Storeからダウンロード](https://apps.microsoft.com/detail/9P7NJ351X9TL)** — Storeレビューは露出に大きく貢献します

---

## プライバシー & テレメトリ

SPAN Finderは[Sentry](https://sentry.io)を**クラッシュレポートの目的のみ**に使用しており、オフにできます。

- **収集するもの**: 例外タイプ、スタックトレース、OSバージョン、アプリバージョン
- **収集しないもの**: ファイル名、フォルダパス、閲覧履歴、個人情報
- **使用状況分析、トラッキング、広告は一切なし**
- クラッシュレポート内のすべてのファイルパスは送信前に自動的にスクラブされます
- `SendDefaultPii = false` — IPアドレスやユーザー識別子を収集しません
- **無効化可能**: 設定 > 詳細設定 > 「クラッシュレポート」トグルで完全にオフにできます
- ソースコードは公開されています — [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs)で直接ご確認ください

詳細は[プライバシーポリシー](../PRIVACY.md)をご覧ください。

---

## ライセンス

このプロジェクトは[GNU General Public License v3.0](../LICENSE)の下でライセンスされています。

**Microsoft Store例外**: 著作権者（LumiBear Studio）はMicrosoft Store規約に従って公式バイナリを配布できます。当該規約はGPL v3第7条に基づく「追加的制限」とはみなされません。この例外は公式配布にのみ適用され、サードパーティのフォークには適用されません。

**商標**: 「SPAN Finder」の名称と公式ロゴはLumiBear Studioの商標です。フォークは別の名称とロゴを使用してください。商標ポリシーの全文は[LICENSE.md](../LICENSE.md)をご参照ください。

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">プライバシーポリシー</a> ·
  <a href="../OpenSourceLicenses.md">オープンソースライセンス</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">バグ報告 & 機能リクエスト</a>
</p>
