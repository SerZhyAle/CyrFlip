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
        public static string Describe(Exception? ex, string language)
        {
            if (ex == null) return Localization.Translate(language, "Произошла непредвиденная ошибка.");
            if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
                ex = agg.GetBaseException();

            if (ex is UnauthorizedAccessException || ex is SecurityException)
                return Localization.Translate(language, "Нет доступа к файлу или папке.");

            if (ex is IOException)
                return Localization.Translate(language, "Ошибка чтения или записи на диск.");

            if (ex is HttpRequestException || ex is WebException || ex is SocketException)
                return Localization.Translate(language, "Сетевая ошибка или сервер недоступен.");

            if (ex is Win32Exception)
                return Localization.Translate(language, "Не удалось запустить процесс.");

            return Localization.Translate(language, "Произошла непредвиденная ошибка.");
        }
    }
}
