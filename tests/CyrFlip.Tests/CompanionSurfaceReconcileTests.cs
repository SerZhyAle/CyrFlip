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
                    form.TopMost = true;   // the pin switch: WinForms raises the window itself
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
                    form.TopMost = true;
                    CompanionSurfaceRegistry.RegisterCompanion(form, CompanionSurfaceKind.TrafficLight, () => false);
                    try
                    {
                        Assert.False(IsTopmost(form));

                        // TrafficLightWindow.ApplyPinned: Form.TopMost raises the real window whatever the registry
                        // has done to it, then the registry is asked to apply its policy.
                        form.TopMost = true;
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
                    form.TopMost = true;
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
