using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// What "Add popular languages" is about to do, worked out before anything is written (ticket S0060 P7-h,
    /// APP-BEHAVIOUR rule 5): which of the curated layouts are missing from the user's input list and ship
    /// with this Windows, how many were left out because they are already there, and how many Windows does
    /// not have. Pure - the registry reads that feed it happen on the caller's pool job - so the batch can be
    /// confirmed by name, can say "nothing to add" instead of silently doing nothing, and can report after.
    /// </summary>
    internal sealed class PopularLayoutPlan
    {
        /// <summary>The layouts that will be installed, in the curated order.</summary>
        public List<InputLayouts.Available> ToAdd { get; } = new List<InputLayouts.Available>();

        /// <summary>Curated layouts the user already has.</summary>
        public int AlreadyInstalled { get; private set; }

        /// <summary>Curated layouts this Windows does not ship.</summary>
        public int NotAvailable { get; private set; }

        /// <summary>Everything the batch leaves alone: already there, or not on offer.</summary>
        public int Skipped => AlreadyInstalled + NotAvailable;

        /// <summary>
        /// The first layout of each curated language (the one <see cref="WorldLayouts.Recommended.Klids"/> lists
        /// first is the language's own keyboard; the other entries are variants the picker offers).
        /// </summary>
        public static PopularLayoutPlan Build(IEnumerable<string> installedKlids, IEnumerable<InputLayouts.Available> available,
            IEnumerable<WorldLayouts.Recommended> popular)
        {
            var installed = new HashSet<string>(installedKlids, StringComparer.OrdinalIgnoreCase);
            var shipped = new Dictionary<string, InputLayouts.Available>(StringComparer.OrdinalIgnoreCase);
            foreach (InputLayouts.Available layout in available) shipped[layout.Klid] = layout;

            var plan = new PopularLayoutPlan();
            foreach (WorldLayouts.Recommended language in popular)
            {
                if (language.Klids.Length == 0) continue;
                string klid = language.Klids[0];
                if (installed.Contains(klid)) { plan.AlreadyInstalled++; continue; }
                if (!shipped.TryGetValue(klid, out InputLayouts.Available? layout)) { plan.NotAvailable++; continue; }
                plan.ToAdd.Add(layout);
                installed.Add(klid);
            }
            return plan;
        }
    }
}
