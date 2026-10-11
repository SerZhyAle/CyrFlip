using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0061 (LK, spec 5.5 item 2) - the window-lifetime gate. A tray app that lives for weeks cannot
    /// afford a window that is closed and yet kept alive: by a static event, a timer nobody disposed, a
    /// handler on a long-lived service (the history and notes services outlive every window that
    /// subscribes to them). Each window and dialog the theme walk already enumerates
    /// (<see cref="ThemeCoverageTests"/>, which also fails on a form it does not know) is built, its
    /// handle and every child handle created, disposed - and must then be collectable.
    ///
    /// Decidable without a desktop, so it runs in CI. What it deliberately does not do is count GDI or
    /// USER handles: xUnit runs other collections in parallel in this process, so a handle delta here
    /// would measure them. The handle counts are the soak scenes of the rig (SC13 / SC17 / SC38).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class WindowLifetimeTests
    {
        private static readonly List<Form> Rooted = new List<Form>();   // for the guard fact only

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        /// <summary>Builds, creates every handle, disposes - in a frame of its own so no local keeps the form alive.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildAndRelease(Func<Form> make, bool keepRooted)
        {
            Form form = make();
            _ = form.Handle;
            foreach (Control child in Everything(form))
                _ = child.Handle;
            if (keepRooted) Rooted.Add(form);
            form.Dispose();
            return new WeakReference(form);
        }

        private static IEnumerable<Control> Everything(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control inner in Everything(child))
                    yield return inner;
            }
        }

        private static bool Collected(WeakReference weak)
        {
            for (int round = 0; round < 5 && weak.IsAlive; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Application.DoEvents();
            }
            return !weak.IsAlive;
        }

        [Fact]
        public void EveryWindowIsCollectableOnceItIsDisposed()
        {
            OnSta(() =>
            {
                using var windows = new ThemeCoverageTests();
                var survivors = new List<string>();
                int built = 0;
                foreach ((string name, Func<Form> make) in windows.Windows())
                {
                    WeakReference weak = BuildAndRelease(make, keepRooted: false);
                    built++;
                    if (!Collected(weak)) survivors.Add(name);
                }
                Assert.True(built >= 15, "the walk enumerated only " + built + " windows");
                Assert.True(survivors.Count == 0,
                    "Disposed and still reachable (something holds the window - a static event, a service handler, a timer):"
                    + Environment.NewLine + string.Join(Environment.NewLine, survivors));
            });
        }

        /// <summary>
        /// The guard fact (a check that cannot fail is a finding): a window deliberately kept in a static
        /// list is reported as alive by the same probe - otherwise a green run above would prove nothing.
        /// </summary>
        [Fact]
        public void TheProbeSeesAWindowThatIsKeptAlive()
        {
            OnSta(() =>
            {
                WeakReference leaked = BuildAndRelease(() => new Form(), keepRooted: true);
                try { Assert.False(Collected(leaked), "a rooted window was reported collectable"); }
                finally { Rooted.Clear(); }

                WeakReference freed = BuildAndRelease(() => new Form(), keepRooted: false);
                Assert.True(Collected(freed), "an unreferenced disposed window was reported alive");
            });
        }
    }
}
