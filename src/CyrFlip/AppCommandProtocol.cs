using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>The closed reason tokens of APP-COMMANDS rule 12 and item M that this product emits.</summary>
    internal static class AppCommandReasons
    {
        public const string UnknownVerb = "unknown-verb";
        public const string BadArgument = "bad-argument";
        public const string TooLarge = "too-large";
        public const string Schema = "schema";
        public const string SwitchOff = "switch-off";
        public const string Elevation = "elevation";
        public const string Busy = "busy";
        public const string NotRunning = "not-running";
        public const string TargetMissing = "target-missing";
        public const string Timeout = "timeout";
        public const string ModuleOff = "module-off";
        /// <summary>This product's own token (rule 12 lets a product add one): the notes journal could not be written or read.</summary>
        public const string StoreFailed = "store-failed";
        public const string NeedsPerson = "needs-person";
        public const string Error = "error";
        public const string IoError = "io-error";
        /// <summary>Item M: the target volume is full.</summary>
        public const string NoSpace = "no-space";
        /// <summary>Item M: a remote share the verb needed did not answer.</summary>
        public const string Network = "network";
        /// <summary>Rule 12: the person cancelled an interactive verb.</summary>
        public const string Cancelled = "cancelled";
        /// <summary>This product's own token (ticket S0110): the file the caller named is already there and <c>--overwrite</c> was not given.</summary>
        public const string Exists = "exists";
        /// <summary>This product's own token (ticket S0110): the screen could not be grabbed.</summary>
        public const string CaptureFailed = "capture-failed";
        /// <summary>This product's own token (ticket S0110): the file is saved, the clipboard was held by another program.</summary>
        public const string ClipboardFailed = "clipboard-failed";
        /// <summary>
        /// This product's own token (ticket S0117, exit 11): the traffic light target the caller named has a label that is also
        /// its address or a part of it, so no answer may carry the label. The person gives the target a label of its own.
        /// </summary>
        public const string LabelUnsafe = "label-unsafe";
        /// <summary>This product's own token (ticket S0120, exit 1): the export file could not be written, or its folder checked.</summary>
        public const string WriteFailed = "write-failed";
        /// <summary>This product's own token (ticket S0120, exit 13): the target volume is full when the notes export writes its file.</summary>
        public const string DiskFull = "disk-full";
        /// <summary>This product's own token (ticket S0125, exit 11): the packaged (Store, MSIX) build, where Windows owns startup, so <c>autostart set</c> writes nothing.</summary>
        public const string ManagedByWindows = "managed-by-windows";
        /// <summary>
        /// This product's own token (ticket S0128, exit 11): the translation server answered, but no model is installed on it, so
        /// <c>translate text</c> never asks the model - the state a script wants one branch for, apart from a dead <c>network</c>.
        /// </summary>
        public const string NoModel = "no-model";
        /// <summary>
        /// This product's own token (ticket S0129, exit 11): the window the verb names was found, but Windows would not give it
        /// the focus, and the selection commands' synthesized Ctrl+C and Ctrl+V only work in the foreground window. The person's
        /// one click on that window is the cure.
        /// </summary>
        public const string WindowNotForeground = "window-not-foreground";
        /// <summary>
        /// This product's own token (ticket S0129, exit 11): the named window is in the foreground and CyrFlip took its copy
        /// keystroke, but nothing in the window is selected.
        /// </summary>
        public const string NoSelection = "no-selection";
    }

    /// <summary>The exit codes of CLI-EVENT-STREAM rule 11 as APP-COMMANDS rule 11 places them.</summary>
    internal static class AppCommandExit
    {
        public const int Done = 0;
        public const int Failed = 1;
        public const int NotProven = 2;
        public const int Usage = 10;
        public const int NotReady = 11;
        public const int Permission = 12;
        /// <summary>Insufficient storage or resources (rule 11).</summary>
        public const int NoSpace = 13;
        /// <summary>A file or device could not be read or written (rule 11, item M <c>io-error</c>).</summary>
        public const int IoError = 14;
        /// <summary>A network or remote share failure (rule 11).</summary>
        public const int Network = 15;
        /// <summary>Stopped: the person cancelled an interactive verb (rule 11).</summary>
        public const int Cancelled = 16;
    }

    /// <summary>What came of sending one request to the running copy.</summary>
    internal enum AppCommandDelivery
    {
        /// <summary>A response line with this request's id arrived.</summary>
        Answered,
        /// <summary>No pipe accepted the connection within the delivery bound.</summary>
        NoListener,
        /// <summary>The copy predates the profile: a one-way pipe, a connection closed with no line, a line that is no response.</summary>
        OlderCopy,
        /// <summary>The completion bound passed with no answer.</summary>
        TimedOut,
        /// <summary>An answer that is too long, or carries an empty or another request's id.</summary>
        Unreadable,
        /// <summary>The pipe is served by a process outside this user's session.</summary>
        NotOurs,
    }

    /// <summary>One request of the request-response profile (APP-COMMANDS rule 7).</summary>
    internal sealed class AppCommandRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string Command { get; set; } = "";
        public string[] Args { get; set; } = new string[0];
        public string WorkingDir { get; set; } = "";
        public string Origin { get; set; } = "cli";
        public string RequestId { get; set; } = "";
        public string Wait { get; set; } = "done";
        /// <summary>The caller typed a spelling shipped before the adoption (item F): lifts the rule 13 gate only.</summary>
        public bool Legacy { get; set; }
    }

    /// <summary>One response line (APP-COMMANDS rule 7).</summary>
    internal sealed class AppCommandResponse
    {
        public string RequestId { get; set; } = "";
        public string Status { get; set; } = "done";
        public int ExitCode { get; set; }
        public string? Reason { get; set; }
        public string Message { get; set; } = "";
        public Dictionary<string, object>? Data { get; set; }
        /// <summary>Run once the line has reached the caller (quit closes the program only then). Never serialized.</summary>
        public Action? AfterSent { get; set; }

        public static AppCommandResponse Done(string message, Dictionary<string, object>? data = null)
            => new AppCommandResponse { Status = "done", ExitCode = AppCommandExit.Done, Message = message, Data = data };
        public static AppCommandResponse Accepted(string message)
            => new AppCommandResponse { Status = "accepted", ExitCode = AppCommandExit.Done, Message = message };
        /// <summary>With answer data - the members a verb knows at hand-off (ticket S0121: <c>kind</c> and <c>sizeBytes</c> of a started send).</summary>
        public static AppCommandResponse Accepted(string message, Dictionary<string, object>? data)
            => new AppCommandResponse { Status = "accepted", ExitCode = AppCommandExit.Done, Message = message, Data = data };
        public static AppCommandResponse Refused(string reason, int exitCode, string message)
            => new AppCommandResponse { Status = "refused", ExitCode = exitCode, Reason = reason, Message = message };
        public static AppCommandResponse Failed(string reason, int exitCode, string message)
            => new AppCommandResponse { Status = "failed", ExitCode = exitCode, Reason = reason, Message = message };
        public static AppCommandResponse BusyNow(string message)
            => new AppCommandResponse { Status = "busy", ExitCode = AppCommandExit.NotReady, Reason = AppCommandReasons.Busy, Message = message };
    }

    /// <summary>
    /// The wire of the request-response profile (APP-COMMANDS rule 7, APP-ACTIVATION 0.15 section 11), pure:
    /// one JSON object a line, UTF-8, members matched by name and unknown ones ignored. A line that is not JSON,
    /// or has no <c>requestId</c>, is not a profile request (item I paragraph 3) - the listener reads such a line as
    /// the one-line token of the generation before.
    /// </summary>
    internal static class AppCommandProtocol
    {
        public const int SchemaVersion = 1;

        /// <summary>A line that opens like a JSON object is a profile request; anything else is a legacy token.</summary>
        public static bool LooksLikeRequest(string line) => line.TrimStart().StartsWith("{", StringComparison.Ordinal);

        public static string Serialize(AppCommandRequest request)
        {
            var map = new Dictionary<string, object>
            {
                ["schemaVersion"] = request.SchemaVersion,
                ["command"] = request.Command,
                ["args"] = request.Args,
                ["workingDir"] = request.WorkingDir,
                ["origin"] = request.Origin,
                ["requestId"] = request.RequestId,
                ["wait"] = request.Wait,
            };
            if (request.Legacy) map["legacy"] = true;
            return new JavaScriptSerializer().Serialize(map);
        }

        /// <summary>
        /// The request a line carries; null with <paramref name="reason"/> set when it cannot be read
        /// (<c>bad-argument</c>) or comes from a newer schema (<c>schema</c>). <paramref name="requestId"/> is the
        /// line's own when it had one, else empty (item I paragraph 3).
        /// </summary>
        public static AppCommandRequest? TryParseRequest(string line, out string reason, out string requestId)
        {
            reason = ""; requestId = "";
            Dictionary<string, object>? map;
            try { map = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>; }
            catch (Exception) { map = null; } // a malformed line is an answer, never a fault (APP-ACTIVATION rule 3)
            if (map == null) { reason = AppCommandReasons.BadArgument; return null; }

            requestId = map.TryGetValue("requestId", out object? id) && id is string s ? s : "";
            if (requestId.Length == 0) { reason = AppCommandReasons.BadArgument; return null; }
            int schema = map.TryGetValue("schemaVersion", out object? v) && v is int n ? n : 0;
            if (schema > SchemaVersion) { reason = AppCommandReasons.Schema; return null; }
            if (schema < 1 || !(map.TryGetValue("command", out object? c) && c is string command) || command.Length == 0)
            {
                reason = AppCommandReasons.BadArgument;
                return null;
            }
            string[] args = map.TryGetValue("args", out object? a) && a is object[] list
                ? list.Select(x => x as string ?? "").ToArray()
                : new string[0];
            if (map.TryGetValue("args", out object? raw) && raw is object[] items && items.Any(x => !(x is string)))
            {
                reason = AppCommandReasons.BadArgument;
                return null;
            }
            return new AppCommandRequest
            {
                SchemaVersion = schema,
                Command = command,
                Args = args,
                WorkingDir = map.TryGetValue("workingDir", out object? w) && w is string dir ? dir : "",
                Origin = map.TryGetValue("origin", out object? o) && o is string origin ? origin : "cli",
                RequestId = requestId,
                Wait = map.TryGetValue("wait", out object? wt) && wt is string wait ? wait : "done",
                Legacy = map.TryGetValue("legacy", out object? l) && l is bool legacy && legacy,
            };
        }

        public static string Serialize(AppCommandResponse response)
        {
            var map = new Dictionary<string, object>
            {
                ["schemaVersion"] = SchemaVersion,
                ["requestId"] = response.RequestId,
                ["status"] = response.Status,
                ["exitCode"] = response.ExitCode,
            };
            if (!string.IsNullOrEmpty(response.Reason)) map["reason"] = response.Reason!;
            map["message"] = response.Message;
            if (response.Data != null) map["data"] = response.Data;
            return new JavaScriptSerializer().Serialize(map);
        }

        /// <summary>The response a line carries, or null when it is not one (not JSON, no <c>schemaVersion</c>).</summary>
        public static AppCommandResponse? TryParseResponse(string line)
        {
            Dictionary<string, object>? map;
            try { map = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>; }
            catch (Exception) { map = null; } // not a response of this profile
            if (map == null || !(map.TryGetValue("schemaVersion", out object? v) && v is int)) return null;
            if (!(map.TryGetValue("exitCode", out object? e) && e is int exit)) return null;
            return new AppCommandResponse
            {
                RequestId = map.TryGetValue("requestId", out object? id) && id is string s ? s : "",
                Status = map.TryGetValue("status", out object? st) && st is string status ? status : "failed",
                ExitCode = exit,
                Reason = map.TryGetValue("reason", out object? r) && r is string reason ? reason : null,
                Message = map.TryGetValue("message", out object? m) && m is string message ? message : "",
                Data = map.TryGetValue("data", out object? d) ? d as Dictionary<string, object> : null,
            };
        }

        /// <summary>The <c>data</c> of a response as the JSON a <c>--result</c> file holds; <c>{}</c> when it has none.</summary>
        public static string DataJson(AppCommandResponse response)
            => new JavaScriptSerializer().Serialize(response.Data ?? new Dictionary<string, object>());
    }
}
