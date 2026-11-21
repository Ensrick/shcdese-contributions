#nullable enable
using System;

namespace SHCDESE.API
{
    // Internal observations for the existing owner's final gate. These values are NOT
    // writer credentials: the fixed native reader/installation/lifecycle binding is
    // still missing. A fixture may substitute these leaves, never claim live proof.
    // Exact content consumed by this tail, not an invented all-writer revision.
    internal readonly struct EuropeanRecruitmentNativeBill
    {
        internal readonly int PaymentCarrier, Gold, Good1, Good2, Good3, EmptyGood, HorseRequirement;
        internal EuropeanRecruitmentNativeBill(int paymentCarrier, int gold, int good1, int good2, int good3,
            int emptyGood, int horseRequirement)
        { PaymentCarrier = paymentCarrier; Gold = gold; Good1 = good1; Good2 = good2; Good3 = good3;
          EmptyGood = emptyGood; HorseRequirement = horseRequirement; }
        internal bool IsWellFormed => PaymentCarrier >= 22 && PaymentCarrier <= 28 && Gold >= 0;
        internal bool RequiresHorse => HorseRequirement == -1;
        internal int Good(int slot) => slot == 0 ? Good1 : slot == 1 ? Good2 : Good3;
        internal bool Same(EuropeanRecruitmentNativeBill other) => PaymentCarrier == other.PaymentCarrier &&
            Gold == other.Gold && Good1 == other.Good1 && Good2 == other.Good2 && Good3 == other.Good3 &&
            EmptyGood == other.EmptyGood && HorseRequirement == other.HorseRequirement;
    }

    // Static FBCB operands, not an invented current-table projection. The accepted
    // tail consumes R14D gold, R13D / [nativeRSP+0x88] / R12D goods, and tests
    // [nativeRSP+0x70] == -1 for a horse. No native memory is read here. Complete
    // entry-to-site dataflow and a qualified owner reader must bind these scalars.
    internal readonly struct EuropeanRecruitmentTailInputs
    {
        internal readonly int GoldR14D, Good1R13D, Good2Stack88, Good3R12D, HorseStack70;
        internal readonly bool QualifiedFrameAndCostDomain;
        internal EuropeanRecruitmentTailInputs(int goldR14D, int good1R13D, int good2Stack88,
            int good3R12D, int horseStack70, bool qualifiedFrameAndCostDomain)
        { GoldR14D = goldR14D; Good1R13D = good1R13D; Good2Stack88 = good2Stack88;
          Good3R12D = good3R12D; HorseStack70 = horseStack70; QualifiedFrameAndCostDomain = qualifiedFrameAndCostDomain; }
        internal bool Matches(EuropeanRecruitmentNativeBill bill) => QualifiedFrameAndCostDomain &&
            GoldR14D == bill.Gold && Good1R13D == bill.Good1 && Good2Stack88 == bill.Good2 &&
            Good3R12D == bill.Good3 && HorseStack70 == bill.HorseRequirement;
    }

    internal readonly struct EuropeanRecruitmentFinalContext
    {
        internal readonly object? Owner, World, Engine;
        internal readonly long LoadGeneration;
        internal readonly bool PublishedOwner, QualifiedReader, SinglePlayerReady, Loading, SaveFrozen;
        internal EuropeanRecruitmentFinalContext(object? owner, object? world, object? engine, long loadGeneration,
            bool publishedOwner, bool qualifiedReader, bool singlePlayerReady, bool loading, bool saveFrozen)
        { Owner = owner; World = world; Engine = engine; LoadGeneration = loadGeneration;
          PublishedOwner = publishedOwner; QualifiedReader = qualifiedReader; SinglePlayerReady = singlePlayerReady;
          Loading = loading; SaveFrozen = saveFrozen; }
        internal bool IsAdmitted => Owner != null && World != null && Engine != null && LoadGeneration > 0 &&
            PublishedOwner && QualifiedReader && SinglePlayerReady && !Loading && !SaveFrozen;
        internal bool SameBinding(EuropeanRecruitmentFinalContext other) => ReferenceEquals(Owner, other.Owner) &&
            ReferenceEquals(World, other.World) && ReferenceEquals(Engine, other.Engine) && LoadGeneration == other.LoadGeneration;
    }

    internal readonly struct EuropeanRecruitmentNativeResources
    {
        internal readonly int Player, Gold, Good1, Good2, Good3, FreeHorses, PrefixCachedHorses;
        internal readonly bool QualifiedResources, QualifiedHorseAvailability;
        internal EuropeanRecruitmentNativeResources(int player, int gold, int good1, int good2, int good3,
            int freeHorses, int prefixCachedHorses, bool qualifiedResources, bool qualifiedHorseAvailability)
        { Player = player; Gold = gold; Good1 = good1; Good2 = good2; Good3 = good3; FreeHorses = freeHorses;
          PrefixCachedHorses = prefixCachedHorses; QualifiedResources = qualifiedResources;
          QualifiedHorseAvailability = qualifiedHorseAvailability; }
        internal int Good(int slot) => slot == 0 ? Good1 : slot == 1 ? Good2 : Good3;
    }

