using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The standard order and the search over it. The load-bearing test is the last one: the
    /// incremental insert is proven <b>equal to the full sort it replaces</b> over a thousand random
    /// operations, the same way <see cref="ClipboardHistoryOrderTests"/> pins its own order - an
    /// order maintained a note at a time is exactly the kind of code that is right for a year and
    /// then subtly wrong, and only a differential test notices.
    /// </summary>
    public class QuickNotesOrderTests
    {
        private static QuickNote At(int minute, Guid? id = null) => new QuickNote
        {
            Id = id ?? Guid.NewGuid(),
            CreatedAtUtc = new DateTime(2026, 8, 11, 9, minute, 0, DateTimeKind.Utc),
        };

        [Fact]
        public void NewestFirstByCreation()
        {
            var notes = new List<QuickNote> { At(1), At(9), At(5) };
            QuickNotesOrder.Sort(notes);

            Assert.Equal(new[] { 9, 5, 1 }, notes.ConvertAll(n => n.CreatedAtUtc.Minute).ToArray());
        }

        [Fact]
        public void EditingANoteDoesNotMoveIt()
        {
            QuickNote old = At(1);
            var notes = new List<QuickNote> { At(9), At(5), old };
            QuickNotesOrder.Sort(notes);

            // The only thing an edit changes; the standard order does not look at it at all.
            old.UpdatedAtUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            QuickNotesOrder.Sort(notes);

            Assert.Same(old, notes[2]);
        }

        [Fact]
        public void EqualTimestampsAreBrokenByIdSoTheOrderIsTotal()
        {
            var low = new Guid("00000000-0000-0000-0000-000000000001");
            var high = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var notes = new List<QuickNote> { At(5, low), At(5, high) };

            QuickNotesOrder.Sort(notes);
            Assert.Equal(high, notes[0].Id);

            // Same set the other way round: a total order gives the same answer either way, which is
            // what "the list does not reshuffle itself between launches" actually means.
            notes.Reverse();
            QuickNotesOrder.Sort(notes);
            Assert.Equal(high, notes[0].Id);
        }

        [Fact]
        public void FilterKeepsTheStandardOrderRatherThanRankingByRelevance()
        {
            var notes = new List<QuickNote>();
            for (int minute = 1; minute <= 5; minute++)
            {
                QuickNote note = At(minute);
                note.RawText = minute % 2 == 0 ? "needle " + minute : "nothing";
                notes.Add(note);
            }
            QuickNotesOrder.Sort(notes);

            List<QuickNote> found = QuickNotesOrder.Filter(notes, "NEEDLE");

            Assert.Equal(new[] { 4, 2 }, found.ConvertAll(n => n.CreatedAtUtc.Minute).ToArray());
        }

        [Fact]
        public void AnEmptyQueryReturnsEverythingUntouched()
        {
            var notes = new List<QuickNote> { At(9), At(5), At(1) };
            Assert.Equal(3, QuickNotesOrder.Filter(notes, "").Count);
        }

        /// <summary>
        /// The incremental insert against the reference sort, a thousand times. The timestamps are
        /// drawn from a deliberately small range so collisions are common - the tie-breaker is the
        /// half of the comparison a wide random range would almost never exercise.
        /// </summary>
        [Fact]
        public void InsertingOneNoteAtATimeMatchesSortingTheWholeListEveryTime()
        {
            var random = new Random(20260811);
            var incremental = new List<QuickNote>();
            var reference = new List<QuickNote>();
            int collisions = 0;

            for (int i = 0; i < 1000; i++)
            {
                QuickNote note = At(random.Next(0, 12));
                QuickNotesOrder.Insert(incremental, note);

                foreach (QuickNote existing in reference)
                    if (existing.CreatedAtUtc == note.CreatedAtUtc) { collisions++; break; }
                reference.Add(note);
                QuickNotesOrder.Sort(reference);

                Assert.Equal(reference.Count, incremental.Count);
                for (int j = 0; j < reference.Count; j++)
                    Assert.Same(reference[j], incremental[j]);
            }

            // Proof the interesting branch actually ran: without collisions this test would only
            // ever have compared timestamps and the tie-breaker would be untested.
            Assert.True(collisions > 100, "expected plenty of equal timestamps, got " + collisions);
        }
    }
}
