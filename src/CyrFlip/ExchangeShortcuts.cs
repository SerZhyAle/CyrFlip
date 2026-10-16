using System;
using System.Text;

namespace CyrFlip
{
    internal enum ExchangeShortcutKind
    {
        Send,
        Get,
    }

    /// <summary>
    /// The desktop shortcuts of the two clipboard functions (ticket S0074, D6; the owner's wish that they exist as
    /// separate functions). A shortcut starts <c>CyrFlip.exe</c> with a command; when CyrFlip is already running the
    /// command reaches the live instance over the pipe and the second process ends, and when it is not running it
    /// starts as the tray app and then runs the command. A shortcut works only while the module is on.
    /// </summary>
    internal static class ExchangeShortcuts
    {
        public static string CommandOf(ExchangeShortcutKind kind)
            => kind == ExchangeShortcutKind.Send ? LauncherIpc.ClipboardSendCommand : LauncherIpc.ClipboardGetCommand;

        public static string CaptionOf(ExchangeShortcutKind kind)
            => kind == ExchangeShortcutKind.Send ? "Send clipboard to server" : "Get clipboard from server";

        public static string GlyphOf(ExchangeShortcutKind kind)
            => kind == ExchangeShortcutKind.Send ? AppGlyphs.SendClipboard : AppGlyphs.ReceiveClipboard;

        /// <summary>The file name of the shortcut in the interface language: the caption with what Windows refuses removed.</summary>
        public static string FileNameOf(ExchangeShortcutKind kind, Func<string, string> translate)
        {
            string caption = translate(CaptionOf(kind));
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(caption.Length);
            foreach (char c in caption)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? ' ' : c);
            string name = sb.ToString().Trim().TrimEnd('.');
            return (name.Length == 0 ? CaptionOf(kind) : name) + ".lnk";
        }

        /// <summary>Creates the shortcut on the desktop; the path of the file, or null when it could not be written.</summary>
        public static string? Create(ExchangeShortcutKind kind, Func<string, string> translate)
        {
            string? icon = LauncherShortcutIcons.PathFor(GlyphOf(kind));
            return AppShortcuts.CreateDesktopCommandShortcut(FileNameOf(kind, translate), CommandOf(kind),
                translate(CaptionOf(kind)), icon);
        }
    }
}
