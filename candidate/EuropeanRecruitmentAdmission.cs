#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SHCDESE.API
{
    /// <summary>The terminal evidence for one explicit European purchase invocation.</summary>
    public enum EuropeanRecruitmentAttemptKind
    {
        /// <summary>The owned wrapper did not invoke the native purchase callable.</summary>
        NotEntered,
        /// <summary>The callable returned normally. This does not certify payment or conversion.</summary>
        NativeReturned,
        /// <summary>The qualified same-owner rejection branch returned normally without this
        /// invocation's conversion/payment tail. Earlier native status/horse-cache writes and
        /// arbitrary callback/foreign writes are outside this narrow certificate.</summary>
        RejectedBeforeConversion,
        /// <summary>Native entry occurred, but rejection or settlement cannot be certified.</summary>
        UnknownNativeEffects
    }

    public enum EuropeanRecruitmentFailure
    {
        None, InvalidInput, MissingEngineMonitor, InvalidContext, ReentrantAttempt,
        DemandPublicationFailed, DemandRetirementFailed, ContextChanged,
        LegacyDispatchFailed, CandidateReadFailed, CandidateMismatch,
        AdmissionRejected, AdmissionFailed, UnexpectedSelection, UnexpectedNativeReturn,
        NativeInvocationFailed, FrameDispatchFailed,
        FinalProofUnavailable, FinalProofFailed, FinalProofChanged,
        CostScopeUnavailable, CostRestorationFailed
    }

    /// <summary>Immutable scalar observation of the selected peasant after legacy type edits.
    /// Numeric type values are existing eChimps values; this adds no native unit ID.
    /// NativeOrigin is caller data passed through the audited unused third native argument,
    /// not a certified building/home identity. Retaining this value reserves no native slot.</summary>
    public readonly struct EuropeanRecruitmentCandidate
    {
        internal EuropeanRecruitmentCandidate(int unitId, uint globalId, int playerId,
            int nativePaymentCarrier, int nativeOrigin, int nextUnitType)
        { UnitId = unitId; GlobalId = globalId; PlayerId = playerId; NativePaymentCarrier = nativePaymentCarrier;
          NativeOrigin = nativeOrigin; NextUnitType = nextUnitType; }
        public int UnitId { get; }
        public uint GlobalId { get; }
        public int PlayerId { get; }
        public int NativePaymentCarrier { get; }
        public int NativeOrigin { get; }
        public int NextUnitType { get; }
    }

    /// <summary>Runs once, synchronously, after the existing Pre event and before conversion.
    /// False or an ordinary exception requests the owned abort. Do not await, migrate threads,
    /// invoke native recruitment, or mutate native state here. A managed reservation is consumer
    /// state; this callback supplies neither native writer authority nor cross-store atomicity.</summary>
    public delegate bool EuropeanRecruitmentAdmission(EuropeanRecruitmentCandidate candidate);

    /// <summary>Immutable result. Only RejectedBeforeConversion is the narrow rejection
    /// certificate; raw zero, NativeReturned, exceptions and mismatches are not safe-cancel proof.
    /// The existing SDK must still verify gold/goods/horse/unit state and retain paid receipts.</summary>
    public sealed class EuropeanRecruitmentAttemptResult
    {
        internal EuropeanRecruitmentAttemptResult(EuropeanRecruitmentAttemptKind kind,
            EuropeanRecruitmentFailure failure, bool invoked, int? nativeReturn,
            EuropeanRecruitmentCandidate? candidate, bool admissionCalled, string errorType, string errorMessage)
        { Kind = kind; Failure = failure; NativeInvoked = invoked; NativeReturn = nativeReturn;
          Candidate = candidate; AdmissionCalled = admissionCalled; ErrorType = errorType; ErrorMessage = errorMessage; }
        public EuropeanRecruitmentAttemptKind Kind { get; }
        public EuropeanRecruitmentFailure Failure { get; }
        public bool NativeInvoked { get; }
        public int? NativeReturn { get; }
        public EuropeanRecruitmentCandidate? Candidate { get; }
        public bool AdmissionCalled { get; }
        /// <summary>Bounded immutable diagnostic text, not a protocol identifier.</summary>
        public string ErrorType { get; }
        public string ErrorMessage { get; }
    }

    // Internal leaves belong to the one SE owner. No consumer can supply a native invoker,
    // context validator, demand writer, legacy publisher or candidate reader to the public API.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int EuropeanRecruitmentNativeCall(IntPtr manager, int carrier, int nativeOrigin, int player, int queryOnly);

    internal readonly struct EuropeanRecruitmentRequest
    {
        internal EuropeanRecruitmentRequest(IntPtr manager, int carrier, int nativeOrigin, int player)
        { Manager = manager; Carrier = carrier; NativeOrigin = nativeOrigin; Player = player; }
        internal IntPtr Manager { get; }
        internal int Carrier { get; }
        internal int NativeOrigin { get; }
        internal int Player { get; }
    }

    internal enum EuropeanRecruitmentBranch : ulong
    {
        Abort = 0, Allow = 1,
        // Private frame protocol. Never a public predicate result or legacy SkipOriginal bit.
        NoOwnedScope = 0x53454C45UL
    }

    /// <summary>Managed state only. The native adapter must separately establish exact image,
    /// ABI, installed same-owner branch, live world, reset/save exclusion and current monitor.
    /// Context validation is not global native quiescence. This class installs no hook.</summary>
    internal static class EuropeanRecruitmentInvocation
    {
        [ThreadStatic] private static Attempt? current;
        internal static bool HasOwnedScope => current != null;
        internal static bool TryReadOwnedRequest(out EuropeanRecruitmentRequest request)
        { request = current == null ? default : current.Request; return current != null; }
        internal static void ReportFrameFailure(Exception error) =>
            current?.Fail(EuropeanRecruitmentFailure.FrameDispatchFailed, error, true);

        // The fixed native adapter supplies these leaves. CPU fixtures may substitute only
        // native invocation, coherent context/candidate reads and the legacy publication leaf.
        internal static EuropeanRecruitmentAttemptResult Invoke(EuropeanRecruitmentRequest request,
            EuropeanRecruitmentAdmission admission, object engineMonitor, Func<bool> isCurrentContext,
            EuropeanRecruitmentNativeCall original, Action<bool> writeDemand)
            => InvokeCore(request, admission, engineMonitor, isCurrentContext, original, writeDemand, null);

        // Private extension only: no public always-unavailable entry is published. The
        // fixed same-owner adapter must bind qualified final readers/lifecycle first.
        internal static EuropeanRecruitmentAttemptResult InvokeWithFinalValidation(EuropeanRecruitmentRequest request,
            EuropeanRecruitmentAdmission admission, object engineMonitor, Func<bool> isCurrentContext,
            EuropeanRecruitmentNativeCall original, Action<bool> writeDemand,
            EuropeanRecruitmentFinalRequirement requirement)
        {
            if (requirement == null || !requirement.MatchesRequest(request, engineMonitor))
                return NotEntered(EuropeanRecruitmentFailure.InvalidContext);
            return InvokeCore(request, admission, engineMonitor, isCurrentContext, original, writeDemand, requirement);
        }

        // Actual internal composition for the fixed SE owner. It owns scratch
        // around the existing invocation, rather than trusting a consumer's raw
        // table override. Readers/lifecycle/native entry still require qualification.
        internal static EuropeanRecruitmentAttemptResult InvokeWithOwnedCostsAndFinalValidation(
            EuropeanRecruitmentRequest request, EuropeanRecruitmentAdmission admission, object engineMonitor,
            Func<bool> isCurrentContext, EuropeanRecruitmentNativeCall original, Action<bool> writeDemand,
            EuropeanRecruitmentFinalRequirement requirement, NativeRecruitmentCostOwner costOwner)
        {
            if (requirement == null || costOwner == null || !requirement.MatchesRequest(request, engineMonitor))
                return NotEntered(EuropeanRecruitmentFailure.InvalidContext);
            if (current != null) return NotEntered(EuropeanRecruitmentFailure.ReentrantAttempt);
            if (request.Manager == IntPtr.Zero || request.Player < 1 || request.Player > 8 ||
                admission == null || engineMonitor == null || isCurrentContext == null || original == null || writeDemand == null)
                return NotEntered(EuropeanRecruitmentFailure.InvalidInput);
            if (!Monitor.IsEntered(engineMonitor)) return NotEntered(EuropeanRecruitmentFailure.MissingEngineMonitor);
            try { if (!isCurrentContext()) return NotEntered(EuropeanRecruitmentFailure.InvalidContext); }
            catch (Exception) { return NotEntered(EuropeanRecruitmentFailure.InvalidContext); }
            var bill = requirement.Bill;
            var scratch = new NativeRecruitmentCostSnapshot(bill.Gold,
                new NativeRecruitmentGoods(bill.Good1, bill.Good2, bill.Good3, bill.HorseRequirement));
            if (!costOwner.TryBegin(engineMonitor, request.Carrier, scratch, out var scope, out var beginFailure))
                return new EuropeanRecruitmentAttemptResult(EuropeanRecruitmentAttemptKind.NotEntered,
                    beginFailure.GoldRestore != null || beginFailure.GoodsRestore != null
                        ? EuropeanRecruitmentFailure.CostRestorationFailed : EuropeanRecruitmentFailure.CostScopeUnavailable,
                    false, null, null, false, "", "Cost scope was not entered; no native purchase callable was invoked.");
            NativeRecruitmentCostClose close = default;
            EuropeanRecruitmentAttemptResult result;
            try
            {
                result = InvokeCore(request, admission, engineMonitor, isCurrentContext, original, writeDemand,
                    requirement, costOwner, scope);
            }
            finally { close = costOwner.End(scope!); }
            if (close.Closed && close.Restored && !close.Invalidated) return result;
            return new EuropeanRecruitmentAttemptResult(result.NativeInvoked
                    ? EuropeanRecruitmentAttemptKind.UnknownNativeEffects : EuropeanRecruitmentAttemptKind.NotEntered,
                !close.Closed || !close.Restored ? EuropeanRecruitmentFailure.CostRestorationFailed
                    : EuropeanRecruitmentFailure.CostScopeUnavailable,
                result.NativeInvoked, result.NativeReturn, result.Candidate, result.AdmissionCalled,
                result.ErrorType, result.ErrorMessage);
        }

        private static EuropeanRecruitmentAttemptResult InvokeCore(EuropeanRecruitmentRequest request,
            EuropeanRecruitmentAdmission admission, object engineMonitor, Func<bool> isCurrentContext,
            EuropeanRecruitmentNativeCall original, Action<bool> writeDemand,
            EuropeanRecruitmentFinalRequirement? requirement, NativeRecruitmentCostOwner? costOwner = null,
            NativeRecruitmentCostOwner.Scope? costScope = null)
        {
            if (current != null) return NotEntered(EuropeanRecruitmentFailure.ReentrantAttempt);
            if (request.Manager == IntPtr.Zero || request.Player < 1 || request.Player > 8 ||
                admission == null || engineMonitor == null || isCurrentContext == null || original == null || writeDemand == null)
                return NotEntered(EuropeanRecruitmentFailure.InvalidInput);
            if (!Monitor.IsEntered(engineMonitor)) return NotEntered(EuropeanRecruitmentFailure.MissingEngineMonitor);
            try { if (!isCurrentContext()) return NotEntered(EuropeanRecruitmentFailure.InvalidContext); }
            catch (Exception) { return NotEntered(EuropeanRecruitmentFailure.InvalidContext); }

            var attempt = new Attempt(request, admission, engineMonitor, isCurrentContext, requirement, costOwner, costScope);
            current = attempt; // Publish TLS before global demand; retire demand before clearing TLS.
            try
            {
                try { writeDemand(true); }
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.DemandPublicationFailed, error, true); }
                if (attempt.Failure == EuropeanRecruitmentFailure.None && costOwner != null && !attempt.PhysicalCostMatches())
                    attempt.Fail(EuropeanRecruitmentFailure.CostScopeUnavailable, null, true);
                if (attempt.Failure == EuropeanRecruitmentFailure.None)
                {
                    attempt.Invoked = true; // Any error beyond this boundary is not NotEntered.
                    try { attempt.NativeReturn = original(request.Manager, request.Carrier, request.NativeOrigin, request.Player, 0); }
                    catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.NativeInvocationFailed, error, true); }
                    if (!attempt.ContextMatches()) attempt.Fail(EuropeanRecruitmentFailure.ContextChanged, null, true);
                }
            }
            finally
            {
                try { writeDemand(false); }
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.DemandRetirementFailed, error, true); }
                finally { current = null; }
            }
            return attempt.Complete();
        }

        // The active frame first calls HasOwnedScope. Keep the owner's no-scope return in a
        // separate static entry so captured lambdas cannot allocate ahead of that cheap guard.
        // The no-scope marker restores the outer frame, then uses the unchanged legacy frame.
        internal static EuropeanRecruitmentBranch Dispatch<TFrame>(TFrame frame, int selectedUnit, int player,
            Action<TFrame> publishLegacy, Func<TFrame, EuropeanRecruitmentCandidate> readCandidate)
            => Dispatch(frame, selectedUnit, player, publishLegacy, readCandidate, null);

        internal static EuropeanRecruitmentBranch Dispatch<TFrame>(TFrame frame, int selectedUnit, int player,
            Action<TFrame> publishLegacy, Func<TFrame, EuropeanRecruitmentCandidate> readCandidate,
            Func<TFrame, EuropeanRecruitmentFinalObservation>? readFinal)
        {
            Attempt? attempt = current;
            if (attempt == null) return EuropeanRecruitmentBranch.NoOwnedScope;
            if (attempt.Thread != Environment.CurrentManagedThreadId || attempt.Dispatching || attempt.Selections++ != 0)
            {
                attempt.Fail(EuropeanRecruitmentFailure.UnexpectedSelection, null, true);
                return EuropeanRecruitmentBranch.Abort; // TLS-owned window: deny the inner tail, not legacy passthrough.
            }
            attempt.Dispatching = true;
            try
            {
                try { publishLegacy(frame); } // Existing payload/order and final R9 writeback, once.
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.LegacyDispatchFailed, error, true); return EuropeanRecruitmentBranch.Abort; }
                if (attempt.Unknown || !attempt.ContextMatches())
                { attempt.Fail(EuropeanRecruitmentFailure.ContextChanged, null, true); return EuropeanRecruitmentBranch.Abort; }
                EuropeanRecruitmentCandidate candidate;
                try { candidate = readCandidate(frame); }
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.CandidateReadFailed, error, true); return EuropeanRecruitmentBranch.Abort; }
                if (selectedUnit <= 0 || player != attempt.Request.Player || candidate.UnitId != selectedUnit ||
                    candidate.GlobalId == 0 || candidate.PlayerId != player ||
                    candidate.NativePaymentCarrier != attempt.Request.Carrier || candidate.NativeOrigin != attempt.Request.NativeOrigin)
                { attempt.Fail(EuropeanRecruitmentFailure.CandidateMismatch, null, true); return EuropeanRecruitmentBranch.Abort; }
                attempt.Candidate = candidate;
                // All legacy handlers have completed. A required-but-missing or invalid
                // final proof must stop BEFORE admission can acquire consumer capacity.
                if (!attempt.ValidateFinal(frame, candidate, readFinal, false)) return EuropeanRecruitmentBranch.Abort;
                attempt.AdmissionCalled = true;
                bool allow = false;
                try { allow = attempt.Admission(candidate); }
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.AdmissionFailed, error, false); }
                EuropeanRecruitmentCandidate after;
                try { after = readCandidate(frame); }
                catch (Exception error) { attempt.Fail(EuropeanRecruitmentFailure.CandidateReadFailed, error, true); return EuropeanRecruitmentBranch.Abort; }
                if (attempt.Unknown || !attempt.ContextMatches() || !Same(candidate, after))
                { attempt.Fail(EuropeanRecruitmentFailure.CandidateMismatch, null, true); return EuropeanRecruitmentBranch.Abort; }
                // Admission may reenter managed code. Drift makes effects uncertain even
                // when it returned false/threw; it is never an implicit refund receipt.
                if (!attempt.ValidateFinal(frame, after, readFinal, true)) return EuropeanRecruitmentBranch.Abort;
                attempt.AfterAdmissionValidated = true;
                attempt.Rejected = !allow || attempt.Failure == EuropeanRecruitmentFailure.AdmissionFailed;
                if (attempt.Rejected && attempt.Failure == EuropeanRecruitmentFailure.None)
                    attempt.Failure = EuropeanRecruitmentFailure.AdmissionRejected;
                return attempt.Rejected ? EuropeanRecruitmentBranch.Abort : EuropeanRecruitmentBranch.Allow;
            }
            finally { attempt.Dispatching = false; }
        }

        private static bool Same(EuropeanRecruitmentCandidate x, EuropeanRecruitmentCandidate y) =>
            x.UnitId == y.UnitId && x.GlobalId == y.GlobalId && x.PlayerId == y.PlayerId &&
            x.NativePaymentCarrier == y.NativePaymentCarrier && x.NativeOrigin == y.NativeOrigin && x.NextUnitType == y.NextUnitType;

        private static EuropeanRecruitmentAttemptResult NotEntered(EuropeanRecruitmentFailure failure) =>
            new EuropeanRecruitmentAttemptResult(EuropeanRecruitmentAttemptKind.NotEntered, failure, false, null, null, false, "", "");

        private sealed class Attempt
        {
            internal Attempt(EuropeanRecruitmentRequest request, EuropeanRecruitmentAdmission admission,
                object engine, Func<bool> context, EuropeanRecruitmentFinalRequirement? requirement,
                NativeRecruitmentCostOwner? costOwner, NativeRecruitmentCostOwner.Scope? costScope)
            { Request = request; Admission = admission; Engine = engine; Context = context;
              Requirement = requirement; CostOwner = costOwner; CostScope = costScope; Thread = Environment.CurrentManagedThreadId; }
            internal readonly EuropeanRecruitmentRequest Request;
            internal readonly EuropeanRecruitmentAdmission Admission;
            internal readonly object Engine;
            internal readonly Func<bool> Context;
            internal readonly int Thread;
            internal readonly EuropeanRecruitmentFinalRequirement? Requirement;
            internal readonly NativeRecruitmentCostOwner? CostOwner;
            internal readonly NativeRecruitmentCostOwner.Scope? CostScope;
            internal bool Invoked, Dispatching, Unknown, AdmissionCalled, Rejected, AfterAdmissionValidated;
            internal int Selections;
            internal int? NativeReturn;
            internal EuropeanRecruitmentCandidate? Candidate;
            internal EuropeanRecruitmentFailure Failure;
            internal string ErrorType = "", ErrorMessage = "";
            internal bool ValidateFinal<TFrame>(TFrame frame, EuropeanRecruitmentCandidate candidate,
                Func<TFrame, EuropeanRecruitmentFinalObservation>? readFinal, bool afterAdmission)
            {
                if (Requirement == null) return true;
                if (Unknown) return false;
                if (readFinal == null)
                { Fail(EuropeanRecruitmentFailure.FinalProofUnavailable, null, true); return false; }
                try
                {
                    EuropeanRecruitmentFinalObservation observed = readFinal(frame);
                    if (Unknown) return false;
                    bool contextMatches = ContextMatches();
                    if (Unknown || !contextMatches || !PhysicalCostMatches() || Requirement.Validate(observed, candidate) != EuropeanRecruitmentFinalFailure.None)
                    {
                        Fail(afterAdmission ? EuropeanRecruitmentFailure.FinalProofChanged : EuropeanRecruitmentFailure.FinalProofFailed, null, true);
                        return false;
                    }
                    return true;
                }
                catch (Exception error)
                {
                    Fail(afterAdmission ? EuropeanRecruitmentFailure.FinalProofChanged : EuropeanRecruitmentFailure.FinalProofFailed, error, true);
                    return false;
                }
            }
            internal bool PhysicalCostMatches()
            {
                if (CostOwner == null) return true; // Retained pure managed/default proposal path.
                if (CostScope == null || Requirement == null || !CostOwner.TryRead(CostScope, out var physical)) return false;
                var bill = Requirement.Bill;
                return physical.Gold == bill.Gold && physical.Goods.First == bill.Good1 &&
                    physical.Goods.Second == bill.Good2 && physical.Goods.Third == bill.Good3 &&
                    physical.Goods.Horse == bill.HorseRequirement;
            }
            internal bool ContextMatches()
            {
                try { return Thread == Environment.CurrentManagedThreadId && Monitor.IsEntered(Engine) && Context(); }
                catch (Exception) { return false; }
            }
            internal void Fail(EuropeanRecruitmentFailure failure, Exception? error, bool unknown)
            {
                Unknown |= unknown;
                if (Failure == EuropeanRecruitmentFailure.None || Failure == EuropeanRecruitmentFailure.AdmissionRejected)
                {
                    Failure = failure;
                    if (error != null)
                    {
                        try { ErrorType = Bounded(error.GetType().FullName ?? error.GetType().Name); } catch (Exception) { ErrorType = "Exception"; }
                        try { ErrorMessage = Bounded(error.Message); } catch (Exception) { ErrorMessage = "Message unavailable"; }
                    }
                }
            }
            internal EuropeanRecruitmentAttemptResult Complete()
            {
                EuropeanRecruitmentAttemptKind kind;
                if (!Invoked) kind = EuropeanRecruitmentAttemptKind.NotEntered;
                else if (Unknown || !NativeReturn.HasValue) kind = EuropeanRecruitmentAttemptKind.UnknownNativeEffects;
                else if (Rejected && Candidate.HasValue && AdmissionCalled && AfterAdmissionValidated && NativeReturn == 0)
                    kind = EuropeanRecruitmentAttemptKind.RejectedBeforeConversion;
                else if (Selections == 0 && NativeReturn == 0) kind = EuropeanRecruitmentAttemptKind.NativeReturned;
                else if (!Rejected && Candidate.HasValue && AdmissionCalled && AfterAdmissionValidated && NativeReturn == Candidate.Value.UnitId)
                    kind = EuropeanRecruitmentAttemptKind.NativeReturned;
                else
                { kind = EuropeanRecruitmentAttemptKind.UnknownNativeEffects; Fail(EuropeanRecruitmentFailure.UnexpectedNativeReturn, null, true); }
                return new EuropeanRecruitmentAttemptResult(kind, Failure, Invoked, NativeReturn, Candidate, AdmissionCalled, ErrorType, ErrorMessage);
            }
            private static string Bounded(string value) => value.Length <= 256 ? value : value.Substring(0, 256);
        }
    }
}
