using ColorCode;
using ColorCode.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace Span.Views
{
    public sealed partial class PreviewPanelView : UserControl
    {
        private LocalizationService? _loc;
        private DispatcherTimer? _seekTimer;
        private CancellationTokenSource? _highlightCts;
        public PreviewPanelViewModel? ViewModel { get; private set; }

        public PreviewPanelView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                LocalizeUI();
                if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                Helpers.CursorHelper.SetHandCursor(CenterPlayButton);
                CopyHashButton.Click += OnCopyHashClick;
            };
            this.Unloaded += (s, e) =>
            {
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
                _seekTimer?.Stop();
            };
        }

        private static readonly Dictionary<string, ILanguage> _extToLanguage = new(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = Languages.CSharp,
            [".c"] = Languages.Cpp,
            [".cpp"] = Languages.Cpp,
            [".h"] = Languages.Cpp,
            [".hpp"] = Languages.Cpp,
            [".css"] = Languages.Css,
            [".html"] = Languages.Html,
            [".htm"] = Languages.Html,
            [".js"] = Languages.JavaScript,
            [".jsx"] = Languages.JavaScript,
            [".ts"] = Languages.Typescript,
            [".tsx"] = Languages.Typescript,
            [".json"] = Languages.JavaScript,
            [".xml"] = Languages.Xml,
            [".xaml"] = Languages.Xml,
            [".csproj"] = Languages.Xml,
            [".svg"] = Languages.Xml,
            [".sql"] = Languages.Sql,
            [".php"] = Languages.Php,
            [".ps1"] = Languages.PowerShell,
            [".psm1"] = Languages.PowerShell,
            [".java"] = Languages.Java,
            [".vb"] = Languages.VbDotNet,
            [".fs"] = Languages.FSharp,
            [".fsx"] = Languages.FSharp,
            [".md"] = Languages.Markdown,
            [".markdown"] = Languages.Markdown,
        };

        /// <summary>사용자 폰트 (Issue #11). PropertyChangedCallback으로 모든 TextBlock에 직접 설정.</summary>
        public static readonly DependencyProperty UserFontProperty =
            DependencyProperty.Register(nameof(UserFont), typeof(Microsoft.UI.Xaml.Media.FontFamily),
                typeof(PreviewPanelView), new PropertyMetadata(null, OnUserFontChanged));

        public Microsoft.UI.Xaml.Media.FontFamily UserFont
        {
            get => (Microsoft.UI.Xaml.Media.FontFamily)GetValue(UserFontProperty);
            set => SetValue(UserFontProperty, value);
        }

        private static void OnUserFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PreviewPanelView view && e.NewValue is Microsoft.UI.Xaml.Media.FontFamily font)
                view.SetFontOnAllTextBlocks(font);
        }

        private void SetFontOnAllTextBlocks(Microsoft.UI.Xaml.Media.FontFamily font)
        {
            Helpers.DebugLogger.Log($"[PreviewPanel] SetFontOnAllTextBlocks called: {font.Source}");
            ApplyFontDirect(font);

            // 테마 토글 등 deferred 업데이트가 폰트를 덮어쓸 수 있으므로 지연 재적용
            DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                Helpers.DebugLogger.Log($"[PreviewPanel] Deferred check — LabelType font: {LabelType.FontFamily.Source}");
                ApplyFontDirect(font);
            });
        }

        private void ApplyFontDirect(Microsoft.UI.Xaml.Media.FontFamily font)
        {
            // Named TextBlocks — 직접 설정 (Collapsed 상태여도 항상 접근 가능)
            EmptyStateText.FontFamily = font;
            ValFileName.FontFamily = font;
            // Archive
            ValArchiveStats.FontFamily = font;
            ValArchiveRatio.FontFamily = font;
            LabelCompressed.FontFamily = font;
            ValCompressedSize.FontFamily = font;
            LabelOriginal.FontFamily = font;
            ValOriginalSize.FontFamily = font;
            ValArchiveTree.FontFamily = font;
            // Folder
            ValFolderCount.FontFamily = font;
            // Metadata labels
            LabelType.FontFamily = font;
            LabelSize.FontFamily = font;
            LabelCreated.FontFamily = font;
            LabelModified.FontFamily = font;
            LabelResolution.FontFamily = font;
            LabelDuration.FontFamily = font;
            LabelArtist.FontFamily = font;
            LabelAlbum.FontFamily = font;
            // Metadata values
            ValType.FontFamily = font;
            ValSize.FontFamily = font;
            ValCreated.FontFamily = font;
            ValModified.FontFamily = font;
            ValResolution.FontFamily = font;
            ValDuration.FontFamily = font;
            ValArtist.FontFamily = font;
            ValAlbum.FontFamily = font;
            // Git & Hash
            LabelGit.FontFamily = font;
            ValGitInfo.FontFamily = font;
            LabelHash.FontFamily = font;
            HashCalcText.FontFamily = font;
        }

        /// <summary>사용자 폰트를 프리뷰 패널에 적용 (Issue #11).</summary>
        public void ApplyFont(Microsoft.UI.Xaml.Media.FontFamily font)
        {
            if (font is not null) UserFont = font;
        }

        public void Initialize(PreviewPanelViewModel viewModel)
        {
            ViewModel = viewModel;
            RootPanel.DataContext = ViewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PreviewPanelViewModel.TextPreview))
                _ = ApplySyntaxHighlightingAsync();
        }

        // 구문 강조 최대 길이 — 이 이상은 단색 표시 (XAML RichTextBlock Inline 과다 생성 방지)
        private const int MaxHighlightLength = 10000;

        // 백그라운드 regex 사전 검증 타임아웃 (ms)
        private const int RegexProbeTimeoutMs = 800;

        /// <summary>
        /// 백그라운드 스레드에서 ColorCode regex를 사전 실행하여 backtracking 여부를 검증.
        /// UI 스레드 블로킹 없이 안전하게 하이라이팅 가능 여부를 판단한다.
        /// </summary>
        private static async Task<bool> ProbeHighlightSafeAsync(string text, ILanguage language, CancellationToken ct)
        {
            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        // ColorCode의 내부 regex를 직접 실행해 backtracking 검증
                        foreach (var rule in language.Rules)
                        {
                            ct.ThrowIfCancellationRequested();
                            var regex = new Regex(rule.Regex, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline,
                                TimeSpan.FromMilliseconds(RegexProbeTimeoutMs));
                            // 첫 매치만 시도 — backtracking이면 여기서 타임아웃
                            regex.Match(text);
                        }
                        return true;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        Helpers.DebugLogger.Log($"[PreviewPanel] Regex probe timeout for language: {language.Name}");
                        return false;
                    }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task ApplySyntaxHighlightingAsync()
        {
            _highlightCts?.Cancel();
            var cts = new CancellationTokenSource();
            _highlightCts = cts;

            try
            {
                CodePreviewBlock.Blocks.Clear();
                // Blocks.Clear() 후 레이아웃 패스에 시간을 줘야 COMException 방지
                await Task.Yield();
                if (cts.Token.IsCancellationRequested) return;

                CodePreviewScrollViewer.ChangeView(null, 0, null, true);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return;
            }

            var text = ViewModel?.TextPreview;
            if (string.IsNullOrEmpty(text)) return;

            var ext = ViewModel?.TextFileExtension ?? "";
            _extToLanguage.TryGetValue(ext, out var language);

            // 1차 방어: 알려진 backtracking 패턴 즉시 차단 (Issue #36)
            // - Markdown fenced code block
            // - CSS @-rules (@keyframes, @media 등) — 중첩 브레이스가 CSS regex의 exponential backtracking 유발
            if (language == Languages.Markdown && (text.Contains("```") || text.Contains("~~~")))
                language = null;
            if (language == Languages.Css && text.Contains('@'))
                language = null;

            // 2차 방어: 백그라운드에서 regex 사전 검증 — UI 스레드 블로킹 원천 차단
            if (language != null)
            {
                var probeText = text.Length > MaxHighlightLength ? text[..MaxHighlightLength] : text;
                bool safe = await ProbeHighlightSafeAsync(probeText, language, cts.Token);
                if (cts.Token.IsCancellationRequested) return;
                if (!safe)
                {
                    Helpers.DebugLogger.Log($"[PreviewPanel] Regex probe failed for {ext} — fallback to plain text");
                    language = null;
                }
            }

            if (language != null)
            {
                try
                {
                    var theme = ActualTheme == ElementTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                    var formatter = new RichTextBlockFormatter(theme);
                    var highlightText = text.Length > MaxHighlightLength ? text[..MaxHighlightLength] : text;

                    if (text.Length > 5000)
                    {
                        // 먼저 단색으로 즉시 표시 (체감 속도 향상)
                        var tempPara = new Paragraph();
                        tempPara.Inlines.Add(new Run { Text = text });
                        CodePreviewBlock.Blocks.Add(tempPara);

                        await Task.Delay(50, cts.Token);
                        if (cts.Token.IsCancellationRequested) return;

                        CodePreviewBlock.Blocks.Clear();
                        await Task.Yield();
                        if (cts.Token.IsCancellationRequested) return;

                        formatter.FormatRichTextBlock(highlightText, language, CodePreviewBlock);
                        if (text.Length > MaxHighlightLength)
                        {
                            var remainPara = new Paragraph();
                            remainPara.Inlines.Add(new Run { Text = text[MaxHighlightLength..] });
                            CodePreviewBlock.Blocks.Add(remainPara);
                        }
                    }
                    else
                    {
                        if (cts.Token.IsCancellationRequested) return;
                        formatter.FormatRichTextBlock(highlightText, language, CodePreviewBlock);
                    }
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (RegexMatchTimeoutException)
                {
                    // ColorCode regex backtracking 타임아웃 — 단색 텍스트로 폴백
                    try { CodePreviewBlock.Blocks.Clear(); } catch { }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // WinUI 3 RichTextBlock native 렌더링 충돌 — 폴백으로 진행
                    try { CodePreviewBlock.Blocks.Clear(); } catch { }
                }
                catch
                {
                    try { CodePreviewBlock.Blocks.Clear(); } catch { }
                }
            }

            if (cts.Token.IsCancellationRequested) return;

            // 미지원 확장자 또는 폴백: 단색 텍스트
            var para = new Paragraph();
            para.Inlines.Add(new Run { Text = text });
            CodePreviewBlock.Blocks.Add(para);
        }

        public void UpdatePreview(FileSystemViewModel? selectedItem)
        {
            ResetMediaState();
            ViewModel?.OnSelectionChanged(selectedItem);
        }

        public void StopMedia()
        {
            try
            {
                _seekTimer?.Stop();
                if (PreviewMediaPlayer?.MediaPlayer != null)
                {
                    PreviewMediaPlayer.MediaPlayer.Pause();
                    PreviewMediaPlayer.MediaPlayer.Source = null;
                }
                ResetMediaUI();
            }
            catch { }
        }

        private void ResetMediaUI()
        {
            CenterPlayButton.Visibility = Visibility.Visible;
            CenterPlayButton.IsEnabled = true;
            CenterPlayButton.Opacity = 1.0;
            ToolTipService.SetToolTip(CenterPlayButton, null);
            BottomControlBar.Visibility = Visibility.Collapsed;
            SeekSlider.Value = 0;
            TimeLabel.Text = "0:00";
        }

        private void ResetMediaState()
        {
            _seekTimer?.Stop();
            if (PreviewMediaPlayer?.MediaPlayer != null)
            {
                PreviewMediaPlayer.MediaPlayer.Pause();
                PreviewMediaPlayer.MediaPlayer.Source = null;
            }
            ResetMediaUI();
        }

        // ── Play 클릭 → 즉시 재생 UI 전환 ─────────────────────

        private void OnCenterPlayClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var player = PreviewMediaPlayer.MediaPlayer;
                if (player == null) return;

                player.Play();

                // 즉시 UI 전환: 중앙 Play 숨기고 하단 바 표시
                CenterPlayButton.Visibility = Visibility.Collapsed;
                BottomControlBar.Visibility = Visibility.Visible;
                StartSeekTimer();
            }
            catch { }
        }

        // ── Pause 클릭 → 정지 UI 복원 ─────────────────────────

        private void OnBottomPauseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                PreviewMediaPlayer.MediaPlayer?.Pause();
                _seekTimer?.Stop();
                BottomControlBar.Visibility = Visibility.Collapsed;
                CenterPlayButton.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // ── Hash 복사 ────────────────────────────────────────────

        private void OnCopyHashClick(object sender, RoutedEventArgs e)
        {
            var hash = ViewModel?.FileHashText;
            if (string.IsNullOrEmpty(hash)) return;

            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(hash);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            // Visual feedback: change icon to checkmark briefly
            if (CopyHashButton.Content is FontIcon icon)
            {
                var original = icon.Glyph;
                icon.Glyph = "\uE73E"; // Checkmark
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(1.5);
                timer.IsRepeating = false;
                timer.Tick += (s, _) => { icon.Glyph = original; };
                timer.Start();
            }
        }

        // ── 시크 타이머: 진행 표시 + 디코딩 불가 감지 + 재생 완료 ──

        private void StartSeekTimer()
        {
            if (_seekTimer == null)
            {
                _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _seekTimer.Tick += OnSeekTimerTick;
            }
            _seekTimer.Start();
        }

        private void OnSeekTimerTick(object? sender, object e)
        {
            try
            {
                var session = PreviewMediaPlayer?.MediaPlayer?.PlaybackSession;
                if (session == null) { _seekTimer?.Stop(); return; }

                var state = session.PlaybackState;
                var pos = session.Position;
                var dur = session.NaturalDuration;

                // 진행바 업데이트
                if (dur.TotalSeconds > 0)
                {
                    SeekSlider.Value = pos.TotalSeconds / dur.TotalSeconds * 100;
                    TimeLabel.Text = FormatTime(pos);
                }

                // 재생 완료 감지
                if (state == MediaPlaybackState.Paused && dur.TotalSeconds > 0 && pos >= dur)
                {
                    _seekTimer?.Stop();
                    BottomControlBar.Visibility = Visibility.Collapsed;
                    CenterPlayButton.Visibility = Visibility.Visible;
                    SeekSlider.Value = 0;
                    TimeLabel.Text = "0:00";
                }
            }
            catch { }
        }

        private static string FormatTime(TimeSpan ts)
            => ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

        // ── 다국어 ─────────────────────────────────────────────

        private void LocalizeUI()
        {
            if (_loc == null) return;
            EmptyStateText.Text = _loc.Get("Preview_SelectFile");
            LabelType.Text = _loc.Get("Preview_Type");
            LabelSize.Text = _loc.Get("Preview_Size");
            LabelCreated.Text = _loc.Get("Preview_Created");
            LabelModified.Text = _loc.Get("Preview_Modified");
            LabelResolution.Text = _loc.Get("Preview_Resolution");
            LabelDuration.Text = _loc.Get("Preview_Duration");
            LabelArtist.Text = _loc.Get("Preview_Artist");
            LabelAlbum.Text = _loc.Get("Preview_Album");
            LabelGit.Text = _loc.Get("Preview_Git");
            LabelHash.Text = _loc.Get("Preview_Hash") ?? "SHA256";
            HashCalcText.Text = _loc.Get("Preview_HashCalculating") ?? "계산 중...";
            LabelCompressed.Text = _loc.Get("Preview_Compressed");
            LabelOriginal.Text = _loc.Get("Preview_Original");
        }

        public void Cleanup()
        {
            _highlightCts?.Cancel();
            _seekTimer?.Stop();
            StopMedia();
            if (ViewModel != null)
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel?.Dispose();
            ViewModel = null;
            RootPanel.DataContext = null;
        }
    }
}
