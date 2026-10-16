using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The "add layout" picker's keyboard (ticket S0060 P7-l): the arrows walk the list from the filter
    /// box, Enter accepts, Escape cancels - the behaviour the settings search dialog already has.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class LayoutPickerKeyTests
    {
        private static readonly List<InputLayouts.Available> Layouts = new List<InputLayouts.Available>
        {
            new InputLayouts.Available { Klid = "00000407", LangId = 0x0407, LanguageName = "German (Germany)", DisplayName = "German" },
            new InputLayouts.Available { Klid = "0000040C", LangId = 0x040C, LanguageName = "French (France)", DisplayName = "French" },
            new InputLayouts.Available { Klid = "00000410", LangId = 0x0410, LanguageName = "Italian (Italy)", DisplayName = "Italian" },
        };

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
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the UI thread of the test hung");
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        // ---- the pure step ----

        [Theory]
        [InlineData(0, 3, true, 1)]
        [InlineData(1, 3, true, 2)]
        [InlineData(2, 3, true, 2)]   // never past the last row
        [InlineData(2, 3, false, 1)]
        [InlineData(0, 3, false, 0)]  // never before the first
        [InlineData(-1, 3, true, 0)]  // from no selection both arrows land on the first row
        [InlineData(-1, 3, false, 0)]
        [InlineData(-1, 0, true, -1)] // an empty list selects nothing
        [InlineData(0, 0, false, -1)]
        public void AnArrowMovesOneRowAndNeverLeavesTheList(int current, int count, bool down, int expected)
            => Assert.Equal(expected, LayoutPickerDialog.StepSelection(current, count, down));

        // ---- the dialog ----

        [Fact]
        public void TheArrowsWalkTheListFromTheFilterBox()
        {
            OnSta(() =>
            {
                using var picker = new LayoutPickerDialog(new string[0], "English", Layouts);
                Assert.Equal(3, picker.ListForTest.Items.Count);
                Assert.Equal(0, picker.ListForTest.SelectedIndex);

                Assert.True(picker.HandleFilterKey(Keys.Down, Keys.None));
                Assert.Equal(1, picker.ListForTest.SelectedIndex);
                Assert.True(picker.HandleFilterKey(Keys.Down, Keys.None));
                Assert.True(picker.HandleFilterKey(Keys.Down, Keys.None));
                Assert.Equal(2, picker.ListForTest.SelectedIndex);   // clamped at the end
                Assert.True(picker.HandleFilterKey(Keys.Up, Keys.None));
                Assert.Equal(1, picker.ListForTest.SelectedIndex);
            });
        }

        [Fact]
        public void OnlyThePlainArrowsAreTheLists()
        {
            OnSta(() =>
            {
                using var picker = new LayoutPickerDialog(new string[0], "English", Layouts);
                // Typing, Home/End and a modified arrow (select-by-word in the box) stay the box's own.
                Assert.False(picker.HandleFilterKey(Keys.A, Keys.None));
                Assert.False(picker.HandleFilterKey(Keys.Home, Keys.None));
                Assert.False(picker.HandleFilterKey(Keys.Down, Keys.Shift));
                Assert.False(picker.HandleFilterKey(Keys.Up, Keys.Control));
                Assert.Equal(0, picker.ListForTest.SelectedIndex);
            });
        }

        [Fact]
        public void WithNothingMatchingTheArrowsAreLeftToTheBox()
        {
            OnSta(() =>
            {
                using var picker = new LayoutPickerDialog(new string[0], "English", Layouts);
                picker.FilterForTest.Text = "no such layout anywhere";
                Assert.Empty(picker.ListForTest.Items);
                Assert.False(picker.HandleFilterKey(Keys.Down, Keys.None));
            });
        }

        [Fact]
        public void TheFilterBoxKeyDownIsWiredToTheSameHandler()
        {
            OnSta(() =>
            {
                using var picker = new LayoutPickerDialog(new string[0], "English", Layouts);
                // The real event path: the box raises KeyDown, the handler moves the list and swallows the key.
                var args = new KeyEventArgs(Keys.Down);
                var raise = typeof(Control).GetMethod("OnKeyDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                raise.Invoke(picker.FilterForTest, new object[] { args });

                Assert.Equal(1, picker.ListForTest.SelectedIndex);
                Assert.True(args.Handled);
                Assert.True(args.SuppressKeyPress);
            });
        }

        [Fact]
        public void EnterAcceptsTheSelectedRowAndEscapeCancels()
        {
            OnSta(() =>
            {
                // Enter: Down, Down, Enter -> the third layout.
                using (var picker = new LayoutPickerDialog(new string[0], "English", Layouts))
                {
                    picker.Shown += (_, _) => picker.BeginInvoke((MethodInvoker)(() =>
                    {
                        picker.HandleFilterKey(Keys.Down, Keys.None);
                        picker.HandleFilterKey(Keys.Down, Keys.None);
                        picker.AcceptButton!.PerformClick();   // Enter in the filter box is the AcceptButton's click
                    }));
                    Assert.Equal(DialogResult.OK, picker.ShowDialog());
                    Assert.Equal("00000410", picker.SelectedKlid);
                }

                // Escape: the CancelButton's click closes with nothing chosen.
                using (var picker = new LayoutPickerDialog(new string[0], "English", Layouts))
                {
                    picker.Shown += (_, _) => picker.BeginInvoke((MethodInvoker)(() => picker.CancelButton!.PerformClick()));
                    Assert.Equal(DialogResult.Cancel, picker.ShowDialog());
                    Assert.Null(picker.SelectedKlid);
                }
            });
        }

        [Fact]
        public void EnterWithNothingMatchingKeepsTheDialogOpenForAnotherTry()
        {
            OnSta(() =>
            {
                using var picker = new LayoutPickerDialog(new string[0], "English", Layouts);
                bool stillOpenAfterEnter = false;
                picker.Shown += (_, _) => picker.BeginInvoke((MethodInvoker)(() =>
                {
                    picker.FilterForTest.Text = "no such layout anywhere";
                    picker.AcceptButton!.PerformClick();        // Enter on an empty list
                    // A later turn of the message loop, after WinForms has acted on the button's answer.
                    picker.BeginInvoke((MethodInvoker)(() =>
                    {
                        stillOpenAfterEnter = picker.Visible && !picker.IsDisposed;
                        picker.CancelButton!.PerformClick();    // then the way out
                    }));
                }));
                Assert.Equal(DialogResult.Cancel, picker.ShowDialog());
                Assert.True(stillOpenAfterEnter, "Enter with no row chosen closed the dialog as if it had answered");
                Assert.Null(picker.SelectedKlid);
            });
        }
    }
}
