using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

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
    /// Only the three commands of <see cref="ParseCommand"/> ever travel here; nothing else on the
    /// command line is treated as a command (tech plan Фаза 3.1).
    /// </summary>
    internal sealed class LauncherIpc : IDisposable
    {
        public const string PipeName = "CyrFlip_Launcher_Pipe";
        public const string RunPrefix = "/launcher-run:";
        public const string SettingsCommand = "/launcher-settings";
        public const string ExitCommand = "/exit";

        private readonly Action<string> _onCommand;
        private readonly string _pipeName;
        private NamedPipeServerStream? _server;
        private Thread? _thread;
        private volatile bool _stopped;

        /// <summary><paramref name="pipeName"/> is overridable only so tests never talk to a live CyrFlip.</summary>
        public LauncherIpc(Action<string> onCommand, string? pipeName = null)
        {
            _onCommand = onCommand;
            _pipeName = pipeName ?? PipeName;
        }

        /// <summary>
        /// The launcher command carried by the process arguments, or null. Strict by design: a
        /// malformed guid or any unknown argument is not a command. Normalizes the run command to
        /// its canonical "D" guid form so the receiver can parse it back without surprises.
        /// </summary>
        public static string? ParseCommand(string[] args)
        {
            if (args.Length == 0) return null;
            string arg = args[0].Trim();
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

        /// <summary>Send one command to the live instance. False when it could not be delivered in time.</summary>
        public static bool TrySend(string command, int timeoutMs = 1000, string? pipeName = null)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.Out);
                client.Connect(timeoutMs);
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

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Listen) { IsBackground = true, Name = "CyrFlip.LauncherIpc" };
            _thread.Start();
        }

        private void Listen()
        {
            // Per-connection failure boundary: a failed accept/read is logged and the loop takes the
            // next connection. Only Dispose ends the loop.
            while (!_stopped)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1);
                    _server = server;
                    server.WaitForConnection();
                    if (_stopped) break;

                    using var reader = new StreamReader(server);
                    string? command = reader.ReadLine();
                    if (!string.IsNullOrEmpty(command))
                    {
                        LauncherLog.Log("IPC: received: " + command);
                        _onCommand(command!);
                    }
                }
                catch (Exception ex)
                {
                    if (_stopped) break;
                    LauncherLog.Log("IPC: connection error (listener continues): " + ex.Message);
                    Thread.Sleep(200); // a persistent error (pipe name occupied) must not spin hot
                }
                finally
                {
                    _server = null;
                }
            }
            LauncherLog.Log("IPC: listener stopped");
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
