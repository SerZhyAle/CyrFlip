using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0135: what the settings window no longer redoes. Both facts are cheap (no settings window is built):
    /// the hotkeys overview is rebuilt only when the rows it shows changed, and a page built hidden is laid out when it
    /// is first shown, not before.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsOpenSavingsTests
    {
        private static ChordOverviewRow Row(string caption = "Flip case", bool on = true, object? source = null) => new ChordOverviewRow
        {
            Group = ChordGroup.Fixed, Kind = ChordKind.Case, Id = "", Caption = caption, StoredChord = "Ctrl+Shift+F11",
            ChordText = "Ctrl+Shift+F11", State = ChordBindingState.Active, HasSwitch = true, SwitchOn = on, Source = source,
        };

        [Fact]
        public void EqualRowSetsAreTheSameOverview()
        {
            Assert.True(SettingsForm.SameOverview(new List<ChordOverviewRow> { Row(), Row("Clipboard history") },
                                                  new List<ChordOverviewRow> { Row(), Row("Clipboard history") }));
        }

        [Fact]
        public void ADifferenceInAnythingTheRowShowsIsADifferentOverview()
        {
            var baseline = new List<ChordOverviewRow> { Row() };
            Assert.False(SettingsForm.SameOverview(baseline, new List<ChordOverviewRow> { Row(on: false) }));
            Assert.False(SettingsForm.SameOverview(baseline, new List<ChordOverviewRow> { Row("Switch the case") }));
            Assert.False(SettingsForm.SameOverview(baseline, new List<ChordOverviewRow>()));
            ChordOverviewRow moved = Row();
            moved.ChordText = "Ctrl+Shift+F9";
            Assert.False(SettingsForm.SameOverview(baseline, new List<ChordOverviewRow> { moved }));
            ChordOverviewRow clash = Row();
            clash.ClashWith = "Clipboard history";
            Assert.False(SettingsForm.SameOverview(baseline, new List<ChordOverviewRow> { clash }));
        }

        [Fact]
        public void ARowThatCanBeEditedStandsForOneProfileAndAReplacedProfileRebuilds()
        {
            object first = new object(), replacement = new object();
            Assert.True(SettingsForm.SameOverview(new List<ChordOverviewRow> { Row(source: first) }, new List<ChordOverviewRow> { Row(source: first) }));
            Assert.False(SettingsForm.SameOverview(new List<ChordOverviewRow> { Row(source: first) }, new List<ChordOverviewRow> { Row(source: replacement) }));

            // Windows' own rows are read-only and carry a fresh record on every read: their source is not compared.
            ChordOverviewRow a = Row(source: first), b = Row(source: replacement);
            a.ReadOnly = b.ReadOnly = true;
            Assert.True(SettingsForm.SameOverview(new List<ChordOverviewRow> { a }, new List<ChordOverviewRow> { b }));
        }

        [Fact]
        public void APageBuiltHiddenIsLaidOutWhenItIsReleasedAndNotBefore()
        {
            using var form = new Form { ShowInTaskbar = false };
            var page = new SettingsPagePanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            inner.Controls.Add(new Label { Text = "a", AutoSize = true });
            page.Controls.Add(inner);
            form.Controls.Add(page);
            form.Size = new Size(600, 400);
            form.PerformLayout();
            int before = inner.Width;

            page.DeferLayout();
            Assert.True(page.LayoutDeferred);
            // A caption that grows, assigned under the page's deferred layout, must not move the container yet.
            inner.Controls.Add(new Label { Text = new string('W', 40), AutoSize = true });
            Assert.Equal(before, inner.Width);

            page.ReleaseDeferredLayout();
            Assert.False(page.LayoutDeferred);
            Assert.True(inner.Width > before, "the page lays itself out when it is released");

            // A second release, and a control made while the layout was held back, change nothing and throw nothing.
            page.ReleaseDeferredLayout();
        }
    }
}
