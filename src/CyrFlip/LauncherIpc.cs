using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// Named-pipe IPC for the launcher's single-instance model: the live CyrFlip listens for
    /// commands, a second (Jump List) launch forwards its command with <see cref="TrySend"/> and
    /// exits. The listener's failure boundary is per-connection, so one bad connection never stops
    /// it, and the received command is handed to the callback raw - marshalling to the UI thread is
    /// the subscriber's job. Runs on a plain dedicated thread because .NET Framework cannot cancel
    /// a pipe's async wait reliably; <see cref="Dispose"/> unblocks the wait with a dummy client.
    ///
    /// Only the three commands of <see cref="ParseCommand(string[])"/> ever travel here; nothing else
    /// on the command line, and nothing else read from the pipe, is treated as a command (tech plan
    /// Фаза 3.1).
    ///
    /// <b>Whose pipe it is</b> (ticket S0008, LS-2). A pipe name is machine-global, while the
    /// single-instance mutex is per session, so the name carries the user's SID and the session id:
    /// with fast user switching or on a terminal server each CyrFlip has its own pipe instead of the
    /// second one failing forever and the first one receiving the second user's Jump List. The
    /// instance is created with a DACL that admits the current user only (and never over the
    /// network), it lives for the whole process - <c>Disconnect</c> between clients rather than a new
    /// instance per connection, which let the name change hands - and a client that connects and
    /// says nothing is dropped after <see cref="ReadTimeoutMs"/>. The sending side opens the pipe at
    /// Identification level only and checks that the process serving it runs in its own session as
    /// its own user before it writes a byte.
    ///
    /// <b>With the launcher off</b> only <see cref="ExitCommand"/> is honoured (open decision 1 of
    /// S0008): the build and release scripts stop the tray process with it, and every other command
    /// belongs to a feature the user has not switched on.
    /// </summary>
    internal sealed class LauncherIpc : IDisposable
    {
        public const string PipeNamePrefix = "CyrFlip_Launcher_";
        public const string RunPrefix = "/launcher-run:";
        public const string SettingsCommand = "/launcher-settings";
        public const string ExitCommand = "/exit";

        /// <summary>Longest line that can be a command; the longest real one is 50 characters.</summary>
        public const int MaxCommandChars = 256;

        /// <summary>How long a connected client has to deliver its line.</summary>
        public const int ReadTimeoutMs = 2000;

        private const int FirstBackoffMs = 200;
        private const int MaxBackoffMs = 30000;

        private readonly Action<string> _onCommand;
        private readonly string _pipeName;
        private readonly int _readTimeoutMs;
        private NamedPipeServerStream? _server;
        private Thread? _thread;
        private volatile bool _stopped;
        private volatile bool _launcherCommands = true;

        /// <summary>
        /// <paramref name="pipeName"/> and <paramref name="readTimeoutMs"/> are overridable only so
        /// tests never talk to a live CyrFlip and never wait two seconds.
        /// </summary>
        public LauncherIpc(Action<string> onCommand, string? pipeName = null, int readTimeoutMs = ReadTimeoutMs)
        {
            _onCommand = onCommand;
            _pipeName = pipeName ?? PipeName;
            _readTimeoutMs = readTimeoutMs;
        }

        private static string? s_defaultPipeName;

        /// <summary>This user's, this session's pipe: <c>CyrFlip_Launcher_{sid}_{session}</c>.</summary>
        public static string PipeName
        {
            get
            {
                if (s_defaultPipeName != null) return s_defaultPipeName;
                string sid;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    sid = identity.User?.Value ?? "unknown";
                int session;
                using (Process self = Process.GetCurrentProcess())
                    session = self.SessionId;
                return s_defaultPipeName = BuildPipeName(sid, session);
            }
        }

        internal static string BuildPipeName(string userSid, int sessionId)
            => PipeNamePrefix + userSid + "_" + sessionId;

        /// <summary>
        /// While false, every command but <see cref="ExitCommand"/> is dropped on the listener's
        /// thread. Follows <c>EnableScenarioLauncher</c>.
        /// </summary>
        public void SetLauncherCommandsEnabled(bool enabled) => _launcherCommands = enabled;

        /// <summary>
        /// The launcher command carried by the process arguments, or null. Strict by design: a
        /// malformed guid or any unknown argument is not a command. Normalizes the run command to
        /// its canonical "D" guid form so the receiver can parse it back without surprises.
        /// </summary>
        public static string? ParseCommand(string[] args)
            => args.Length == 0 ? null : ParseCommand(args[0]);

        /// <summary>One line as a command, or null - the same rules for the pipe as for the command line.</summary>
        public static string? ParseCommand(string? line)
        {
            if (line == null || line.Length > MaxCommandChars) return null;
            string arg = line.Trim();
            if (arg.Equals(SettingsCommand, StringComparison.OrdinalIgnoreCase)) return SettingsCommand;
            if (arg.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase)) return ExitCommand;
            if (arg.StartsWith(RunPrefix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(arg.Substring(RunPrefix.Length), out Guid id))
                return RunPrefix + id.ToString("D");
            return null;
        }

        /// <summary>The scenario id of a run command, or null for anything else.</summary>
        public static Guid? RunId(string command)
            => command.StartsWith(RunPrefix, StringComparison.OrdinalIgnoreCase)
               && Guid.TryParse(command.Substring(RunPrefix.Length), out Guid id)
                ? id : (Guid?)null;

        /// <summary>
        /// Send one command to the live instance. False when it could not be delivered in time - or
        /// when whoever serves the pipe is not this user's CyrFlip in this session.
        /// </summary>
        public static bool TrySend(string command, int timeoutMs = 1000, string? pipeName = null)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.Out,
                    PipeOptions.None, TokenImpersonationLevel.Identification);
                client.Connect(timeoutMs);
                if (!ServerIsOurs(client, out string why))
                {
                    LauncherLog.Log("IPC: send refused - " + why);
                    return false;
                }
                using var writer = new StreamWriter(client);
                writer.WriteLine(command);
                writer.Flush();
                LauncherLog.Log("IPC: sent to live instance: " + command);
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("IPC: send failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>The process serving the pipe runs in our session as our user.</summary>
        private static bool ServerIsOurs(NamedPipeClientStream client, out string why)
        {
            why = "";
            if (!WindowInterop.GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverPid))
            {
                why = "the server process is unknown";
                return false;
            }

            using Process self = Process.GetCurrentProcess();
            if (!WindowInterop.ProcessIdToSessionId(serverPid, out uint serverSession)
                || serverSession != (uint)self.SessionId)
            {
                why = "the server runs in another session";
                return false;
            }

            string? serverUser = UserOfProcess(serverPid);
            string? ourUser;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                ourUser = identity.User?.Value;
            if (serverUser == null || ourUser == null || !string.Equals(serverUser, ourUser, StringComparison.OrdinalIgnoreCase))
            {
                why = "the server runs as another user";
                return false;
            }
            return true;
        }

        private static string? UserOfProcess(uint pid)
        {
            IntPtr process = WindowInterop.OpenProcess(WindowInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!WindowInterop.OpenProcessToken(process, WindowInterop.TOKEN_QUERY, out IntPtr token)) return null;
                try
                {
                    using var identity = new WindowsIdentity(token);
                    return identity.User?.Value;
                }
                finally { WindowInterop.CloseHandle(token); }
            }
            catch { return null; }
            finally { WindowInterop.CloseHandle(process); }
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Listen) { IsBackground = true, Name = "CyrFlip.LauncherIpc" };
            _thread.Start();
        }

        /// <summary>The one server instance: this user only, never from the network.</summary>
        private NamedPipeServerStream CreateServer()
        {
            var security = new PipeSecurity();
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, MaxCommandChars * 4, 0, security);
        }

        private void Listen()
        {
            // Per-connection failure boundary: a failed accept/read is logged and the loop takes the
            // next connection. A failure to create the pipe at all backs off exponentially and is
            // logged once, then once per hundred - it used to retry every 200 ms and log every time.
            int failures = 0;
            int backoffMs = FirstBackoffMs;
            while (!_stopped)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = CreateServer();
                    _server = server;
                    while (!_stopped)
                    {
                        server.WaitForConnection();
                        if (_stopped) break;
                        failures = 0;
                        backoffMs = FirstBackoffMs;

                        string? line = ReadLine(server);
                        try { server.Disconnect(); } catch { /* already disconnected */ }
                        Dispatch(line);
                    }
                }
                catch (Exception ex)
                {
                    if (_stopped) break;
                    failures++;
                    if (failures == 1 || failures % 100 == 0)
                        LauncherLog.Log($"IPC: listener error #{failures} (listener continues, retry in {backoffMs} ms): {ex.Message}");
                    Thread.Sleep(backoffMs);
                    backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
                }
                finally
                {
                    _server = null;
                    try { server?.Dispose(); } catch { }
                }
            }
            LauncherLog.Log("IPC: listener stopped");
        }

        private void Dispatch(string? line)
        {
            string? command = ParseCommand(line);
            if (command == null)
            {
                if (line != null) LauncherLog.Log("IPC: ignored a line that is not a command (" + line.Length + " chars)");
                return;
            }
            if (command != ExitCommand && !_launcherCommands)
            {
                LauncherLog.Log("IPC: ignored while the launcher is off: " + command);
                return;
            }
            LauncherLog.Log("IPC: received: " + command);
            _onCommand(command);
        }

        /// <summary>
        /// One line from the connected client: at most <see cref="MaxCommandChars"/> characters and
        /// <see cref="ReadTimeoutMs"/>. Null when the client is silent, too long or broken - the
        /// caller disconnects it either way. A read abandoned on the timeout is settled by the
        /// disconnect before the instance is reused; one that will not settle fails the instance,
        /// which the loop above replaces.
        /// </summary>
        private string? ReadLine(NamedPipeServerStream server)
        {
            var buffer = new byte[MaxCommandChars * 4 + 2]; // UTF-8 worst case, plus CRLF
            int total = 0;
            int started = Environment.TickCount;
            while (true)
            {
                int left = _readTimeoutMs - unchecked(Environment.TickCount - started);
                if (left <= 0 || total == buffer.Length) return null;

                Task<int> read = server.ReadAsync(buffer, total, buffer.Length - total);
                bool done;
                try { done = read.Wait(left); }
                catch { return null; } // the client broke the pipe

                if (!done)
                {
                    try { server.Disconnect(); } catch { }
                    bool settled;
                    try { settled = read.Wait(1000); }
                    catch { settled = true; }
                    if (!settled) throw new IOException("a timed-out read did not settle");
                    LauncherLog.Log("IPC: dropped a client that sent nothing within " + _readTimeoutMs + " ms");
                    return null;
                }

                int n = read.Result;
                if (n == 0) break; // the client closed: what arrived is the line
                int newline = Array.IndexOf(buffer, (byte)'\n', total, n);
                total += n;
                if (newline >= 0) { total = newline; break; }
            }

            string text = Encoding.UTF8.GetString(buffer, 0, total).TrimEnd('\r');
            return text.Length <= MaxCommandChars ? text : null;
        }

        public void Dispose()
        {
            if (_stopped) return;
            _stopped = true;
            try { _server?.Dispose(); } catch { }
            // WaitForConnection on .NET Framework does not always abort on Dispose from another
            // thread - a throwaway connection wakes it deterministically so the loop can exit.
            try
            {
                using var nudge = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                nudge.Connect(100);
            }
            catch { /* nothing was waiting - fine */ }
        }
    }
}
