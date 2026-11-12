using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0096: the registry compares a companion window's real WS_EX_TOPMOST bit with the policy, so a pin
    /// written while a dialog is up, and a handle rebuilt by a right-to-left layout, cannot leave the window
    /// above the dialog the registry believes it has stepped aside for. Real windows, off screen and shown
    /// without activation. Nothing here touches the registry's dialog state, which other tests may share: a
    /// traffic light registered as unpinned never wants topmost, whatever is active.
    /// </summary>
    [Collection(CompanionSurfaceStateCollection.Name)]   // the signal tests move the registry's dialog state, which lowers any registered window
    public class CompanionSurfaceReconcileTests
    {
        private sealed class Probe : Form
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

        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true); action(); } catch (Exception e) { failure = e; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
        }

        private static bool IsTopmost(Form form) =>
            (WindowInterop.GetWindowLong(form.Handle, WindowInterop.GWL_EXSTYLE) & WindowInterop.WS_EX_TOPMOST) != 0;

        /// <summary>
        /// The pin switch: <c>Form.TopMost = true</c>. WinForms applies it with a SetWindowPos that does not pass
        /// SWP_NOACTIVATE, and whether Windows lets a window of a process that may not take the foreground (a test host
        /// after its first window, a runner without an active session) become topmost that way depends on the machine:
        /// the property then reads true while the bit stays off. This is the staging, not what is judged - so when the
        /// property did not raise the window, the window is raised the same way without activation, and the tests
        /// judge what the registry does with it.
        /// </summary>
        private static void Pin(Form form)
        {
            form.TopMost = true;
            if (!IsTopmost(form))
                WindowInterop.SetWindowPos(form.Handle, WindowInterop.HWND_TOPMOST, 0, 0, 0, 0,
                    WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        }

        /// <summary>Runs the thread's posted messages (the registry defers its rebuilt-handle reconcile by one turn).</summary>
        private static bool PumpUntil(Func<bool> done)
        {
            for (int i = 0; i < 100 && !done(); i++)
            {
                Application.DoEvents();
                if (!done()) Thread.Sleep(10);
            }
            return done();
        }

        [Fact]
        public void ReconcileMovesTheRealBitBothWaysAndOnlyWhenItDiffers()
        {
            Sta(() =>
            {
                using (var form = new Probe())
                {
                    form.Show();
                    Pin(form);   // the pin switch: the window is raised (see Pin)
                    Assert.True(IsTopmost(form));

                    Assert.False(CompanionSurfaceRegistry.Reconcile(form, shouldBeTopmost: true));
                    Assert.True(CompanionSurfaceRegistry.Reconcile(form, shouldBeTopmost: false));
                    Assert.False(IsTopmost(form));

                    Assert.False(CompanionSurfaceRegistry.Reconcile(form, shouldBeTopmost: false));
                    Assert.True(CompanionSurfaceRegistry.Reconcile(form, shouldBeTopmost: true));
                    Assert.True(IsTopmost(form));
                }
            });
        }

        [Fact]
        public void ReconcileLeavesAWindowWithoutAHandleAlone()
        {
            Sta(() =>
            {
                using (var form = new Probe())
                {
                    Assert.False(CompanionSurfaceRegistry.Reconcile(form, shouldBeTopmost: false));
                    Assert.False(form.IsHandleCreated);
                }
            });
        }

        [Fact]
        public void APinWrittenWhileTheWindowIsYieldedIsLoweredAgain()
        {
            Sta(() =>
            {
                using (var form = new Probe())
                {
                    form.Show();
                    Pin(form);
                    CompanionSurfaceRegistry.RegisterCompanion(form, CompanionSurfaceKind.TrafficLight, () => false);
                    try
                    {
                        Assert.False(IsTopmost(form));

                        // TrafficLightWindow.ApplyPinned: Form.TopMost raises the real window whatever the registry
                        // has done to it, then the registry is asked to apply its policy.
                        Pin(form);
                        Assert.True(IsTopmost(form));
                        CompanionSurfaceRegistry.ApplyToForm(form);

                        Assert.False(IsTopmost(form));
                    }
                    finally { CompanionSurfaceRegistry.UnregisterCompanion(form); }
                }
            });
        }

        [Fact]
        public void ARebuiltHandleIsLoweredOnTheNextTurn()
        {
            Sta(() =>
            {
                using (var form = new Probe())
                {
                    form.Show();
                    Pin(form);
                    CompanionSurfaceRegistry.RegisterCompanion(form, CompanionSurfaceKind.TrafficLight, () => false);
                    try
                    {
                        Assert.False(IsTopmost(form));
                        IntPtr before = form.Handle;

                        // WindowScript.Apply for ar / ur: WinForms rebuilds the window and gives the new one
                        // WS_EX_TOPMOST back from Form.TopMost.
                        form.RightToLeft = RightToLeft.Yes;
                        form.RightToLeftLayout = true;
                        Assert.NotEqual(before, form.Handle);
                        Assert.True(IsTopmost(form));

                        Assert.True(PumpUntil(() => !IsTopmost(form)), "the registry never lowered the rebuilt window");
                    }
                    finally { CompanionSurfaceRegistry.UnregisterCompanion(form); }
                }
            });
        }
    }
}
