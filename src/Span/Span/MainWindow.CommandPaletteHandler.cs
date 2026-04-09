using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span
{
    /// <summary>
    /// Command Palette (Ctrl+K) 관련 이벤트 핸들러.
    /// </summary>
    public partial class MainWindow
    {
        private bool _isCommandPaletteOpen;
        private List<CommandPaletteItem>? _commandCatalog;

        internal void ToggleCommandPalette()
        {
            if (_isCommandPaletteOpen)
                CloseCommandPalette();
            else
                OpenCommandPalette();
        }

        private void OpenCommandPalette()
        {
            _isCommandPaletteOpen = true;
            CommandPaletteOverlay.Visibility = Visibility.Visible;
            CommandPaletteInput.Text = string.Empty;
            CommandPaletteInput.Focus(FocusState.Programmatic);

            // 카탈로그 빌드 (최초 1회 or 매번 갱신)
            _commandCatalog = BuildCommandCatalog();
            UpdateCommandPaletteResults(string.Empty);
        }

        private void CloseCommandPalette()
        {
            _isCommandPaletteOpen = false;
            CommandPaletteOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnCommandPaletteOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            CloseCommandPalette();
        }

        private void OnCommandPaletteInputTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCommandPaletteResults(CommandPaletteInput.Text);
        }

        private void OnCommandPaletteInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseCommandPalette();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ExecuteSelectedPaletteItem();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                if (CommandPaletteList.Items.Count > 0)
                {
                    var idx = CommandPaletteList.SelectedIndex;
                    CommandPaletteList.SelectedIndex = Math.Min(idx + 1, CommandPaletteList.Items.Count - 1);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Up)
            {
                if (CommandPaletteList.Items.Count > 0)
                {
                    var idx = CommandPaletteList.SelectedIndex;
                    CommandPaletteList.SelectedIndex = Math.Max(idx - 1, 0);
                    CommandPaletteList.ScrollIntoView(CommandPaletteList.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void OnCommandPaletteItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CommandPaletteItem item)
            {
                ExecutePaletteItem(item);
            }
        }

        private void ExecuteSelectedPaletteItem()
        {
            if (CommandPaletteList.SelectedItem is CommandPaletteItem item)
            {
                ExecutePaletteItem(item);
            }
        }

        private void ExecutePaletteItem(CommandPaletteItem item)
        {
            CloseCommandPalette();

            switch (item.Type)
            {
                case CommandPaletteItemType.Command:
                    if (!string.IsNullOrEmpty(item.CommandId))
                    {
                        ExecuteCommand(item.CommandId);
                    }
                    break;

                case CommandPaletteItemType.Tab:
                    if (item.TabIndex >= 0 && item.TabIndex < ViewModel.Tabs.Count)
                    {
                        SwitchToTabByIndex(item.TabIndex);
                    }
                    break;

                case CommandPaletteItemType.Navigation:
                    // Phase 2: folder navigation via FolderItem
                    break;
            }
        }

        private void UpdateCommandPaletteResults(string query)
        {
            if (_commandCatalog == null) return;

            var results = string.IsNullOrWhiteSpace(query)
                ? _commandCatalog.Take(20).ToList()
                : _commandCatalog
                    .Where(c => FuzzyMatch(c.Title, query) || FuzzyMatch(c.Category, query))
                    .OrderByDescending(c => ScoreMatch(c.Title, query))
                    .Take(20)
                    .ToList();

            CommandPaletteList.ItemsSource = results;
            if (results.Count > 0)
                CommandPaletteList.SelectedIndex = 0;

            CommandPaletteNoResults.Visibility = results.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<CommandPaletteItem> BuildCommandCatalog()
        {
            var catalog = new List<CommandPaletteItem>();
            var keyBindingSvc = _keyBindingService ??= App.Current.Services.GetRequiredService<Services.KeyBindingService>();
            var bindings = keyBindingSvc.CloneCurrentBindings();

            // All registered commands
            foreach (var cmdId in ShortcutCommands.GetAllCommands())
            {
                var displayName = ShortcutCommands.GetDisplayName(cmdId);
                var category = ShortcutCommands.GetCategory(cmdId);
                var shortcut = bindings.TryGetValue(cmdId, out var keys) && keys.Count > 0
                    ? keys[0] : string.Empty;

                catalog.Add(new CommandPaletteItem
                {
                    Title = displayName,
                    Category = category,
                    CommandId = cmdId,
                    Shortcut = shortcut,
                    Type = CommandPaletteItemType.Command,
                    IconGlyph = GetCommandIconGlyph(category),
                });
            }

            // Open tabs
            for (int i = 0; i < ViewModel.Tabs.Count; i++)
            {
                var tab = ViewModel.Tabs[i];
                catalog.Add(new CommandPaletteItem
                {
                    Title = tab.Header ?? "Tab",
                    Category = _loc.Get("CommandPalette_Tabs"),
                    TabIndex = i,
                    Type = CommandPaletteItemType.Tab,
                    IconGlyph = "\uE737", // tab icon
                });
            }

            return catalog;
        }

        private static string GetCommandIconGlyph(string category) => category switch
        {
            "Navigation" => "\uE72A",
            "Edit" => "\uE70F",
            "Selection" => "\uE762",
            "View" => "\uE8A9",
            "Tab" => "\uE737",
            "Window" => "\uE8A7",
            "Shelf" => "\uE8F1",
            "CommandPalette" => "\uE773",
            "Workspace" => "\uE74C",
            "QuickLook" => "\uE8FF",
            _ => "\uE756",
        };

        private static bool FuzzyMatch(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return true;
            int qi = 0;
            foreach (var ch in text)
            {
                if (qi < query.Length && char.ToLowerInvariant(ch) == char.ToLowerInvariant(query[qi]))
                    qi++;
            }
            return qi == query.Length;
        }

        private static int ScoreMatch(string text, string query)
        {
            if (string.IsNullOrEmpty(query)) return 0;
            var lowerText = text.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();

            // Exact prefix match scores highest
            if (lowerText.StartsWith(lowerQuery)) return 100;
            // Contains scores mid
            if (lowerText.Contains(lowerQuery)) return 50;
            // Fuzzy match scores lowest
            return FuzzyMatch(text, query) ? 10 : 0;
        }
    }
}
