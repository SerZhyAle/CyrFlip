using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// One check run to completion for a person who is waiting for the answer - the editor's "Check now" and the
    /// machine fallback of the smart add. It is the scheduler's rules without the scheduler: the target's own
    /// hard timeout, a synchronous probe moved off the calling thread, nothing written anywhere.
    /// </summary>
    internal static class TrafficLightQuickCheck
    {
        public static async Task<ProbeOutcome> RunAsync(TrafficLightTarget target, TrafficLightProbeSet probes, CancellationToken ct)
        {
            if (TrafficLightAddress.Validate(target) != AddressIssue.None)
                return ProbeOutcome.Fail(TrafficReasons.InvalidAddress);

            int timeout = target.EffectiveTimeoutMs;
            try
            {
                IBlockingTrafficProbe? blocking = probes.Blocking(target.Kind);
                if (blocking != null)
                {
                    Task<ProbeOutcome> call = Task.Run(() => blocking.Run(target));
                    Task winner = await Task.WhenAny(call, Task.Delay(timeout, ct)).ConfigureAwait(false);
                    if (winner == call) return await call.ConfigureAwait(false);
                    return ProbeOutcome.Fail(ct.IsCancellationRequested ? TrafficReasons.Failed : TrafficReasons.Timeout);
                }

                IAsyncTrafficProbe? probe = probes.Async(target.Kind);
                if (probe == null) return ProbeOutcome.Fail(TrafficReasons.Failed);
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    Task<ProbeOutcome> run = Task.Run(() => probe.RunAsync(target, cts.Token));
                    Task winner = await Task.WhenAny(run, Task.Delay(timeout, ct)).ConfigureAwait(false);
                    if (winner == run) return await run.ConfigureAwait(false);
                    cts.Cancel();
                    return ProbeOutcome.Fail(ct.IsCancellationRequested ? TrafficReasons.Failed : TrafficReasons.Timeout);
                }
            }
            catch (OperationCanceledException)
            {
                return ProbeOutcome.Fail(TrafficReasons.Failed);
            }
            catch (Exception)
            {
                return ProbeOutcome.Fail(TrafficReasons.Failed);
            }
        }
    }

    /// <summary>The outcome of turning a recognized paste into targets.</summary>
    internal sealed class SmartAddPlan
    {
        /// <summary>The new targets, ordinals following the existing list, that kind's defaults and nothing opted in.</summary>
        public readonly List<TrafficLightTarget> Targets = new List<TrafficLightTarget>();
        /// <summary>Candidates that were already on the board (same kind and address) and were not added again.</summary>
        public readonly List<TargetCandidate> Duplicates = new List<TargetCandidate>();
        /// <summary>The ids of targets that came from a <c>\\host</c> and still want their ping-then-445 question.</summary>
        public readonly HashSet<string> MachineIds = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Everything the one-field add does after the text is recognized (ticket S0069 D24): the new targets with
    /// their defaults, the duplicate rule, and the single fallback for a machine named the Windows file-sharing
    /// way. <b>No port other than 445 is ever tried and no other candidate is guessed</b> - the non-goal "no port
    /// scanning" holds - and a recognized line costs the network nothing until the add itself runs.
    /// </summary>
    internal static class TrafficLightSmartAdd
    {
        /// <summary>The SMB port, the only one the machine fallback may try after ping.</summary>
        public const int SmbPort = 445;

        public static SmartAddPlan Plan(IEnumerable<RecognizedLine> lines, IReadOnlyCollection<TrafficLightTarget> existing)
        {
            var plan = new SmartAddPlan();
            int ordinal = 0;
            foreach (TrafficLightTarget target in existing) ordinal = Math.Max(ordinal, target.Ordinal + 1);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TrafficLightTarget target in existing) seen.Add(Key(target.Kind, target.Address));

            foreach (RecognizedLine line in lines)
            {
                if (!line.IsRecognized) continue;
                foreach (TargetCandidate candidate in line.Candidates)
                {
                    if (!seen.Add(Key(candidate.Kind, candidate.Address)))
                    {
                        plan.Duplicates.Add(candidate);
                        continue;
                    }
                    TrafficLightTarget target = TrafficLightTarget.Create(candidate.Kind, candidate.Address, candidate.Label, ordinal++);
                    target.Fingerprint = candidate.Fingerprint;
                    plan.Targets.Add(target);
                    if (candidate.IsMachineName) plan.MachineIds.Add(target.Id);
                }
            }
            return plan;
        }

        private static string Key(string kind, string address) => kind + "|" + address.Trim();

        /// <summary>
        /// "A Windows PC usually drops ICMP": ping first, then <c>tcp</c> 445, and the first that answers is the
        /// kind. When neither does, the target is added as <c>ping</c> and is red - the answer says so. Only
        /// "reached" counts: a slow answer is still an answer.
        /// </summary>
        public static async Task<TrafficLightTarget> ResolveMachineAsync(TrafficLightTarget machine, TrafficLightProbeSet probes, CancellationToken ct)
        {
            string host = machine.Address.Trim();
            TrafficLightTarget ping = machine.Clone();
            ping.Kind = TrafficLightKinds.Ping;
            ping.Address = host;
            ProbeOutcome first = await TrafficLightQuickCheck.RunAsync(ping, probes, ct).ConfigureAwait(false);
            if (first.Reached || ct.IsCancellationRequested) return ping;

            TrafficLightTarget smb = machine.Clone();
            smb.Kind = TrafficLightKinds.Tcp;
            smb.Address = TrafficLightAddress.JoinHostPort(host, SmbPort);
            ProbeOutcome second = await TrafficLightQuickCheck.RunAsync(smb, probes, ct).ConfigureAwait(false);
            return second.Reached ? smb : ping;
        }
    }
}
