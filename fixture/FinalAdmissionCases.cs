#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using SHCDESE.API;

internal static unsafe partial class Program
{
    // Uses actual final-gate/dispatcher source. Scalar owner reads and the native
    // callable are controlled fixture leaves; no inventory, selector or payment
    // implementation is modeled and no game pointer/function is supplied.
    private sealed class FinalCase
    {
        internal readonly object Engine = new object(), Owner = new object(), World = new object();
        internal readonly List<string> Events = new List<string>();
        internal readonly EuropeanRecruitmentRequest Request = new EuropeanRecruitmentRequest(new IntPtr(17), 22, 41, 3);
        internal EuropeanRecruitmentNativeBill Bill = new EuropeanRecruitmentNativeBill(22, 100, 2, 2, 0, 0, 0);
        internal EuropeanRecruitmentNativeBill Table;
        internal EuropeanRecruitmentTailInputs Tail;
        internal object ObservedOwner, ObservedWorld, ObservedEngine;
        internal long Generation = 1;
        internal bool KnownReader = true, Reader = true, Published = true, Playable = true, Loading, SaveFrozen;
        internal bool ResourcesQualified = true, HorseQualified = true, SelectedAlive = true, SelectedEligible = true;
        internal int Unit = 37, Player = 3, NextType = 76, Gold = 200, Good1 = 2, Good2 = 2, Good3 = 0, FreeHorses = 1, CachedHorses = 1;
        internal uint Global = 4001;
        internal int NativeCalls, LegacyCalls, ProofCalls, AdmissionCalls, DemandOn, DemandOff;
        internal bool MissingReader, ThrowReader, DefaultObservation, DuplicateSite, ReturnZero, ThrowNative;
        internal Action<FinalCase> LegacyAction, CandidateAction, ReaderAction, ContextAction;
        internal Func<FinalCase, bool> AdmissionAction;
        internal FinalCase()
        {
            ObservedOwner = Owner; ObservedWorld = World; ObservedEngine = Engine;
            ResetBill();
        }
        internal void ResetBill()
        {
            Table = Bill;
            Tail = new EuropeanRecruitmentTailInputs(Bill.Gold, Bill.Good1, Bill.Good2, Bill.Good3, Bill.HorseRequirement, true);
        }
        internal EuropeanRecruitmentFinalRequirement Requirement() => new EuropeanRecruitmentFinalRequirement(
            new EuropeanRecruitmentFinalContext(Owner, World, Engine, 1, true, KnownReader, true, false, false), Bill, 76);
        internal EuropeanRecruitmentCandidate Candidate() => new EuropeanRecruitmentCandidate(Unit, Global, Player, 22, 41, NextType);
        private EuropeanRecruitmentCandidate Read(FinalCase ignored)
        { Events.Add("read"); CandidateAction?.Invoke(this); return Candidate(); }
        private void Legacy(FinalCase ignored) { Events.Add("legacy"); LegacyCalls++; LegacyAction?.Invoke(this); }
        private EuropeanRecruitmentFinalObservation Proof(FinalCase ignored)
        {
            Events.Add("proof"); ProofCalls++;
            ReaderAction?.Invoke(this);
            if (ThrowReader) throw new InvalidOperationException("Owned final-reader failure.");
            if (DefaultObservation) return default;
            return new EuropeanRecruitmentFinalObservation(new EuropeanRecruitmentFinalContext(ObservedOwner, ObservedWorld,
                ObservedEngine, Generation, Published, Reader, Playable, Loading, SaveFrozen), Candidate(), Table, Tail,
                new EuropeanRecruitmentNativeResources(Player, Gold, Good1, Good2, Good3,
                    FreeHorses, CachedHorses, ResourcesQualified, HorseQualified), SelectedAlive, SelectedEligible);
        }
        internal EuropeanRecruitmentBranch Dispatch() => MissingReader
            ? EuropeanRecruitmentInvocation.Dispatch(this, Unit, Player, Legacy, Read)
            : EuropeanRecruitmentInvocation.Dispatch(this, Unit, Player, Legacy, Read, Proof);
        internal EuropeanRecruitmentAttemptResult Run()
        {
            EuropeanRecruitmentAttemptResult result;
            var requirement = Requirement();
            lock (Engine)
                result = EuropeanRecruitmentInvocation.InvokeWithFinalValidation(Request, candidate => {
                    Events.Add("admission"); AdmissionCalls++;
                    return AdmissionAction == null || AdmissionAction(this);
                }, Engine, () => { ContextAction?.Invoke(this); return true; }, (manager, carrier, origin, player, query) => {
                    Events.Add("native"); NativeCalls++;
                    Check(manager == Request.Manager && carrier == 22 && origin == 41 && player == 3 && query == 0,
                        "The new gate must not issue a queryOnly call or change purchase arguments.");
                    var branch = Dispatch();
                    if (DuplicateSite)
                    {
                        var nested = Dispatch();
                        if (nested == EuropeanRecruitmentBranch.NoOwnedScope) Legacy(this);
                    }
                    if (ThrowNative) throw new ApplicationException("Owned tail failure.");
                    return ReturnZero || branch != EuropeanRecruitmentBranch.Allow ? 0 : Unit;
                }, enabled => {
                    Events.Add(enabled ? "on" : "off");
                    if (enabled) DemandOn++; else DemandOff++;
                }, requirement);
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope, "Final-proof scope leaked TLS.");
            return result;
        }
    }

    private static (string Name, Action Body)[] FinalAdmissionCases() => new (string, Action)[] {
        ("Final.KnownUnavailableReaderRefusesBeforeEntry", () => {
            var c = new FinalCase { KnownReader = false }; var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.InvalidContext, false);
            Check(c.NativeCalls == 0 && c.DemandOn == 0 && c.ProofCalls == 0 && c.AdmissionCalls == 0, "Unavailable owner profile entered native work.");
        }),
        ("Final.DefaultAndMissingProofNeverReachAdmission", () => {
            foreach (bool missing in new[] { false, true })
            {
                var c = new FinalCase { MissingReader = missing, DefaultObservation = !missing }; var result = c.Run();
                Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects,
                    missing ? EuropeanRecruitmentFailure.FinalProofUnavailable : EuropeanRecruitmentFailure.FinalProofFailed);
                Check(c.AdmissionCalls == 0 && c.LegacyCalls == 1 && c.DemandOff == 1, "Missing/default proof permitted admission or lost cleanup.");
            }
        }),
        ("Final.ValidProofFollowsLegacyAndPrecedesAdmission", () => {
            var c = new FinalCase(); var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
            Check(c.NativeCalls == 1 && c.ProofCalls == 2 && c.AdmissionCalls == 1 && c.DemandOn == 1 && c.DemandOff == 1, "Wrong proof/call count.");
            Check(c.Events.SequenceEqual(new[] { "on", "native", "legacy", "read", "proof", "admission", "read", "proof", "off" }),
                "The actual dispatcher must finish legacy then validate both sides of admission.");
        }),
        ("Final.CoherentFalseOrThrowRetainsOnlyNarrowAbortEvidence", () => {
            foreach (bool throws in new[] { false, true })
            {
                var c = new FinalCase { AdmissionAction = _ => throws ? throw new InvalidOperationException("Owned final admission failure.") : false };
                var result = c.Run();
                Outcome(result, EuropeanRecruitmentAttemptKind.RejectedBeforeConversion,
                    throws ? EuropeanRecruitmentFailure.AdmissionFailed : EuropeanRecruitmentFailure.AdmissionRejected);
                Check(c.ProofCalls == 2 && c.AdmissionCalls == 1, "Coherent refusal did not finish its final check.");
                if (throws) Check(result.ErrorMessage == "Owned final admission failure.", "Unexpected swallowed assertion replaced intended admission fault.");
            }
        }),
        ("Final.LegacyGoldSpendRefusesBeforeAdmission", () => {
            var c = new FinalCase { LegacyAction = value => value.Gold = 99 }; var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
            Check(c.AdmissionCalls == 0 && c.ProofCalls == 1, "Stale prefix gold affordability authorized admission.");
        }),
        ("Final.DuplicateGoodsNeedAggregatedCurrentStock", () => {
            var c = new FinalCase { LegacyAction = value => { value.Good1 = value.Good2 = 1; } }; var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
            Check(c.AdmissionCalls == 0, "Two native slots spent the same last good twice.");
            c = new FinalCase { Good2 = 3 }; result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
            Check(c.AdmissionCalls == 0, "Incoherent reads for the same native good were accepted.");
        }),
        ("Final.HorseClaimAndStalePrefixCacheRefuse", () => {
            foreach (bool stillOneFree in new[] { false, true })
            {
                var c = new FinalCase(); c.Bill = new EuropeanRecruitmentNativeBill(22, 100, 2, 2, 0, 0, -1); c.ResetBill();
                c.LegacyAction = value => { value.FreeHorses = stillOneFree ? 1 : 0; value.CachedHorses = 2; };
                var result = c.Run(); Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
                Check(c.AdmissionCalls == 0, "Unqualified/stale horse-cache proof authorized admission.");
            }
        }),
        ("Final.CurrentTableAndActualTailOperandsMustBothMatch", () => {
            Action<FinalCase>[] edits = {
                c => c.Table = new EuropeanRecruitmentNativeBill(22, 101, 2, 2, 0, 0, 0),
                c => c.Table = new EuropeanRecruitmentNativeBill(22, 100, 2, 2, 3, 0, 0),
                c => c.Tail = new EuropeanRecruitmentTailInputs(101, 2, 2, 0, 0, true),
                c => c.Tail = new EuropeanRecruitmentTailInputs(100, 3, 2, 0, 0, true),
                c => c.Tail = new EuropeanRecruitmentTailInputs(100, 2, 3, 0, 0, true),
                c => c.Tail = new EuropeanRecruitmentTailInputs(100, 2, 2, 3, 0, true),
                c => c.Tail = new EuropeanRecruitmentTailInputs(100, 2, 2, 0, -1, true),
                c => c.Tail = new EuropeanRecruitmentTailInputs(100, 2, 2, 0, 0, false)
            };
            foreach (var edit in edits)
            {
                var c = new FinalCase { LegacyAction = edit }; var result = c.Run();
                Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
                Check(c.AdmissionCalls == 0, "Table equality alone or an unqualified frame allowed admission.");
            }
        }),
        ("Final.RestoredBillHistoryDoesNotInventAnEpoch", () => {
            var c = new FinalCase();
            var original = c.Table;
            c.LegacyAction = value => {
                value.Table = new EuropeanRecruitmentNativeBill(22, 900, 3, 4, 5, 0, 0);
                value.Table = original;
            };
            var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
            Check(c.ProofCalls == 2 && c.AdmissionCalls == 1 && c.LegacyCalls == 1,
                "Restored content must still pass both actual final-proof stages and one admission.");
        }),
        ("Final.WorldOwnerMonitorSaveAndLifecycleDriftRefuse", () => {
            Action<FinalCase>[] edits = {
                c => c.ObservedOwner = new object(), c => c.ObservedWorld = new object(), c => c.ObservedEngine = new object(),
                c => c.Generation++, c => c.Published = false, c => c.Reader = false, c => c.Playable = false,
                c => c.Loading = true, c => c.SaveFrozen = true, c => c.ResourcesQualified = false
            };
            foreach (var edit in edits)
            {
                var c = new FinalCase { LegacyAction = edit }; var result = c.Run();
                Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
                Check(c.AdmissionCalls == 0 && c.DemandOff == 1, "Stale/frozen world reached admission or lost cleanup.");
            }
        }),
        ("Final.ThrowingReaderNeverReachesAdmission", () => {
            var c = new FinalCase { ThrowReader = true }; var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FinalProofFailed);
            Check(c.AdmissionCalls == 0 && result.ErrorMessage == "Owned final-reader failure." && c.DemandOff == 1,
                "Reader fault was swallowed as admission or replaced by another assertion.");
        }),
        ("Final.StableIdsDoNotHideDeadOrIneligibleSelectedUnits", () => {
            for (int mode = 0; mode < 4; mode++)
            {
                var c = new FinalCase();
                Action<FinalCase> invalidate = value => { if ((mode & 1) == 0) value.SelectedAlive = false; else value.SelectedEligible = false; };
                if (mode < 2) c.LegacyAction = invalidate;
                else c.AdmissionAction = value => { invalidate(value); return false; };
                var result = c.Run();
                Check(result.Kind == EuropeanRecruitmentAttemptKind.UnknownNativeEffects && c.AdmissionCalls == (mode < 2 ? 0 : 1),
                    "Stable identity scalars hid current death/transformation/eligibility drift.");
            }
        }),
        ("Final.TaintedCandidateOrProofCannotReachAdmission", () => {
            for (int mode = 0; mode < 4; mode++)
            {
                var c = new FinalCase();
                Action<FinalCase> taint = value => {
                    if (mode == 3) value.ContextAction = _ => EuropeanRecruitmentInvocation.ReportFrameFailure(new ApplicationException("Owned context-reader failure."));
                    else if (mode == 2) value.Dispatch();
                    else EuropeanRecruitmentInvocation.ReportFrameFailure(new ApplicationException("Owned tainted-reader failure."));
                };
                if (mode == 0) c.CandidateAction = taint; else c.ReaderAction = taint;
                var result = c.Run();
                Check(result.Kind == EuropeanRecruitmentAttemptKind.UnknownNativeEffects && c.AdmissionCalls == 0 && c.DemandOff == 1,
                    "Valid scalar data revived an already tainted owned attempt.");
                Check(c.ProofCalls == (mode == 0 ? 0 : 1), "Tainted candidate/proof was read again.");
            }
        }),
        ("Final.PostAdmissionDriftNeverBecomesRefundEvidence", () => {
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var c = new FinalCase { AdmissionAction = value => {
                    value.Gold = 0;
                    if (mode == 2) throw new InvalidOperationException("Owned drift failure.");
                    return mode == 0;
                } };
                var result = c.Run();
                Check(result.Kind == EuropeanRecruitmentAttemptKind.UnknownNativeEffects && c.AdmissionCalls == 1 && c.ProofCalls == 2,
                    "Post-admission change, including false/throw, became safe release evidence.");
            }
        }),
        ("Final.SelectedSiteAndRawReturnCannotBeReplayed", () => {
            var c = new FinalCase { DuplicateSite = true }; var result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedSelection);
            Check(c.ProofCalls == 2 && c.AdmissionCalls == 1, "Duplicate site called proof/admission again.");
            c = new FinalCase { ReturnZero = true }; result = c.Run();
            Check(result.Kind == EuropeanRecruitmentAttemptKind.UnknownNativeEffects, "Allowed raw zero became a rejection/refund receipt.");
            c = new FinalCase { ThrowNative = true }; result = c.Run();
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.NativeInvocationFailed);
        }),
        ("Final.NoOwnedScopeDoesNotCallNewLeaves", () => {
            var c = new FinalCase { ThrowReader = true };
            Check(c.Dispatch() == EuropeanRecruitmentBranch.NoOwnedScope && c.ProofCalls == 0 && c.LegacyCalls == 0 && c.AdmissionCalls == 0,
                "The dormant/no-scope route touched new proof or admission leaves.");
        })
    };
}
