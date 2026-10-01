using System;
using System.IO;
using System.Threading;

namespace CyrFlip
{
    /// <summary>
    /// Publishes the current layout code (EN, RU, DE, ZH, ..) to a small file so external tools can read
    /// it - chiefly the companion VS Code extension, which can place the marker exactly at the
    /// editor caret (something the external UIA overlay can't do reliably in Monaco/Electron).
    ///
    /// This is the producing half of <c>LAYOUT-SIGNAL</c> (rules 1, 2, 3, 5 and 6): the two locations, one
    /// fact per file, the ASCII payload, one writer whose last write is the latest state, and the files
    /// removed on a clean exit so that their absence means "not running".
    ///
    /// Unpackaged: %LOCALAPPDATA%\CyrFlip\layout.txt. MSIX (Store): the package's own per-user folder,
    /// %LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache\Local\CyrFlip\layout.txt, addressed by its real
    /// path (<see cref="DataFolder"/>, ticket S0016), plus a best-effort <b>mirror</b> of both files in the
    /// deprecated machine-wide %ProgramData%\CyrFlip that a pre-S0016 extension still reads
    /// (<c>LAYOUT-SIGNAL</c> 1.1 rule 1, kept until 2027-03-31 and two extension releases - retiring it is
    /// a contract step, not a date check in code). The extension checks every location
    /// (see vscode-extension/src/extension.ts).
    /// </summary>
    internal static class LayoutPublisher
    {
        internal const string CodeFileName = "layout.txt";

        /// <summary>
        /// The active layout's KLID, published <b>beside</b> layout.txt rather than inside it - the whole
        /// of <c>LAYOUT-SIGNAL</c> rule 2, in one line of code. The
        /// extension reads the first four characters of layout.txt as the code, so appending anything
        /// to that file would break every already-installed copy of it - and the extension is published
        /// on its own clock, so old copies are the normal case, not the edge one. A second file is
        /// additive: an extension that does not know about it behaves exactly as before, and one that
        /// does gets the layout's own shade of the language colour.
        /// </summary>
        internal const string KlidFileName = "layout-klid.txt";

        /// <summary>
        /// The one decision of where the channel lives (rule 1), shared with <see cref="EditorCaretSignal"/>
        /// so the claim is always read from the folder the code is written to.
        /// </summary>
        internal static readonly string Folder = DataFolder.Current;

        /// <summary>
        /// The deprecated machine-wide copy of both files (packaged only, null otherwise). Its writes fail
        /// on a machine where another account created the files first - which is why it is only a mirror.
        /// </summary>
        internal static readonly string? MirrorFolder = DataFolder.LegacyShared;

        private static readonly Channel Default = new Channel(Folder, MirrorFolder);

        /// <summary>Returns at once - the caller is the UI thread; the write happens on a worker.</summary>
        public static void Publish(string code, string? klid = null) => Default.Publish(code, klid);

        /// <summary>Retries publishing the pending layout if a previous write failed (e.g. sharing violation).</summary>
        public static void RetryIfPending() => Default.RetryIfPending();

        /// <summary>
        /// Deletes both files on a clean exit (rule 6). Only the primary instance gets here: the
        /// context's session-end sequence and <c>Program.Fatal</c> - the <c>/launcher-run</c> forwarding
        /// process and the one-shot launch return before those handlers are installed, so neither can
        /// delete the live instance's files. Idempotent; later publishes are ignored.
        /// </summary>
        public static void Retract() => Default.Retract();

        /// <summary>The synchronous write, the body of every publish. Swallows every failure.</summary>
        internal static void WriteNow(string folder, string code, string klid) => TryWriteNow(folder, code, klid);

        internal static bool TryWriteNow(string folder, string code, string klid)
        {
            try
            {
                Directory.CreateDirectory(folder);
                bool ok1 = WriteFileAtomic(folder, CodeFileName, code);
                bool ok2 = WriteFileAtomic(folder, KlidFileName, klid);
                return ok1 && ok2;
            }
            catch
            {
                // Best-effort - never let publishing affect the app.
                return false;
            }
        }

