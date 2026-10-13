using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;

namespace CyrFlip
{
    /// <summary>
    /// Classifies exceptions by type and maps them to localized failure causes (APP-BEHAVIOUR rule 6).
    /// Raw exception messages (which may contain sensitive user paths, tokens, or unreadable stack text)
    /// must never reach the user in dialogs or banners.
    /// </summary>
    internal static class FailureCause
    {
        // The three IOException outcomes a user can act on (S0065 E-8): sharing violation
        // (ERROR_SHARING_VIOLATION), disk full (ERROR_DISK_FULL) and path too long
        // (ERROR_FILENAME_EXCED_RANGE), as the runtime surfaces them - a facility of 0x0007 and
        // the code as the low word.
        private const int HrSharingViolation = unchecked((int)0x80070020);
        private const int HrDiskFull = unchecked((int)0x80070070);
        private const int HrPathTooLong = unchecked((int)0x800700CE);

        public static string Describe(Exception? ex, string language)
            => Describe(ex, ru => Localization.Translate(language, ru));

        /// <summary>
        /// The same classification for a caller that holds the translating function (a window's
        /// <c>T</c>, the launcher's <c>translate</c> seam) rather than a language string - the shape
        /// S0065 E-5 asked for, so a balloon can no longer read "Could not start: Не удалось
        /// запустить процесс." in one UI language. A null seam is the English key itself.
        /// </summary>
        public static string Describe(Exception? ex, Func<string, string>? translate)
        {
            translate ??= ru => ru;
            if (ex == null) return translate("An unexpected error occurred.");
            if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
                ex = agg.GetBaseException();

            if (ex is UnauthorizedAccessException || ex is SecurityException)
                return translate("Access to file or folder is denied.");

            if (ex is IOException io)
            {
                if (io.HResult == HrSharingViolation)
                    return translate("The file is in use by another program.");
                if (io.HResult == HrDiskFull)
                    return translate("The disk is full - free some space.");
                if (io.HResult == HrPathTooLong)
                    return translate("The path to the file is too long.");
                return translate("Disk read or write error.");
            }

            if (ex is HttpRequestException || ex is WebException || ex is SocketException)
                return translate("Network error or server is unavailable.");

            if (ex is Win32Exception)
                return translate("Could not start process.");

            return translate("An unexpected error occurred.");
        }
    }
}
