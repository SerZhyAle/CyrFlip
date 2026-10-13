using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The words of the traffic light: a state, a reason, a kind and the hover text (ticket S0069 section 6).
    /// Pure - the translating function and the clock come in as arguments, so the whole tooltip is tested
    /// without a window. Every source string here is English, like every key of <see cref="Localization"/>.
    /// </summary>
    internal static class TrafficLightText
    {
        /// <summary>The kind as a person says it.</summary>
        public static string KindWord(string? kind, Func<string, string> t)
        {
            switch (kind)
            {
                case TrafficLightKinds.Ping: return t("Host (ping)");
                case TrafficLightKinds.Tcp: return t("TCP port");
                case TrafficLightKinds.Http: return t("Website (HTTP)");
                case TrafficLightKinds.Folder: return t("Folder");
                case TrafficLightKinds.Drive: return t("Drive");
                case TrafficLightKinds.Sftp: return t("SFTP resource");
                case TrafficLightKinds.Exchange: return t("Exchange server");
                default: return t("Not chosen");
            }
        }

        /// <summary>The light as a word - a state must never rest on colour alone.</summary>
        public static string StateWord(TrafficLightTile tile, Func<string, string> t)
        {
            if (tile.Disabled) return t("Off");
            if (tile.InvalidAddress) return t("Invalid address");
            switch (tile.State)
            {
                case TrafficState.Ok: return t("Available");
                case TrafficState.Degraded: return t("Degraded");
                case TrafficState.Down: return t("Unavailable");
                default: return t("Not checked yet");
            }
        }

        /// <summary>A reason code as a sentence fragment. An unknown code is shown as it is, never hidden.</summary>
        public static string ReasonWord(string? reason, Func<string, string> t)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            switch (reason)
            {
                case TrafficReasons.Ok: return t("all is well");
                case TrafficReasons.Slow: return t("answers slowly");
                case TrafficReasons.Unverified: return t("answers, but is not verified");
                case TrafficReasons.Timeout: return t("no answer within the time limit");
                case TrafficReasons.NameNotResolved: return t("name not found");
                case TrafficReasons.ConnectionRefused: return t("connection refused");
                case TrafficReasons.NetworkUnreachable: return t("network unreachable");
                case TrafficReasons.AccessDenied: return t("access denied");
                case TrafficReasons.PathNotFound: return t("path not found");
                case TrafficReasons.DrivePresentNot: return t("drive not connected");
                case TrafficReasons.TlsFailed: return t("secure connection failed");
                case TrafficReasons.CertificateNotTrusted: return t("certificate is not trusted");
                case TrafficReasons.CertificateChanged: return t("certificate differs from the pinned one");
                case TrafficReasons.NotSshServer: return t("not an SSH server");
                case TrafficReasons.ComputerOffline: return t("this computer is offline");
                case TrafficReasons.TooManyRedirects: return t("too many redirects");
                case TrafficReasons.InvalidAddress: return t("invalid address");
                case TrafficReasons.Failed: return t("the check failed");
            }
            if (reason!.StartsWith("http-", StringComparison.Ordinal))
                return Localization.Format(t, "server answered {0}", reason.Substring(5));
            return reason;
        }

        /// <summary>The caption on the tile: the label, or the address when the user gave none.</summary>
        public static string Caption(TrafficLightTarget target)
            => string.IsNullOrWhiteSpace(target.Label) ? target.Address : target.Label.Trim();

        /// <summary>
        /// "Availability: 1 of 12 down" / "all 12 up" / "computer is offline" - the tray item's text and the
        /// board's header (D14, D23). Null when there is nothing to say (no target is switched on).
        /// </summary>
        public static string? SummaryLine(TrafficLightSummary summary, Func<string, string> t)
        {
            if (summary.Offline) return t("this computer is offline");
            if (summary.Total == 0) return null;
            if (summary.Down > 0) return Localization.Format(t, "unavailable: {0} of {1}", summary.Down, summary.Total);
            if (summary.Degraded > 0) return Localization.Format(t, "degraded: {0} of {1}", summary.Degraded, summary.Total);
            if (summary.Unknown == summary.Total) return Localization.Format(t, "not checked yet: {0}", summary.Total);
            return Localization.Format(t, "all available: {0}", summary.Total);
        }

        /// <summary>
        /// The hover text: label, kind and address, the light and its reason, latency, the three times, the
        /// failure streak and the stale and offline notes. One fact per line; a missing fact is left out.
        /// </summary>
        public static string Tooltip(TrafficLightTile tile, Func<string, string> t, DateTime nowUtc, TimeZoneInfo zone)
        {
            TrafficLightTarget target = tile.Target;
            var lines = new List<string>();
            lines.Add(Caption(target));
            lines.Add(KindWord(target.Kind, t) + " - " + target.Address);

            string state = StateWord(tile, t);
            string reason = ReasonWord(tile.Reason, t);
            if (!tile.Disabled && !tile.InvalidAddress && tile.State != TrafficState.Unknown && reason.Length > 0
                && tile.Reason != TrafficReasons.Ok)
                state += " - " + reason;
            lines.Add(t("State:") + " " + state);

            if (tile.Disabled) return string.Join("\r\n", lines);

            if (tile.LatencyMs.HasValue && tile.State != TrafficState.Down)
                lines.Add(t("Latency:") + " " + Localization.Format(t, "{0} ms", tile.LatencyMs.Value));
            if (tile.CheckedAtUtc.HasValue)
                lines.Add(t("Checked:") + " " + When(tile.CheckedAtUtc.Value, nowUtc, zone));
            if (tile.LastOkAtUtc.HasValue && tile.State == TrafficState.Down)
                lines.Add(t("Last worked:") + " " + When(tile.LastOkAtUtc.Value, nowUtc, zone));
            else if (!tile.LastOkAtUtc.HasValue && tile.State == TrafficState.Down)
                lines.Add(t("Last worked:") + " " + t("never"));

            if (tile.Checking)
                lines.Add(t("Checking.."));
            else if (target.ManualOnly)
                lines.Add(t("Next check:") + " " + t("manual only"));
            else if (tile.NextDueUtc.HasValue && !tile.InvalidAddress)
                lines.Add(t("Next check:") + " " + When(tile.NextDueUtc.Value, nowUtc, zone));

            if (tile.Confirming)
                lines.Add(Localization.Format(t, "Confirming: {0} of {1}", tile.FailureStreak, target.FailuresBeforeDown));
            if (!string.IsNullOrEmpty(tile.Note))
                lines.Add(NoteText(tile.Note, t));
            if (tile.PausedOffline)
                lines.Add(t("This computer is offline - checking is paused."));
            else if (tile.Stale)
                lines.Add(t("The result is out of date."));
            return string.Join("\r\n", lines);
        }

        // A note is either a short English tag from a probe or a pinned/observed pair; the tag is localized.
        private static string NoteText(string note, Func<string, string> t)
        {
            if (note == "untrusted certificate accepted") return t("A certificate that Windows does not trust was accepted.");
            return note;
        }

        /// <summary>Today's time as a clock reading, another day with its date.</summary>
        public static string When(DateTime utc, DateTime nowUtc, TimeZoneInfo zone)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
            return local.Date == now.Date
                ? local.ToString("T", CultureInfo.CurrentCulture)
                : local.ToString("g", CultureInfo.CurrentCulture);
        }

        /// <summary>The accessible value of a tile: the state word and the reason, in one phrase.</summary>
        public static string AccessibleValue(TrafficLightTile tile, Func<string, string> t)
        {
            string state = StateWord(tile, t);
            string reason = ReasonWord(tile.Reason, t);
            if (tile.Disabled || tile.InvalidAddress || tile.State == TrafficState.Unknown || reason.Length == 0
                || tile.Reason == TrafficReasons.Ok) return state;
            return state + ", " + reason;
        }
    }
}