        private static bool WriteFileAtomic(string folder, string fileName, string content)
        {
            string targetPath = Path.Combine(folder, fileName);
            string tempPath = Path.Combine(folder, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tempPath, content);
                if (File.Exists(targetPath))
                {
                    File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
                return true;
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        /// <summary>
        /// One folder's writer. A single drain worker writes whatever is pending until nothing newer is,
        /// so two publishes can never race each other and leave an older value - or a code and a KLID from
        /// two different states - on disk (rule 5). The producer writes only on change, so a stale value
        /// would not heal on the next poll the way the rule assumes.
        /// </summary>
        internal sealed class Channel
        {
            private readonly string _folder;
            private readonly string? _mirror;
            private readonly object _gate = new object();
            private string? _pendingCode;
            private string _pendingKlid = "";
            private bool _draining;
            private bool _retracted;

            internal Channel(string folder, string? mirror = null)
            {
                _folder = folder;
                _mirror = mirror;
            }

            internal void Publish(string code, string? klid)
            {
                lock (_gate)
                {
                    if (_retracted)
                        return;
                    _pendingCode = code ?? "";
                    _pendingKlid = klid ?? "";
                    if (_draining)
                        return; // the running worker picks the newest value up before it stops
                    _draining = true;
                }
                ThreadPool.QueueUserWorkItem(_ => Drain());
            }

            internal void RetryIfPending()
            {
                lock (_gate)
                {
                    if (_retracted || _pendingCode == null || _draining)
                        return;
                    _draining = true;
                }
                ThreadPool.QueueUserWorkItem(_ => Drain());
            }

            private void Drain()
            {
                while (true)
                {
                    string code, klid;
                    lock (_gate)
                    {
                        if (_pendingCode == null || _retracted)
                        {
                            _draining = false;
                            Monitor.PulseAll(_gate);
                            if (_retracted)
                                DeleteFiles();
                            return;
                        }
                        code = _pendingCode;
                        klid = _pendingKlid;
                        _pendingCode = null;
                    }
                    bool ok = TryWriteNow(_folder, code, klid); // outside the lock: a publish never waits on the disk
                    if (_mirror != null)
                        TryWriteNow(_mirror, code, klid); // its own try: a mirror another account owns costs only itself

                    if (!ok)
                    {
                        lock (_gate)
                        {
                            if (!_retracted && _pendingCode == null)
                            {
                                _pendingCode = code;
                                _pendingKlid = klid;
                            }
                            _draining = false;
                            Monitor.PulseAll(_gate);
                            if (_retracted)
                                DeleteFiles();
                            return;
                        }
                    }
                }
            }

            /// <summary>Waits until no write is pending or running; false on timeout.</summary>
            internal bool Flush(TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow + timeout;
                lock (_gate)
                {
                    while (_draining)
                    {
                        TimeSpan left = deadline - DateTime.UtcNow;
                        if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                            return !_draining;
                    }
                    return true;
                }
            }

            internal void Retract()
            {
                lock (_gate)
                {
                    _retracted = true;
                    _pendingCode = null;
                }
                // A write already in flight would otherwise recreate the files after the delete.
                Flush(TimeSpan.FromMilliseconds(250));
                DeleteFiles();
                // editor-caret.txt is the extension's claim, never ours to delete (VERSIONING section 4
                // rule 5: absence is not authority to destroy).
            }

            private void DeleteFiles()
            {
                TryDelete(Path.Combine(_folder, CodeFileName));
                TryDelete(Path.Combine(_folder, KlidFileName));
                if (_mirror != null)
                {
                    TryDelete(Path.Combine(_mirror, CodeFileName));
                    TryDelete(Path.Combine(_mirror, KlidFileName));
                }
            }

            private static void TryDelete(string path)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Best-effort, like every other write of this channel.
                }
            }
        }
    }
}
