using System.Collections.Generic;

namespace Span.Models
{
    /// <summary>
    /// Represents a menu item enumerated from the Windows Shell IContextMenu.
    /// Used to render shell extension items (Bandizip, 7-Zip, VS Code, etc.)
    /// inside a WinUI MenuFlyout.
    /// </summary>
    public class ShellMenuItem
    {
        /// <summary>Menu item display text (with & accelerator markers stripped)</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Command ID offset from idCmdFirst (used for InvokeCommand)</summary>
        public int CommandId { get; set; }

        /// <summary>Canonical verb from GetCommandString (e.g. "open", "7-zip.extract"), empty if unavailable</summary>
        public string Verb { get; set; } = string.Empty;

        /// <summary>True if this is a separator line</summary>
        public bool IsSeparator { get; set; }

        /// <summary>True if the item is disabled/grayed</summary>
        public bool IsDisabled { get; set; }

        /// <summary>True if the item uses owner-drawn rendering (may have no text)</summary>
        public bool IsOwnerDrawn { get; set; }

        /// <summary>Child items for submenus</summary>
        public List<ShellMenuItem>? Children { get; set; }

        /// <summary>True if this item has a submenu</summary>
        public bool HasSubmenu => Children != null && Children.Count > 0;
    }
}
