using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The incremental display order of the clipboard history. The load-bearing test is
    /// <see cref="Random_operations_match_the_reference_sort"/>: it replays a long random operation
    /// stream and compares the incrementally maintained list against the full
    /// <c>OrderByDescending(IsPinned).ThenByDescending(CreatedAt)</c> that used to run after every
    /// single copy - i.e. the incremental version is proven equal to the code it replaced, which is
    /// the only claim that matters here.
    /// </summary>
    public sealed class ClipboardHistoryOrderTests
    {
        /// <summary>
        /// Dates are unique on purpose: the reference sort is stable, so equal keys would make its
        /// output depend on insertion order rather than on the order under test. Real dates come from
        /// <c>DateTime.UtcNow</c> and collide only theoretically, and any order among identical
        /// timestamps is equally correct.
        /// </summary>
        private static ClipboardHistoryEntry Entry(string uuid, long ticks, bool pinned = false)
            => new ClipboardHistoryEntry
            {
                Uuid = uuid,
                Text = uuid,
                CreatedAt = new DateTime(ticks, DateTimeKind.Utc),
                IsPinned = pinned,
            };

        private static IEnumerable<string> ReferenceOrder(IEnumerable<ClipboardHistoryEntry> live)
            => live.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CreatedAt).Select(x => x.Uuid);

        [Fact]
        public void Newest_entry_comes_first()
        {
            var order = new ClipboardHistoryOrder();
            order.Add(Entry("a", 100));
            order.Add(Entry("b", 200));
            order.Add(Entry("c", 150));

            Assert.Equal(new[] { "b", "c", "a" }, order.Entries.Select(x => x.Uuid));
        }

        [Fact]
        public void Pinned_entries_lead_the_list_even_when_older()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry old = Entry("old", 100);
            order.Add(old);
            order.Add(Entry("new", 900));

            order.Update(old, old.CreatedAt, pinned: true);

            Assert.Equal(new[] { "old", "new" }, order.Entries.Select(x => x.Uuid));
            Assert.Equal(1, order.PinnedCount);
        }

        [Fact]
        public void A_pinned_entry_lands_by_its_own_date_among_the_pinned()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry first = Entry("first", 300, pinned: true);
            ClipboardHistoryEntry third = Entry("third", 100, pinned: true);
            ClipboardHistoryEntry middle = Entry("middle", 200);
            order.Add(first);
            order.Add(third);
            order.Add(middle);

            order.Update(middle, middle.CreatedAt, pinned: true);

            Assert.Equal(new[] { "first", "middle", "third" }, order.Entries.Select(x => x.Uuid));
            Assert.Equal(3, order.PinnedCount);
        }

        [Fact]
        public void Touching_a_pinned_entry_moves_it_to_the_head_of_the_pinned_group()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry older = Entry("older", 100, pinned: true);
            order.Add(Entry("newer", 200, pinned: true));
            order.Add(older);
            order.Add(Entry("loose", 900));

            order.Update(older, new DateTime(500, DateTimeKind.Utc), pinned: true);

            Assert.Equal(new[] { "older", "newer", "loose" }, order.Entries.Select(x => x.Uuid));
            Assert.Equal(2, order.PinnedCount);
        }

        [Fact]
        public void Unpinning_returns_the_entry_to_the_dated_group()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry pinned = Entry("pinned", 150, pinned: true);
            order.Add(pinned);
            order.Add(Entry("newer", 200));
            order.Add(Entry("older", 100));

            order.Update(pinned, pinned.CreatedAt, pinned: false);

            Assert.Equal(new[] { "newer", "pinned", "older" }, order.Entries.Select(x => x.Uuid));
            Assert.Equal(0, order.PinnedCount);
        }

        [Fact]
        public void Deleting_a_pinned_entry_keeps_the_pinned_count_honest()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry pinned = Entry("pinned", 100, pinned: true);
            order.Add(pinned);
            order.Add(Entry("loose", 200));

            order.Remove(pinned);

            Assert.Equal(0, order.PinnedCount);
            Assert.Equal(new[] { "loose" }, order.Entries.Select(x => x.Uuid));
            Assert.Null(order.Find("pinned"));
        }

        [Fact]
        public void A_duplicate_uuid_is_refused_rather_than_added_twice()
        {
            var order = new ClipboardHistoryOrder();
            Assert.True(order.Add(Entry("same", 100)));
            Assert.False(order.Add(Entry("same", 200)));

            Assert.Single(order.Entries);
            // The indexed entry is still the first one - a second copy must never shadow it.
            Assert.Equal(new DateTime(100, DateTimeKind.Utc), order.Find("same")!.CreatedAt);
        }

        [Fact]
        public void Current_moves_between_two_entries_without_touching_the_rest()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry first = Entry("first", 100);
            ClipboardHistoryEntry second = Entry("second", 200);
            order.Add(first);
            order.Add(second);

            order.SetCurrent(first);
            Assert.True(first.IsCurrent);
            Assert.False(second.IsCurrent);

            order.SetCurrent(second);
            Assert.False(first.IsCurrent);
            Assert.True(second.IsCurrent);
        }

        [Fact]
        public void Deleting_the_current_entry_leaves_no_current_one()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry entry = Entry("gone", 100);
            ClipboardHistoryEntry other = Entry("other", 200);
            order.Add(entry);
            order.Add(other);
            order.SetCurrent(entry);

            order.Remove(entry);
            // Re-electing the entry that took its place must still flip the flag: a stale _current
            // reference would make SetCurrent believe nothing changed.
            order.SetCurrent(other);

            Assert.True(other.IsCurrent);
        }

        [Fact]
        public void Clear_drops_the_entries_the_index_and_the_current_flag()
        {
            var order = new ClipboardHistoryOrder();
            ClipboardHistoryEntry entry = Entry("a", 100, pinned: true);
            order.Add(entry);
            order.SetCurrent(entry);

            order.Clear();

            Assert.Empty(order.Entries);
            Assert.Equal(0, order.PinnedCount);
            Assert.Null(order.Find("a"));
            // Nothing is current any more, so the next election has to take effect.
            ClipboardHistoryEntry fresh = Entry("b", 200);
            order.Add(fresh);
            order.SetCurrent(fresh);
            Assert.True(fresh.IsCurrent);
        }

        /// <summary>
        /// The whole point of the class, checked against the implementation it replaced: after every
        /// one of a thousand random add / touch / pin / unpin / delete operations, the incremental
        /// order must equal the full reference sort, and the pinned-prefix invariant must hold.
        ///
        /// <para>Adds outweigh deletes 3:2 on purpose. With both equally likely the stream is a
        /// symmetric random walk that spends much of its time near an empty history - which passes
        /// while proving almost nothing. A history that grows is also what the real thing does.</para>
        /// </summary>
        [Fact]
        public void Random_operations_match_the_reference_sort()
        {
            var order = new ClipboardHistoryOrder();
            var live = new List<ClipboardHistoryEntry>();
            var random = new Random(20260730); // fixed seed: a failure has to be reproducible
            long ticks = 1000;
            var performed = new Dictionary<string, int>
            {
                { "add", 0 }, { "touch", 0 }, { "pin", 0 }, { "unpin", 0 }, { "delete", 0 },
            };

            for (int step = 0; step < 1000; step++)
            {
                // 0-2 add, 3 touch, 4 pin, 5 unpin, 6-7 delete.
                switch (live.Count == 0 ? 0 : random.Next(8))
                {
                    case 0:
                    case 1:
                    case 2:
                        {
                            var entry = Entry("e" + performed["add"], ticks++);
                            order.Add(entry);
                            live.Add(entry);
                            performed["add"]++;
                            break;
                        }
                    case 3: // the same text copied again
                        {
                            ClipboardHistoryEntry entry = live[random.Next(live.Count)];
                            order.Update(entry, new DateTime(ticks++, DateTimeKind.Utc), entry.IsPinned);
                            performed["touch"]++;
                            break;
                        }
                    case 4:
                        {
                            ClipboardHistoryEntry entry = live[random.Next(live.Count)];
                            order.Update(entry, entry.CreatedAt, pinned: true);
                            performed["pin"]++;
                            break;
                        }
                    case 5:
                        {
                            ClipboardHistoryEntry entry = live[random.Next(live.Count)];
                            order.Update(entry, entry.CreatedAt, pinned: false);
                            performed["unpin"]++;
                            break;
                        }
                    default:
                        {
                            int index = random.Next(live.Count);
                            order.Remove(live[index]);
                            live.RemoveAt(index);
                            performed["delete"]++;
                            break;
                        }
                }

                Assert.Equal(ReferenceOrder(live), order.Entries.Select(x => x.Uuid));
                Assert.Equal(live.Count(x => x.IsPinned), order.PinnedCount);
            }

            // A stream that skipped a branch, or ended on an empty history, would pass vacuously -
            // so the run has to prove it exercised every operation and left both groups populated.
            foreach (KeyValuePair<string, int> operation in performed)
                Assert.True(operation.Value >= 50,
                    "the random stream performed '" + operation.Key + "' only " + operation.Value + " time(s)");
            Assert.True(live.Count(x => x.IsPinned) >= 5,
                "the run ended with only " + live.Count(x => x.IsPinned) + " pinned entries");
            Assert.True(live.Count(x => !x.IsPinned) >= 5,
                "the run ended with only " + live.Count(x => !x.IsPinned) + " unpinned entries");
        }
    }
}
