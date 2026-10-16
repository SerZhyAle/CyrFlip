using System;

namespace CyrFlip
{
    /// <summary>
    /// Diagnostic log for the exchange server clipboard feature (<c>exchange-clipboard.log</c>, ticket S0074).
    ///
    /// <para>Vault and privacy invariants (CLIPBOARD-GUARD item R, DIAGNOSTIC-REPORT redaction):
    /// Records only site, payload kind, size, entry ordinal, and reason code.
    /// <b>NEVER</b> records clipboard content, file names, logins, device tokens, passwords, or network addresses.</para>
    /// </summary>
    internal static class ExchangeClipboardLog
    {
        public const string FileName = "exchange-clipboard.log";

        public static void Log(string site, string kind, long size, int entryOrdinal, string reason)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} - {site} kind={kind} size={size} entry={entryOrdinal} reason={reason}";
                DiagnosticLog.Append(DiagnosticLog.Path(FileName), line);
            }
            catch
            {
                // Diagnostic logging must never throw into application code
            }
        }

        public static void Fault(string site, Exception? ex = null)
        {
            try
            {
                string err = ex != null ? (" " + ex.GetType().Name) : "";
                string line = $"{DateTime.Now:HH:mm:ss.fff} - {site}{err}";
                DiagnosticLog.Append(DiagnosticLog.Path(FileName), line);
            }
            catch
            {
            }
        }
    }
}
