#nullable enable
using System;
using System.Threading;

namespace SHCDESE.API
{
    internal readonly struct NativeRecruitmentGoods : IEquatable<NativeRecruitmentGoods>
    {
        internal readonly int First, Second, Third, Horse;
        internal NativeRecruitmentGoods(int first, int second, int third, int horse)
        { First = first; Second = second; Third = third; Horse = horse; }
        public bool Equals(NativeRecruitmentGoods other) => First == other.First && Second == other.Second &&
            Third == other.Third && Horse == other.Horse;
    }

    internal readonly struct NativeRecruitmentCostSnapshot
    {
        internal readonly int Gold;
        internal readonly NativeRecruitmentGoods Goods;
        internal NativeRecruitmentCostSnapshot(int gold, NativeRecruitmentGoods goods)
        { Gold = gold; Goods = goods; }
    }

    // This is an internal, fixed SE storage adapter, not a consumer/provider extension.
    // Leaves may touch the existing bounded cost arrays/dictionary only. They must not
    // log, call user code, enter native functions, acquire the engine monitor or wait.
    internal interface INativeRecruitmentCostStorage
    {
        int ReadPhysicalGold(int carrier);
        NativeRecruitmentGoods ReadPhysicalGoods(int carrier);
        void WritePhysicalGold(int carrier, int value);
        void WritePhysicalGoods(int carrier, NativeRecruitmentGoods value);
        void SetConfiguredGold(int carrier, int value);
        void SetConfiguredGoods(int carrier, NativeRecruitmentGoods value);
        void ClearConfiguredOverrides();
    }

    internal enum NativeRecruitmentCostFailureKind
    {
        None, InvalidInput, MissingEngineMonitor, NestedScope, PoisonedOwner,
        StorageFailure, WrongOwnerOrThread, InvalidatedScope
    }

