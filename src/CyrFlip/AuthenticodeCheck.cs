using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace CyrFlip
{
    /// <summary>
    /// "Is this file signed, is the signature valid, and is it signed by whom we expect?" - asked of
    /// the Ollama installer before it is run (ticket S0010 TD-5). <c>WinVerifyTrust</c> answers the
    /// first two (chain to a trusted root, file unmodified since signing); the signer's subject answers
    /// the third, since a valid signature by <i>anyone</i> proves nothing about what the file is.
    /// </summary>
    internal static class AuthenticodeCheck
    {
        /// <summary>
        /// The signer Ollama ships under. Read on 2026-09-25 from the signed <c>ollama.exe</c> and
        /// <c>ollama app.exe</c> of an installed Ollama (issuer DigiCert G5 CS ECC SHA384 2021 CA1):
        /// <c>CN=Ollama Inc., O=Ollama Inc., L=Toronto, S=Ontario, C=CA, ..</c>. Only the CN and the O
        /// are pinned - the address and serial fields legitimately change when the certificate is renewed.
        /// </summary>
        public const string OllamaPublisher = "Ollama Inc.";

        /// <summary>True when the file carries a valid Authenticode signature whose signer is <paramref name="publisher"/>.</summary>
        public static bool IsSignedBy(string path, string publisher)
        {
            if (!VerifySignature(path)) return false;
            try
            {
                using (var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    return SubjectNames(signer.SubjectName, publisher);
            }
            catch { return false; }
        }

        /// <summary>
        /// The pin, apart from the machine: the subject's CN <b>and</b> O are both
        /// <paramref name="publisher"/>. A lookalike ("Ollama Inc. Ltd", "CN=Ollama Inc., O=Someone")
        /// does not match.
        /// </summary>
        internal static bool SubjectNames(string? subject, string publisher)
        {
            if (string.IsNullOrEmpty(subject)) return false;
            try { return SubjectNames(new X500DistinguishedName(subject), publisher); }
            catch { return false; }
        }

        private static bool SubjectNames(X500DistinguishedName name, string publisher)
        {
            try
            {
                string? cn = null, o = null;
                foreach (string part in name.Decode(X500DistinguishedNameFlags.UseNewLines).Split('\n'))
                {
                    string line = part.Trim('\r', ' ');
                    if (line.StartsWith("CN=", StringComparison.Ordinal)) cn ??= Unquote(line.Substring(3));
                    else if (line.StartsWith("O=", StringComparison.Ordinal)) o ??= Unquote(line.Substring(2));
                }
                return string.Equals(cn, publisher, StringComparison.Ordinal)
                    && string.Equals(o, publisher, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string Unquote(string value)
            => value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"' ? value.Substring(1, value.Length - 2) : value;

        // ---- WinVerifyTrust ----

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid pgActionID, [In, Out] ref WINTRUST_DATA pWVTData);

        /// <summary>
        /// <c>WinVerifyTrust</c> with no UI: 0 means the signature chains to a trusted root and the
        /// file is unmodified. Revocation is not checked online - CyrFlip opens no socket for this.
        /// </summary>
        private static bool VerifySignature(string path)
        {
            IntPtr fileInfo = IntPtr.Zero;
            try
            {
                var file = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                    pcwszFilePath = path,
                };
                fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(file, fileInfo, false);

                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = fileInfo,
                    dwStateAction = WTD_STATEACTION_VERIFY,
                };
                Guid action = GenericVerifyV2;
                int result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

                data.dwStateAction = WTD_STATEACTION_CLOSE;
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                return result == 0;
            }
            catch { return false; }
            finally
            {
                if (fileInfo != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(fileInfo, typeof(WINTRUST_FILE_INFO));
                    Marshal.FreeHGlobal(fileInfo);
                }
            }
        }
    }
}