    internal readonly struct EuropeanRecruitmentFinalObservation
    {
        internal readonly EuropeanRecruitmentFinalContext Context;
        internal readonly EuropeanRecruitmentCandidate Candidate;
        internal readonly EuropeanRecruitmentNativeBill TableBill;
        internal readonly EuropeanRecruitmentTailInputs Tail;
        internal readonly EuropeanRecruitmentNativeResources Resources;
        // Fresh selected-record results, not reader-installation qualification.
        // The fixed reader must reject a dead/transformed/ineligible peasant even
        // when its slot/global/owner tuple did not change during a callback.
        internal readonly bool SelectedUnitAlive, SelectedUnitEligible;
        internal EuropeanRecruitmentFinalObservation(EuropeanRecruitmentFinalContext context,
            EuropeanRecruitmentCandidate candidate, EuropeanRecruitmentNativeBill tableBill,
            EuropeanRecruitmentTailInputs tail, EuropeanRecruitmentNativeResources resources,
            bool selectedUnitAlive, bool selectedUnitEligible)
        { Context = context; Candidate = candidate; TableBill = tableBill; Tail = tail; Resources = resources;
          SelectedUnitAlive = selectedUnitAlive; SelectedUnitEligible = selectedUnitEligible; }
    }

    internal enum EuropeanRecruitmentFinalFailure
    {
        None, UnqualifiedContext, BindingChanged, CandidateChanged, BillChanged,
        UnqualifiedTail, UnqualifiedResources, NativeGoldUnavailable, NativeGoodsUnavailable,
        InconsistentGoodsRead, NativeHorseUnavailable, CandidateNoLongerEligible
    }

    internal sealed class EuropeanRecruitmentFinalRequirement
    {
        internal readonly EuropeanRecruitmentFinalContext Context;
        internal readonly EuropeanRecruitmentNativeBill Bill;
        internal readonly int NextUnitType;
        internal EuropeanRecruitmentFinalRequirement(EuropeanRecruitmentFinalContext context,
            EuropeanRecruitmentNativeBill bill, int nextUnitType)
        { Context = context; Bill = bill; NextUnitType = nextUnitType; }
        internal bool MatchesRequest(EuropeanRecruitmentRequest request, object engine) => Context.IsAdmitted &&
            ReferenceEquals(Context.Engine, engine) && Bill.IsWellFormed && Bill.PaymentCarrier == request.Carrier && NextUnitType >= 0;

        // No allocation, callbacks, queryOnly call, native pointer or native algorithm.
        // The fixed owner supplies qualified scalar observations AFTER all legacy Pre
        // and again after managed admission. Matching flags alone do not qualify that
        // missing reader, establish global exclusion or authorize any material debit.
        internal EuropeanRecruitmentFinalFailure Validate(EuropeanRecruitmentFinalObservation observed,
            EuropeanRecruitmentCandidate selected)
        {
            if (!observed.Context.IsAdmitted) return EuropeanRecruitmentFinalFailure.UnqualifiedContext;
            if (!Context.SameBinding(observed.Context)) return EuropeanRecruitmentFinalFailure.BindingChanged;
            if (!observed.SelectedUnitAlive || !observed.SelectedUnitEligible)
                return EuropeanRecruitmentFinalFailure.CandidateNoLongerEligible;
            var candidate = observed.Candidate;
            if (candidate.UnitId != selected.UnitId || candidate.GlobalId != selected.GlobalId ||
                candidate.PlayerId != selected.PlayerId || candidate.NativePaymentCarrier != selected.NativePaymentCarrier ||
                candidate.NativeOrigin != selected.NativeOrigin || candidate.NextUnitType != selected.NextUnitType ||
                candidate.NextUnitType != NextUnitType)
                return EuropeanRecruitmentFinalFailure.CandidateChanged;
            if (!Bill.Same(observed.TableBill)) return EuropeanRecruitmentFinalFailure.BillChanged;
            if (!observed.Tail.Matches(Bill)) return EuropeanRecruitmentFinalFailure.UnqualifiedTail;
            var resources = observed.Resources;
            if (!resources.QualifiedResources || resources.Player != selected.PlayerId)
                return EuropeanRecruitmentFinalFailure.UnqualifiedResources;
            if (resources.Gold < Bill.Gold || resources.Gold < 0)
                return EuropeanRecruitmentFinalFailure.NativeGoldUnavailable;
            for (int slot = 0; slot < 3; slot++)
            {
                int good = Bill.Good(slot);
                if (good == Bill.EmptyGood) continue;
                int quantity = 0;
                for (int other = 0; other < 3; other++)
                    if (Bill.Good(other) == good)
                    {
                        quantity++;
                        if (resources.Good(other) != resources.Good(slot))
                            return EuropeanRecruitmentFinalFailure.InconsistentGoodsRead;
                    }
                if (resources.Good(slot) < quantity)
                    return EuropeanRecruitmentFinalFailure.NativeGoodsUnavailable;
            }
            if (Bill.RequiresHorse && (!resources.QualifiedHorseAvailability || resources.FreeHorses < 1 ||
                resources.PrefixCachedHorses < 1 || resources.FreeHorses != resources.PrefixCachedHorses))
                return EuropeanRecruitmentFinalFailure.NativeHorseUnavailable;
            return EuropeanRecruitmentFinalFailure.None;
        }
    }
}