    internal readonly struct NativeRecruitmentCostFailure
    {
        internal readonly NativeRecruitmentCostFailureKind Kind;
        internal readonly Exception? Primary, GoldRestore, GoodsRestore;
        internal NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind kind,
            Exception? primary = null, Exception? goldRestore = null, Exception? goodsRestore = null)
        { Kind = kind; Primary = primary; GoldRestore = goldRestore; GoodsRestore = goodsRestore; }
    }

    internal readonly struct NativeRecruitmentCostClose
    {
        internal readonly bool Closed, Restored, Invalidated;
        internal readonly NativeRecruitmentCostFailure Failure;
        internal NativeRecruitmentCostClose(bool closed, bool restored, bool invalidated,
            NativeRecruitmentCostFailure failure)
        { Closed = closed; Restored = restored; Invalidated = invalidated; Failure = failure; }
    }

    // Owns physical scratch only. It is not native entry/writer authority, a world
    // lease, a save veto, a material reservation, or a revision of arbitrary memory.
    // The short gate is NEVER retained across native entry or untrusted callbacks.
    internal sealed class NativeRecruitmentCostOwner
    {
        private readonly object sync = new object();
        private readonly INativeRecruitmentCostStorage storage;
        private Scope? active;
        private bool poisoned;

        internal NativeRecruitmentCostOwner(INativeRecruitmentCostStorage storage)
        { this.storage = storage ?? throw new ArgumentNullException(nameof(storage)); }

        internal bool TryBegin(object engineMonitor, int carrier, NativeRecruitmentCostSnapshot scratch,
            out Scope? scope, out NativeRecruitmentCostFailure failure)
        {
            scope = null;
            if (engineMonitor == null || carrier < 22 || carrier > 28 || scratch.Gold < 0)
            { failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.InvalidInput); return false; }
            if (!Monitor.IsEntered(engineMonitor))
            { failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.MissingEngineMonitor); return false; }
            // All per-attempt storage is prepared before the first native invocation.
            Scope pending = new Scope(this, engineMonitor, carrier);
            lock (sync)
            {
                if (poisoned)
                { failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.PoisonedOwner); return false; }
                if (active != null)
                { failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.NestedScope); return false; }
                try
                {
                    pending.RestoreGold = storage.ReadPhysicalGold(carrier);
                    pending.RestoreGoods = storage.ReadPhysicalGoods(carrier);
                    pending.GoldIntentKnown = pending.GoodsIntentKnown = true;
                }
                catch (Exception error)
                { failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.StorageFailure, error); return false; }
                active = pending; // Cleanup owns partial physical writes from here.
                try
                {
                    storage.WritePhysicalGold(carrier, scratch.Gold);
                    storage.WritePhysicalGoods(carrier, scratch.Goods);
                    scope = pending;
                    failure = default;
                    return true;
                }
                catch (Exception error)
                {
                    pending.Invalidated = true;
                    pending.FirstFailure = error;
                    NativeRecruitmentCostClose close = CloseLocked(pending);
                    failure = new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.StorageFailure,
                        error, close.Failure.GoldRestore, close.Failure.GoodsRestore);
                    return false;
                }
            }
        }

        internal bool TryRead(Scope scope, out NativeRecruitmentCostSnapshot snapshot)
        {
            snapshot = default;
            if (scope == null || !ReferenceEquals(scope.Owner, this) ||
                scope.Thread != Environment.CurrentManagedThreadId || !Monitor.IsEntered(scope.Engine)) return false;
            lock (sync)
            {
                if (!ReferenceEquals(active, scope) || scope.Invalidated || poisoned) return false;
                try
                {
                    snapshot = new NativeRecruitmentCostSnapshot(storage.ReadPhysicalGold(scope.Carrier),
                        storage.ReadPhysicalGoods(scope.Carrier));
                    return true;
                }
                catch (Exception error)
                { Invalidate(scope, error); return false; }
            }
        }

        // Ordinary public setters keep their full original domain. Only scratch
        // Begin is restricted to European carriers22..28. No CAS/value-equality test
        // can replace recording an explicit successful same-value setter here.
        internal void SetGold(int carrier, int value)
        {
            lock (sync)
            {
                Scope? scope = active != null && active.Carrier == carrier ? active : null;
                if (scope != null && scope.GoldIntentKnown)
                {
                    // Real getters do not populate tracking. Unmask just before
                    // the supported writer can first call EnsureTracked, without
                    // eagerly changing original/base snapshot timing at Begin.
                    try { storage.WritePhysicalGold(carrier, scope.RestoreGold); }
                    catch (Exception error) { Invalidate(scope, error); throw; }
                }
                try { storage.SetConfiguredGold(carrier, value); }
                catch (Exception error)
                {
                    if (scope != null) { Invalidate(scope, error); CaptureGoldIntent(scope); }
                    throw;
                }
                if (scope != null)
                {
                    // In the fixed adapter this carrier is in-range: normal
                    // return means the requested physical value was published.
                    scope.RestoreGold = value;
                    scope.GoldIntentKnown = true;
                    scope.GoldIntentFailure = null;
                }
            }
        }

        internal void SetGoods(int carrier, NativeRecruitmentGoods value)
        {
            lock (sync)
            {
                Scope? scope = active != null && active.Carrier == carrier ? active : null;
                if (scope != null && scope.GoodsIntentKnown)
                {
                    try { storage.WritePhysicalGoods(carrier, scope.RestoreGoods); }
                    catch (Exception error) { Invalidate(scope, error); throw; }
                }
                try { storage.SetConfiguredGoods(carrier, value); }
                catch (Exception error)
                {
                    if (scope != null) { Invalidate(scope, error); CaptureGoodsIntent(scope); }
                    throw;
                }
                if (scope != null)
                {
                    scope.RestoreGoods = value;
                    scope.GoodsIntentKnown = true;
                    scope.GoodsIntentFailure = null;
                }
            }
        }

        // Called only at the existing UnitManager cost-clear position in unload.
        // Restore scratch BEFORE ClearOverrides: depth1 Clear is a no-op and gold
        // physical wrappers are intentionally not cleared by the original SE code.
        internal void ClearForUnload()
        {
            lock (sync)
            {
                Scope? scope = active;
                if (scope != null)
                {
                    scope.Invalidated = true;
                    Exception? gold = scope.GoldIntentFailure, goods = scope.GoodsIntentFailure;
                    if (scope.GoldIntentKnown)
                        try { storage.WritePhysicalGold(scope.Carrier, scope.RestoreGold); }
                        catch (Exception error) { gold = error; }
                    if (scope.GoodsIntentKnown)
                        try { storage.WritePhysicalGoods(scope.Carrier, scope.RestoreGoods); }
                        catch (Exception error) { goods = error; }
                    if (gold != null || goods != null)
                    {
                        Exception error = gold ?? goods!;
                        Invalidate(scope, error);
                        throw new InvalidOperationException("Cost scratch could not be removed before the original unload clear.", error);
                    }
                }
                try { storage.ClearConfiguredOverrides(); }
                catch (Exception error)
                {
                    if (scope != null) { Invalidate(scope, error); CaptureGoldIntent(scope); CaptureGoodsIntent(scope); }
                    throw;
                }
                if (scope != null) { CaptureGoldIntent(scope); CaptureGoodsIntent(scope); }
            }
        }

        internal NativeRecruitmentCostClose End(Scope scope)
        {
            if (scope == null || !ReferenceEquals(scope.Owner, this) ||
                scope.Thread != Environment.CurrentManagedThreadId || !Monitor.IsEntered(scope.Engine))
                return new NativeRecruitmentCostClose(false, false, true,
                    new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.WrongOwnerOrThread));
            lock (sync)
            {
                if (scope.Closed) return scope.CloseResult;
                if (!ReferenceEquals(active, scope))
                    return new NativeRecruitmentCostClose(false, false, true,
                        new NativeRecruitmentCostFailure(NativeRecruitmentCostFailureKind.WrongOwnerOrThread));
                return CloseLocked(scope);
            }
        }

        private NativeRecruitmentCostClose CloseLocked(Scope scope)
        {
            Exception? gold = scope.GoldIntentFailure, goods = scope.GoodsIntentFailure;
            // An unreadable post-setter component has UNKNOWN restore intent.
            // Never overwrite a possibly successful setter with a stale preimage.
            if (scope.GoldIntentKnown)
                try { storage.WritePhysicalGold(scope.Carrier, scope.RestoreGold); }
                catch (Exception error) { gold = error; }
            if (scope.GoodsIntentKnown)
                try { storage.WritePhysicalGoods(scope.Carrier, scope.RestoreGoods); }
                catch (Exception error) { goods = error; }
            bool restored = gold == null && goods == null;
            if (!restored) poisoned = true;
            scope.Invalidated |= !restored;
            scope.Closed = true;
            active = null;
            scope.CloseResult = new NativeRecruitmentCostClose(true, restored, scope.Invalidated,
                new NativeRecruitmentCostFailure(scope.Invalidated ? NativeRecruitmentCostFailureKind.InvalidatedScope :
                    NativeRecruitmentCostFailureKind.None, scope.FirstFailure, gold, goods));
            return scope.CloseResult;
        }

        private void CaptureGoldIntent(Scope scope)
        {
            try
            {
                scope.RestoreGold = storage.ReadPhysicalGold(scope.Carrier);
                scope.GoldIntentKnown = true;
                scope.GoldIntentFailure = null;
            }
            catch (Exception error)
            { scope.GoldIntentKnown = false; scope.GoldIntentFailure = error; Invalidate(scope, error); }
        }

        private void CaptureGoodsIntent(Scope scope)
        {
            try
            {
                scope.RestoreGoods = storage.ReadPhysicalGoods(scope.Carrier);
                scope.GoodsIntentKnown = true;
                scope.GoodsIntentFailure = null;
            }
            catch (Exception error)
            { scope.GoodsIntentKnown = false; scope.GoodsIntentFailure = error; Invalidate(scope, error); }
        }

        private static void Invalidate(Scope scope, Exception error)
        {
            scope.Invalidated = true;
            scope.Owner.poisoned = true;
            if (scope.FirstFailure == null) scope.FirstFailure = error;
        }

        internal sealed class Scope
        {
            internal readonly NativeRecruitmentCostOwner Owner;
            internal readonly object Engine;
            internal readonly int Carrier, Thread;
            internal int RestoreGold;
            internal NativeRecruitmentGoods RestoreGoods;
            internal bool Invalidated, Closed;
            internal bool GoldIntentKnown, GoodsIntentKnown;
            internal Exception? GoldIntentFailure, GoodsIntentFailure;
            internal Exception? FirstFailure;
            internal NativeRecruitmentCostClose CloseResult;
            internal Scope(NativeRecruitmentCostOwner owner, object engine, int carrier)
            { Owner = owner; Engine = engine; Carrier = carrier; Thread = Environment.CurrentManagedThreadId; }
        }
    }
}
