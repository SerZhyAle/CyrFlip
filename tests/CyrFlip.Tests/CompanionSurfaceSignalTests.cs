using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The registry's signals state is process-wide, so the tests that move it - and the ones whose windows
    /// it would move - run alone, one after another.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CompanionSurfaceStateCollection
    {
        public const string Name = "CompanionSurfaceState";
    }

    /// <summary>
    /// S0098: the registry learns that a dialog of ours is active, and that a combo box list is open, from the
    /// notifications Windows <i>sends</i> - <c>WM_ACTIVATE</c> to the window and <c>CBN_DROPDOWN</c> /
    /// <c>CBN_CLOSEUP</c> to the combo's parent. The message filter that was meant to see them only gets posted
    /// messages and never did. Every test sends the real message with <c>SendMessage</c>, which is exactly the
    /// kind the filter could not see, so a regression to a filter fails here. Windows are off screen and shown
    /// without activation: the foreground is never taken.
    /// </summary>
    [Collection(CompanionSurfaceStateCollection.Name)]
    public class CompanionSurfaceSignalTests
    {
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_COMMAND = 0x0111;
        private const int WA_INACTIVE = 0;
        private const int WA_ACTIVE = 1;
        private const int CBN_DROPDOWN = 7;
        private const int CBN_CLOSEUP = 8;

        private class Probe : Form
        {
            public Probe()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(40, 40);
            }

            protected override bool ShowWithoutActivation => true;
        }

        /// <summary>A window like every one of ours: a <see cref="ThemedForm"/> that is not a companion.</summary>
        private sealed class DialogProbe : ThemedForm
        {
            public DialogProbe()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(40, 40);
            }

            protected override bool ShowWithoutActivation => true;
        }

        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                    CompanionSurfaceRegistry.SetDialogActive(false);
                    CompanionSurfaceRegistry.SetDropdownDropped(false);
                    action();
                }
                catch (Exception e) { failure = e; }
                finally
                {
                    CompanionSurfaceRegistry.SetDialogActive(false);
                    CompanionSurfaceRegistry.SetDropdownDropped(false);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
        }

        private static bool IsTopmost(Form form) =>
            (WindowInterop.GetWindowLong(form.Handle, WindowInterop.GWL_EXSTYLE) & WindowInterop.WS_EX_TOPMOST) != 0;

        /// <summary>Runs the thread's posted messages (the registry reads the foreground one turn after a window leaves).</summary>
        private static bool PumpUntil(Func<bool> done)
        {
            for (int i = 0; i < 100 && !done(); i++)
            {
                Application.DoEvents();
                if (!done()) Thread.Sleep(10);
            }
            return done();
        }

        private static void SendActivate(Form form, int state) =>
            WindowInterop.SendMessage(form.Handle, WM_ACTIVATE, (IntPtr)state, IntPtr.Zero);

        private static void SendComboNotify(Form parent, ComboBox combo, int notification) =>
            WindowInterop.SendMessage(parent.Handle, WM_COMMAND, (IntPtr)(notification << 16), combo.Handle);

        /// <summary>A pinned traffic light on screen: topmost until the registry says a dialog or a list is up.</summary>
        private static Probe PinnedCompanion()
        {
            var companion = new Probe();
            companion.Show();
            companion.TopMost = true;
            CompanionSurfaceRegistry.RegisterCompanion(companion, CompanionSurfaceKind.TrafficLight, () => true);
            Assert.True(IsTopmost(companion));
            return companion;
        }

        [Fact]
        public void ASentActivationOfAWindowThatIsNotACompanionLowersThePinnedCompanion()
        {
            Sta(() =>
            {
                using (var companion = PinnedCompanion())
                using (var dialog = new DialogProbe())
                {
                    dialog.Show();
                    try
                    {
                        Assert.False(CompanionSurfaceRegistry.DialogActive);

                        SendActivate(dialog, WA_ACTIVE);
                        Assert.True(CompanionSurfaceRegistry.DialogActive);
                        Assert.False(IsTopmost(companion));

                        SendActivate(dialog, WA_INACTIVE);
                        Assert.True(PumpUntil(() => !CompanionSurfaceRegistry.DialogActive),
                            "the registry never noticed the dialog leaving");
                        Assert.True(IsTopmost(companion));
                    }
                    finally { CompanionSurfaceRegistry.UnregisterCompanion(companion); }
                }
            });
        }

        [Fact]
        public void AWindowClosedWhileActiveIsReadAgain()
        {
            Sta(() =>
            {
                using (var companion = PinnedCompanion())
                {
                    try
                    {
                        var dialog = new DialogProbe();
                        dialog.Show();
                        SendActivate(dialog, WA_ACTIVE);
                        Assert.True(CompanionSurfaceRegistry.DialogActive);

                        dialog.Close();   // no Deactivate guaranteed: the window is simply gone
                        Assert.True(PumpUntil(() => !CompanionSurfaceRegistry.DialogActive),
                            "the registry kept the closed dialog as the active window");
                        Assert.True(IsTopmost(companion));
                    }
                    finally { CompanionSurfaceRegistry.UnregisterCompanion(companion); }
                }
            });
        }

        [Fact]
        public void ASentComboNotificationLowersAndRaisesThePinnedCompanion()
        {
            Sta(() =>
            {
                using (var companion = PinnedCompanion())
                using (var host = new Probe())
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    host.Controls.Add(combo);
                    host.Show();
                    // The theme walk is what meets every combo box of every window.
                    ThemeApply.Apply(host, ThemePalette.Light);
                    try
                    {
                        Assert.False(CompanionSurfaceRegistry.DropdownDropped);

                        SendComboNotify(host, combo, CBN_DROPDOWN);
                        Assert.True(CompanionSurfaceRegistry.DropdownDropped);
                        Assert.False(IsTopmost(companion));

                        SendComboNotify(host, combo, CBN_CLOSEUP);
                        Assert.False(CompanionSurfaceRegistry.DropdownDropped);
                        Assert.True(IsTopmost(companion));
                    }
                    finally { CompanionSurfaceRegistry.UnregisterCompanion(companion); }
                }
            });
        }

        [Fact]
        public void AComboBoxDestroyedWithItsListOpenDoesNotLeaveTheFlagUp()
        {
            Sta(() =>
            {
                var host = new Probe();
                try
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    host.Controls.Add(combo);
                    host.Show();
                    ThemeApply.Apply(host, ThemePalette.Light);

                    SendComboNotify(host, combo, CBN_DROPDOWN);
                    Assert.True(CompanionSurfaceRegistry.DropdownDropped);
                }
                finally { host.Dispose(); }

                Assert.False(CompanionSurfaceRegistry.DropdownDropped);
            });
        }

        [Fact]
        public void OnlyCompanionsAreNotDialogs()
        {
            foreach (string companion in CompanionSurfaceRegistry.CompanionTypeNames)
                Assert.False(CompanionSurfaceRegistry.IsDialogTypeName(companion), companion);

            Assert.True(CompanionSurfaceRegistry.IsDialogTypeName("SettingsForm"));
            Assert.True(CompanionSurfaceRegistry.IsDialogTypeName("HotkeyDialog"));
            Assert.True(CompanionSurfaceRegistry.IsDialogTypeName("ConfirmDialog"));
            Assert.True(CompanionSurfaceRegistry.IsDialogTypeName("ClipboardHistorySearchWindow"));
        }
    }
}
