using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    internal enum QuickAddVerdict
    {
        /// <summary>Nothing typed yet.</summary>
        Empty,
        /// <summary>Text the recognizer cannot place: not a host, a site, a folder or a drive.</summary>
        NotRecognized,
        /// <summary>A contract string that was refused as a whole, or a <c>.fmscfg</c> document (never read).</summary>
        Refused,
        /// <summary>Everything typed is already on the board.</summary>
        Duplicate,
        /// <summary>The board would pass its limit.</summary>
        TooMany,
        /// <summary>One or more new targets are ready to be added.</summary>
        Ready,
    }

    /// <summary>The verdict of one look at the add dialog's two fields, and what to say about it.</summary>
    internal sealed class QuickAddEvaluation
    {
        public QuickAddVerdict Verdict = QuickAddVerdict.Empty;
        /// <summary>The new targets (ordinals following the list, that kind's defaults) when <see cref="Verdict"/> is Ready.</summary>
        public SmartAddPlan Plan = new SmartAddPlan();
        /// <summary>Sentences about what was read or dropped, already worded in the interface language.</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// What the board's "add" button does with its two fields, an address and an optional label (ticket S0069
    /// D24): the same pure recognition as the settings page's one-field add, with a label the person chose
    /// laid over a single new target. <b>Pure and offline</b> - it costs the network nothing; the one question
    /// a <c>\\host</c> asks (ping, then port 445) is <see cref="TrafficLightSmartAdd.ResolveMachineAsync"/>, run
    /// by the dialog only when the person confirms.
    /// </summary>
    internal static class TrafficLightQuickAdd
    {
        public static QuickAddEvaluation Evaluate(string address, string label,
            IReadOnlyCollection<TrafficLightTarget> existing, Func<string, string> translate)
        {
            var evaluation = new QuickAddEvaluation();
            string text = (address ?? "").Trim();
            if (text.Length == 0) return evaluation;

            RecognitionResult result = TargetRecognizer.Recognize(text);
            if (result.Lines.Count == 0) return evaluation;

            bool refused = false;
            foreach (RecognizedLine line in result.Lines)
            {
                if (line.Status == LineStatus.FmsCfgFile)
                {
                    evaluation.Notes.Add(translate("A file with a login is not read: enter the host and the port."));
                    refused = true;
                }
                else if (line.Status == LineStatus.Refused)
                {
                    evaluation.Notes.Add(Localization.Format(translate, "The connection string was refused as a whole: {0}.", line.Reason));
                    refused = true;
                }
                else if (line.CommaReadAs.Length > 0)
                {
                    evaluation.Notes.Add(Localization.Format(translate, "Read as {0}.", line.CommaReadAs));
                }
                if (line.SecretDropped && line.Status == LineStatus.Recognized)
                    evaluation.Notes.Add(translate("The login and password were not kept."));
            }

            List<RecognizedLine> recognized = result.Lines.Where(l => l.IsRecognized).ToList();
            if (recognized.Count == 0)
            {
                evaluation.Verdict = refused ? QuickAddVerdict.Refused : QuickAddVerdict.NotRecognized;
                return evaluation;
            }

            SmartAddPlan plan = TrafficLightSmartAdd.Plan(recognized, existing);
            if (plan.Targets.Count == 0)
            {
                evaluation.Verdict = QuickAddVerdict.Duplicate;
                return evaluation;
            }
            if (existing.Count + plan.Targets.Count > TrafficLightTargetList.MaxTargets)
            {
                evaluation.Verdict = QuickAddVerdict.TooMany;
                return evaluation;
            }

            // A label belongs to one target: with several (a contract string can name more than one) each keeps its own.
            string caption = (label ?? "").Trim();
            if (caption.Length > 0 && plan.Targets.Count == 1)
                plan.Targets[0].Label = caption.Length > TrafficLightTargetList.MaxLabelLength
                    ? caption.Substring(0, TrafficLightTargetList.MaxLabelLength)
                    : caption;

            evaluation.Plan = plan;
            evaluation.Verdict = QuickAddVerdict.Ready;
            return evaluation;
        }
    }
}
