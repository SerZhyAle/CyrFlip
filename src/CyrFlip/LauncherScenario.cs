using System;
using System.Xml.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The kind of action a launcher scenario performs. The member names are part of the XML
    /// contract inherited from OneClickRunner (&lt;Type&gt;YtDlp&lt;/Type&gt;) - do not rename them.
    /// </summary>
    public enum LauncherScenarioType
    {
        Executable = 0,
        YtDlp = 1,
    }

    /// <summary>
    /// One launcher scenario - a program/script to run or a yt-dlp download. Serialized one file per
    /// scenario, and the XML shape is deliberately identical to OneClickRunner's <c>AppItem</c>
    /// (same root element, same field names), so the two apps read each other's files. The class is
    /// <b>public</b> only because <see cref="XmlSerializer"/> refuses internal types on .NET Framework.
    ///
    /// <see cref="Hotkey"/> is CyrFlip's one extension: an optional global chord that launches the
    /// scenario (empty = none). OneClickRunner ignores the unknown element when reading such a file
    /// but drops it on re-save - compatibility is promised only toward CyrFlip (spec v0.2 §5.1).
    /// </summary>
    [XmlRoot("AppItem")]
    public sealed class LauncherScenario
    {
        /// <summary>Legacy magic path kept working for scenarios created before the yt-dlp type existed.</summary>
        public const string LegacyYtDlpSentinel = "SPECIAL_YTDLP";

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        /// <summary>The on-disk file name. Internal bookkeeping, not part of the export contract.</summary>
        public string Filename { get; set; } = string.Empty;
        public bool RunAsAdmin { get; set; } = false;

        /// <summary>
        /// Explicit position in the editor, tray submenu and Jump List. Lower comes first. Legacy
        /// scenarios deserialize with 0 and get real ordinals on first load (see the store).
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>Scenario kind. Absent in legacy XML =&gt; <see cref="LauncherScenarioType.Executable"/>.</summary>
        public LauncherScenarioType Type { get; set; } = LauncherScenarioType.Executable;

        /// <summary>yt-dlp output folder. Empty =&gt; the user's Downloads folder.</summary>
        public string YtDlpOutputFolder { get; set; } = string.Empty;

        /// <summary>Optional extra yt-dlp arguments (e.g. <c>-f bestvideo+bestaudio</c>). Shown and passed verbatim.</summary>
        public string YtDlpFormat { get; set; } = string.Empty;

        /// <summary>Optional global hotkey ("Ctrl+Alt+F5"); empty = none. CyrFlip extension, see class doc.</summary>
        public string Hotkey { get; set; } = string.Empty;

        /// <summary>True for the yt-dlp type, including legacy files that still carry the magic path.</summary>
        [XmlIgnore]
        public bool IsYtDlp => Type == LauncherScenarioType.YtDlp || Path == LegacyYtDlpSentinel;

        /// <summary>
        /// A full copy of every field. Used by Edit (edit a copy, commit on OK) and Clone - copying
        /// all fields, including Type, Order, the yt-dlp options and the hotkey, so none are dropped.
        /// </summary>
        public LauncherScenario Clone() => new LauncherScenario
        {
            Id = Id,
            Name = Name,
            Path = Path,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            Filename = Filename,
            RunAsAdmin = RunAsAdmin,
            Order = Order,
            Type = Type,
            YtDlpOutputFolder = YtDlpOutputFolder,
            YtDlpFormat = YtDlpFormat,
            Hotkey = Hotkey,
        };
    }
}
