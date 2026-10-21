using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Signs this device in to an account on an exchange server, or signs it in again (ticket S0074,
    /// <c>DEVICE-EXCHANGE</c> items AD, BN and AT). CyrFlip only <b>logs in</b>: there is no "create account" here -
    /// the registration is the server's, and the person is told to ask its host for an invitation.
    ///
    /// <para>The order is the contract's. Reading the text never connects (BN). The destination - the
    /// <c>host:port</c> the string names - is shown before the password field takes the focus. The certificate is
    /// looked at <b>before</b> a password exists: a chain Windows trusts pins nothing; a pinned leaf must match; a
    /// self-signed one is shown by its fingerprint and pinned only when the person says so (item D). Only then is the
    /// password sent, to that endpoint and to no other, in the one <c>enroll</c> exchange, and cleared.</para>
    /// </summary>
    internal sealed class ExchangeAccountDialog : ThemedForm
    {
        private readonly string _uiLanguage;
        private readonly ExchangeAccountEntry? _existing;
        private readonly IReadOnlyList<ExchangeAccountEntry> _others;
        private readonly Font? _ownFont;
        private readonly Func<string, string> _t;

        private readonly TextBox _connBox = new TextBox();
        private readonly TextBox _loginBox = new TextBox();
        private readonly TextBox _passwordBox = new TextBox { UseSystemPasswordChar = true };
        private readonly Label _destination = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 2) };
        private readonly Label _labelHint = new Label { AutoSize = true, Margin = new Padding(3, 0, 3, 2) };
        private readonly Label _problem = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 2) };
        private readonly Label _details = new Label { AutoSize = true, Margin = new Padding(3, 0, 3, 2) };
        private readonly ExchangeMarkPicture _mark;
        private readonly Button _nextHint;
        private readonly Button _ok;
        private readonly Button _cancel;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private ExchangeConnectionParse? _parsed;
        private ExchangePin _acceptedPin = ExchangePin.None;
        private bool _pinDecided;
        private bool _busy;
        private IReadOnlyList<string> _ladder = Array.Empty<string>();
        private int _ladderAt;

        /// <summary>The entry made or signed in again when the dialog ends with OK; its secrets are already in the store.</summary>
        public ExchangeAccountEntry? ResultEntry { get; private set; }

        public ExchangeAccountDialog(string uiLanguage, IReadOnlyList<ExchangeAccountEntry> others, ExchangeAccountEntry? existing = null)
        {
            HelpLanguage = uiLanguage;
            _uiLanguage = uiLanguage;
            _others = others;
            _existing = existing;
            _t = s => Localization.Translate(uiLanguage, s);

            Text = _t(existing != null ? "Sign in again" : "Add an account on an exchange server");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch (ArgumentException) { /* the font is missing on this machine - keep the default */ }
            }

            int fieldWidth = TextWidth(new string('W', 44));
            _connBox.Width = fieldWidth;
            _loginBox.Width = fieldWidth;
            _passwordBox.Width = fieldWidth;
            int textWidth = fieldWidth;
            _problem.MaximumSize = new Size(textWidth, 0);
            _details.MaximumSize = new Size(textWidth, 0);
            _destination.MaximumSize = new Size(textWidth, 0);
            _labelHint.MaximumSize = new Size(textWidth, 0);
            _problem.ForeColor = ThemePalette.Light.Danger;
            _details.ForeColor = ThemePalette.Light.TextMuted;
            _labelHint.ForeColor = ThemePalette.Light.TextMuted;

            var column = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
            };

            var intro = new Label
            {
                Text = existing != null
                    ? _t("Type the password of this account. CyrFlip keeps no password, only a key from the server.")
                    : _t("CyrFlip only signs in to an existing account. To get one, ask whoever runs the server for an invitation.")
                      + "\n" + _t("Paste the connection string or the text of its QR code."),
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = new Padding(3, 0, 3, 8),
            };
            // The screen where an account on the server is added or signed in again wears the mark at the top of its
            // body, beside the intro (EXCHANGE-MARK rule 8 (a), S0079 M3); the caption bar keeps the program's icon.
            // The intro gives up the mark's width, so the dialog is no wider than the fields under it.
            _mark = new ExchangeMarkPicture(32) { Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(3, 0, 10, 8) };
            _mark.NameIn(_t);
            intro.MaximumSize = new Size(Math.Max(1, textWidth - _mark.DeviceSize - _mark.Margin.Horizontal), 0);
            var introRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
            };
            introRow.Controls.Add(_mark);
            introRow.Controls.Add(intro);
            column.Controls.Add(introRow);

            if (existing == null)
            {
                column.Controls.Add(Caption("Connection string or QR text:"));
                column.Controls.Add(_connBox);
                column.Controls.Add(_destination);
                column.Controls.Add(_labelHint);
                _destination.Text = "";
                _connBox.TextChanged += (_, _) => OnConnectionTextChanged();
            }
            else
            {
                column.Controls.Add(Caption("Server:"));
                column.Controls.Add(new Label { Text = existing.Address, AutoSize = true, Margin = new Padding(3, 0, 3, 6) });
                string why = existing.SignIn == ExchangeSignInState.SignedOut ? ExchangeCodes.SignedOut
                           : existing.SignIn == ExchangeSignInState.DeviceRemoved ? ExchangeCodes.DeviceRevoked
                           : ExchangeCodes.BadCredentials;
                ExchangeMessage reason = ExchangeMessages.Describe(why, ExchangeContext.Enroll, _t);
                column.Controls.Add(new Label { Text = reason.Text, AutoSize = true, MaximumSize = new Size(textWidth, 0), Margin = new Padding(3, 0, 3, 8) });
                _loginBox.Text = existing.Login;
                _loginBox.ReadOnly = true;
            }

            column.Controls.Add(Caption("Login:"));
            column.Controls.Add(_loginBox);
            column.Controls.Add(Caption("Password:"));
            column.Controls.Add(_passwordBox);
            column.Controls.Add(_problem);
            column.Controls.Add(_details);

            _nextHint = new Button { Text = _t("Another thing to try"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false, Margin = new Padding(3, 4, 3, 4) };
            _nextHint.Click += (_, _) => ShowNextStep();
            column.Controls.Add(_nextHint);

            _ok = ButtonGlyphs.Answer(new Button
            {
                Text = _t(existing != null ? "Sign in" : "Add"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0),
                Margin = new Padding(4, 3, 0, 3),
            }, existing != null ? AppGlyphs.Confirm : AppGlyphs.Add);
            _ok.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);
            _cancel = ButtonGlyphs.Answer(new Button
            {
                Text = _t("Cancel"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0),
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(4, 3, 0, 3),
            });
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0),
            };
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_ok);
            column.Controls.Add(buttons);

            Controls.Add(column);
            AcceptButton = _ok;
            CancelButton = _cancel;
            Shown += (_, _) =>
            {
                ForegroundActivator.Activate(this);
                if (existing != null) _passwordBox.Focus(); else _connBox.Focus();
            };
            FormClosing += (_, _) => _lifetime.Cancel();
        }

        protected override void OnDpiChanged(int oldDpi, int newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _mark.Rescale();
        }

        private Label Caption(string source) => new Label { Text = _t(source), AutoSize = true, Margin = new Padding(3, 4, 3, 2) };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        // ---- reading the string never connects (BN) ----

        private void OnConnectionTextChanged()
        {
            _pinDecided = false;
            _acceptedPin = ExchangePin.None;
            SetProblem(null);
            string raw = _connBox.Text;
            if (raw.Trim().Length == 0)
            {
                _parsed = null;
                _destination.Text = "";
                _labelHint.Text = "";
                return;
            }

            _parsed = ExchangeConnectionString.Parse(raw, keepLoginHint: true);
            if (!_parsed.Ok)
            {
                _destination.Text = "";
                _labelHint.Text = "";
                SetProblemText(ReasonText(_parsed));
                return;
            }

            // the destination first (BN): the one place the password will go
            _destination.Text = Localization.Format(_t, "The password will go to {0}.", TrafficLightAddress.JoinHostPort(_parsed.Host, _parsed.Port));
            _labelHint.Text = _parsed.Name.Length > 0 ? Localization.Format(_t, "Label in the string (not verified): {0}", _parsed.Name) : "";
            if (_parsed.LoginHint.Length > 0 && _loginBox.Text.Length == 0) _loginBox.Text = _parsed.LoginHint;
        }

        private string ReasonText(ExchangeConnectionParse parse)
        {
            if (parse.Reason == "newer-major") return _t(ExchangeMessages.UnsupportedFormatText);
            if (parse.Reason == "not-a-string")
                return parse.Looks.Length > 0 ? _t("This is not a server connection string.") : _t("This is not a connection string. Paste the one the server's host gave you.");
            return _t("This connection string is damaged or incomplete. Ask whoever runs the server for a new one.");
        }

        // ---- the sign-in ----

        private async Task SubmitAsync()
        {
            if (_busy) return;
            SetProblem(null);

            string login = _loginBox.Text.Trim();
            string password = _passwordBox.Text;
            if (_existing == null && (_parsed == null || !_parsed.Ok))
            {
                SetProblemText(_parsed != null ? ReasonText(_parsed) : _t("This is not a connection string. Paste the one the server's host gave you."));
                _connBox.Focus();
                return;
            }
            if (login.Length == 0 || password.Length == 0)
            {
                SetProblem(ExchangeCodes.EmptyCredential); // refused locally, before the wire (item BR)
                (login.Length == 0 ? _loginBox : _passwordBox).Focus();
                return;
            }

            string host = _existing != null ? _existing.Host : _parsed!.Host;
            int port = _existing != null ? _existing.Port : _parsed!.Port;
            SetBusy(true);
            try
            {
                var client = new ExchangeClient();
                CancellationToken ct = _lifetime.Token;

                // 1. trust, before a password exists (AD step 2)
                ExchangePin pin = _existing != null ? _existing.Pin : (_pinDecided ? _acceptedPin : new ExchangePin(_parsed!.Fingerprint, ""));
                if (_existing == null && !_pinDecided)
                {
                    ExchangeOutcome<ExchangeTrustResult> probe = await client.ProbeAsync(host, port, pin, ct).ConfigureAwait(true);
                    if (!probe.Ok) { SetProblem(probe.Code, host); return; }
                    ExchangeTrustResult trust = probe.Value!;
                    switch (trust.Kind)
                    {
                        case ExchangeTrustKind.Platform:
                            pin = ExchangePin.None;
                            break;
                        case ExchangeTrustKind.PinMatched:
                        case ExchangeTrustKind.PinRenewed:
                            pin = new ExchangePin(trust.ObservedLeaf, trust.ObservedKey); // the key value comes from the leaf that matched
                            break;
                        case ExchangeTrustKind.Changed:
                            SetProblem(ExchangeCodes.CertificateChanged, host,
                                Localization.Format(_t, "Pinned: {0}\nThe server presents: {1}", pin.Leaf, trust.ObservedLeaf));
                            return;
                        default: // NeedsAcceptance
                            if (!ConfirmFingerprint(trust.ObservedLeaf)) { SetProblem(ExchangeCodes.CertificateChanged, host); return; }
                            pin = new ExchangePin(trust.ObservedLeaf, trust.ObservedKey);
                            break;
                    }
                    _acceptedPin = pin;
                    _pinDecided = true;
                }

                // 2. the one enroll: the password goes to this endpoint and to no other
                ExchangeAccountEntry? same = _existing ?? ExchangeEnrollment.FindSameIdentity(_others, host, port, login);
                ExchangeOutcome<ExchangeAccountEntry> joined = await ExchangeEnrollment.JoinAsync(client, new ExchangeJoinRequest
                {
                    Host = host,
                    Port = port,
                    Pin = pin,
                    Login = login,
                    Password = password,
                    DisplayName = _parsed?.Name ?? "",
                    DeviceName = Environment.MachineName,
                }, same, ct).ConfigureAwait(true);

                if (!joined.Ok)
                {
                    if (joined.Trust != null && joined.Trust.Kind == ExchangeTrustKind.Changed)
                        SetProblem(joined.Code, host, Localization.Format(_t, "Pinned: {0}\nThe server presents: {1}", pin.Leaf, joined.Trust.ObservedLeaf));
                    else
                        SetProblem(joined.Code, host);
                    return;
                }

                ResultEntry = joined.Value;
                DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException)
            {
                // the dialog was closed while the call was running
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // never let a bug in a network call take the settings window down; the type goes to the log, no text
                ExchangeClipboardLog.Fault("enroll-failed", ex);
                if (!IsDisposed) SetProblem(ExchangeCodes.Unanswered);
            }
            finally
            {
                _passwordBox.Clear(); // the password lives in the field and the one exchange, then it is gone
                if (!IsDisposed) SetBusy(false);
            }
        }

        private bool ConfirmFingerprint(string fingerprint)
        {
            string message = _t("Windows does not recognize the certificate of this server. A server of your own usually has one it made itself.")
                + "\n\n" + Localization.Format(_t, "Its fingerprint is:\n\n{0}", fingerprint)
                + "\n\n" + _t("Compare it with the fingerprint the host of the server gave you. Trust this server?");
            return ConfirmDialog.Show(this, _uiLanguage, message, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) == DialogResult.Yes;
        }

        // ---- what a failure shows: what happened, then what to try; the code in the details line (DA) ----

        private void SetProblem(string? code, string? host = null, string? extra = null)
        {
            _ladder = Array.Empty<string>();
            _ladderAt = 0;
            _nextHint.Visible = false;
            if (code == null)
            {
                _problem.Text = "";
                _details.Text = "";
                return;
            }

            ExchangeMessage m = ExchangeMessages.Describe(code, ExchangeContext.Enroll, _t);
            _problem.Text = m.Text;
            _details.Text = Localization.Format(_t, "Details: {0}", m.Details) + (extra != null ? "\n" + extra : "");
            if (m.StepId.Length > 0)
            {
                _ladder = ExchangeMessages.Ladder(m.StepId, ExchangeMessages.IsOwnNetwork(host ?? _existing?.Host ?? _parsed?.Host ?? ""));
                _nextHint.Visible = _ladder.Count > 1;
            }
        }

        private void SetProblemText(string text)
        {
            _ladder = Array.Empty<string>();
            _nextHint.Visible = false;
            _problem.Text = text;
            _details.Text = "";
        }

        /// <summary>Walks the ladder of item CY one step at a time, never as a wall of text.</summary>
        private void ShowNextStep()
        {
            if (_ladder.Count < 2) return;
            _ladderAt = (_ladderAt + 1) % _ladder.Count;
            string head = _problem.Text;
            int cut = head.IndexOf('\n');
            if (cut >= 0) head = head.Substring(0, cut);
            _problem.Text = head + "\n" + _t(ExchangeMessages.StepText(_ladder[_ladderAt]));
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _ok.Enabled = !busy;
            _connBox.Enabled = !busy;
            _loginBox.Enabled = !busy;
            _passwordBox.Enabled = !busy;
            UseWaitCursor = busy;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _lifetime.Dispose();
                _ownFont?.Dispose();
            }
        }
    }
}
