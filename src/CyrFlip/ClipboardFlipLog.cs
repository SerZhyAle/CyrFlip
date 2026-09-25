using System;

namespace CyrFlip
{
    /// <summary>
    /// How the flip pipeline's clipboard hand-back went - a paste that the target never took, a restore
    /// skipped because the clipboard had moved on, a clipboard too busy to back up - and nothing else:
    /// lengths and outcomes, never a character of the text. A one-line wrapper over
    /// <see cref="DiagnosticLog"/>, like <see cref="ClipboardHistoryLog"/>.
    /// </summary>
    internal static class ClipboardFlipLog
    {
        public const string FileName = "clipboard-flip.log";

        public static void Log(string message)
        {
            try { DiagnosticLog.Append(DiagnosticLog.Path(FileName), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message); }
            catch { /* diagnostics must never affect a flip */ }
        }
    }
}
