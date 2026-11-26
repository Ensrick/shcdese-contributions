#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using RedBird.Core.Memory.Managed;
using SHCDESE.API;

// Executes the production kernel and actual Core tracking containers over owned
// pinned buffers. Controlled leaf faults are explicitly fixture injections; this
// does not execute GameUnitManagerAPI, game code, a native purchase or a hook.
internal static unsafe class CostOwnerCases
{
    private static readonly NativeRecruitmentGoods Original = new NativeRecruitmentGoods(2, 0, 0, 0);
    private static readonly NativeRecruitmentGoods Scratch = new NativeRecruitmentGoods(3, 4, 0, 0);
    private static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }

    private sealed class UnjoinedWorker : Exception { }

    private sealed class Store : INativeRecruitmentCostStorage, IDisposable
    {
        private GCHandle goldPin, goodsPin;
        internal readonly ManagedNativeArray<int> Gold;
        internal readonly ManagedNativeArray<NativeRecruitmentGoods> Goods;
        internal readonly ManagedDictionary<int, int> GoldView;
        internal bool RetainPins;
        internal bool FailNextGoldRaw, FailNextGoodsRaw, FailGoldAfterDictionary, FailGoodsAfterSet, FailNextGoodsRead;
        internal bool FailGoodsCaptureAfterSet;
        internal int RawWrites;
        internal readonly Exception Injected = new InvalidOperationException("Controlled trusted-leaf fault, not a game failure.");
        internal Store()
        {
            int[] gold = new int[30]; gold[22] = 12;
            var goods = new NativeRecruitmentGoods[7]; goods[0] = Original;
            try
            {
                goldPin = GCHandle.Alloc(gold, GCHandleType.Pinned);
                goodsPin = GCHandle.Alloc(goods, GCHandleType.Pinned);
                Gold = new ManagedNativeArray<int>(goldPin.AddrOfPinnedObject(), gold.Length);
                Goods = new ManagedNativeArray<NativeRecruitmentGoods>(goodsPin.AddrOfPinnedObject(), goods.Length);
                GoldView = new ManagedDictionary<int, int>(new Dictionary<int, int> { [22] = 10 }, 0);
            }
            catch
            {
                if (goodsPin.IsAllocated) goodsPin.Free();
                if (goldPin.IsAllocated) goldPin.Free();
                throw;
            }
        }
        public int ReadPhysicalGold(int carrier) => Gold.GetBaseAddress().Pointer[carrier];
        public NativeRecruitmentGoods ReadPhysicalGoods(int carrier)
        {
            if (FailNextGoodsRead) { FailNextGoodsRead = false; throw Injected; }
            return Goods.GetBaseAddress().Pointer[carrier - 22];
        }
        public void WritePhysicalGold(int carrier, int value)
        {
            RawWrites++;
            if (FailNextGoldRaw) { FailNextGoldRaw = false; throw Injected; }
            Gold.GetBaseAddress().Pointer[carrier] = value;
        }
        public void WritePhysicalGoods(int carrier, NativeRecruitmentGoods value)
        {
            RawWrites++;
            if (FailNextGoodsRaw) { FailNextGoodsRaw = false; throw Injected; }
            Goods.GetBaseAddress().Pointer[carrier - 22] = value;
        }
        public void SetConfiguredGold(int carrier, int value)
        {
            GoldView.SetValue(carrier, value);
            if (FailGoldAfterDictionary) { FailGoldAfterDictionary = false; throw Injected; }
            Gold.SetValue(carrier, value);
        }
        public void SetConfiguredGoods(int carrier, NativeRecruitmentGoods value)
        {
            // Tagged eChimps is UInt16: preserve its original subtraction/wrap
            // for ordinary out-of-domain setters rather than validating scratch.
            int index = unchecked((ushort)(carrier - 22));
            Goods.SetValue(index, value);
            if (FailGoodsCaptureAfterSet) { FailNextGoodsRead = true; FailGoodsCaptureAfterSet = false; }
            if (FailGoodsAfterSet) { FailGoodsAfterSet = false; throw Injected; }
        }
        public void ClearConfiguredOverrides() { Goods.ClearOverrides(); GoldView.ClearOverrides(); }
        public void Dispose()
        {
            if (RetainPins) return; // Unjoined background worker forces immediate fixture failure/exit.
            try { try { Gold.Dispose(); } finally { Goods.Dispose(); } }
            finally { if (goodsPin.IsAllocated) goodsPin.Free(); if (goldPin.IsAllocated) goldPin.Free(); }
        }
    }

    private static NativeRecruitmentCostOwner.Scope Begin(NativeRecruitmentCostOwner owner, object engine)
    {
        Check(owner.TryBegin(engine, 22, new NativeRecruitmentCostSnapshot(100, Scratch), out var scope, out var failure),
            "Begin failed: " + failure.Kind);
        return scope!;
    }

    private static void Worker(Store store, Action action)
    {
        Exception? failure = null;
        int id = 0;
        Thread thread = new Thread(() => { id = Environment.CurrentManagedThreadId; try { action(); } catch (Exception e) { failure = e; } });
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(2000)) { store.RetainPins = true; throw new UnjoinedWorker(); }
        Check(id != Environment.CurrentManagedThreadId, "Expected a real dedicated foreign BCL thread.");
        if (failure != null) throw new InvalidOperationException("Foreign worker failed.", failure);
    }

    private static void DefaultsAndDomain()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s);
        Check(s.GoldView.GetValue(22) == 10 && s.Gold.GetValue(22) == 12, "Configured and physical gold were conflated.");
        o.SetGold(44, 71); o.SetGoods(0, Scratch);
        Check(s.GoldView.GetValue(44) == 71 && s.Gold.GetValue(22) == 12 && s.Goods.GetValue(0).Equals(Original),
            "Ordinary setter domains changed.");
        Check(!o.TryBegin(new object(), 22, new NativeRecruitmentCostSnapshot(100, Scratch), out _, out var f) &&
            f.Kind == NativeRecruitmentCostFailureKind.MissingEngineMonitor && s.RawWrites == 0, "Unlocked Begin wrote memory.");
    }

    private static void ReadAndRestoreWithoutSetter()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            var scope = Begin(o, engine);
            Check(o.TryRead(scope, out var value) && value.Gold == 100 && value.Goods.Equals(Scratch), "Physical copy differs.");
            Check(s.GoldView.GetValue(22) == 10 && s.Goods.GetValue(0).Equals(Scratch), "Public getter meanings changed.");
            var close = o.End(scope);
            Check(close.Closed && close.Restored && !close.Invalidated && s.Gold.GetValue(22) == 12 &&
                s.Goods.GetValue(0).Equals(Original), "No-setter scope failed restoration.");
        }
    }

    private static void EqualSetterAndRealOriginalTracking()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            var scope = Begin(o, engine); o.SetGold(22, 100); o.SetGoods(22, Scratch);
            Check(o.End(scope).Restored && s.Gold.GetValue(22) == 100 && s.Goods.GetValue(0).Equals(Scratch),
                "Explicit same-value setter intent was discarded.");
            // This is the REAL Core original/base snapshot, not an imitation.
            s.Gold.ResetToOriginal(); s.Goods.ResetToOriginal(); s.GoldView.ResetToOriginal();
            Check(s.Gold.GetValue(22) == 12 && s.Goods.GetValue(0).Equals(Original), "Scratch polluted first-touch original tracking.");
            Check(s.GoldView.GetValue(22) == 100, "Dictionary Reset was incorrectly made symmetric with array Reset.");
        }
    }

    private static void ChangedSetterAndOldClose()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        var changed = new NativeRecruitmentGoods(8, 9, 0, 0);
        lock (engine)
        {
            var first = Begin(o, engine); o.SetGold(22, 81); o.SetGoods(22, changed);
            Check(o.End(first).Restored && s.Gold.GetValue(22) == 81 && s.Goods.GetValue(0).Equals(changed), "Setter was clobbered.");
            var second = Begin(o, engine); int writes = s.RawWrites;
            Check(o.End(first).Restored && s.RawWrites == writes && s.Gold.GetValue(22) == 100, "Old close resurrected a prior row.");
            Check(o.End(second).Restored && s.Gold.GetValue(22) == 81, "Second scope did not retain its own preimage.");
        }
    }

    private static void RealOverlayClearAndInvalidation()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        var baseline = new NativeRecruitmentGoods(7, 0, 0, 0);
        s.Goods.SetBase(0, baseline); s.Goods.Push(0, new NativeRecruitmentGoods(9, 0, 0, 0));
        lock (engine)
        {
            var scope = Begin(o, engine); o.ClearForUnload();
            Check(!o.TryRead(scope, out _) && s.Goods.GetValue(0).Equals(baseline) && s.Gold.GetValue(22) == 12,
                "Clear adopted scratch or lost original gold-clear asymmetry.");
            var close = o.End(scope);
            Check(close.Restored && close.Invalidated && s.Goods.GetValue(0).Equals(baseline), "End resurrected pre-unload overlay.");
        }
        using var fault = new Store(); var failingOwner = new NativeRecruitmentCostOwner(fault); object otherEngine = new object();
        lock (otherEngine)
        {
            var scope = Begin(failingOwner, otherEngine); fault.FailNextGoldRaw = true;
            Exception? failure = null;
            try { failingOwner.ClearForUnload(); } catch (Exception error) { failure = error; }
            Check(failure != null && fault.Goods.GetValue(0).Equals(Original) && !failingOwner.TryRead(scope, out _),
                "First unmask error skipped independent goods restoration before End.");
            Check(failingOwner.End(scope).Restored && fault.Gold.GetValue(22) == 12, "Final independent restoration was lost.");
        }
    }

    private static void NestedAndForeignBegin()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            var scope = Begin(o, engine); int writes = s.RawWrites;
            Check(!o.TryBegin(engine, 22, new NativeRecruitmentCostSnapshot(1, Original), out _, out var same) &&
                same.Kind == NativeRecruitmentCostFailureKind.NestedScope, "Nested Begin replaced owner.");
            bool refused = false;
            Worker(s, () => { object other = new object(); lock (other) refused = !o.TryBegin(other, 22,
                new NativeRecruitmentCostSnapshot(1, Original), out _, out var f) && f.Kind == NativeRecruitmentCostFailureKind.NestedScope; });
            Check(refused && s.RawWrites == writes && o.TryRead(scope, out _), "Contender mutated current scope.");
            Check(o.End(scope).Restored, "Outer scope lost restoration.");
        }
    }

    private static void ForeignSetterIsNotHeldAcrossAction()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        var changed = new NativeRecruitmentGoods(11, 0, 0, 0);
        lock (engine)
        {
            var scope = Begin(o, engine);
            // The production Begin returned; this native/callback-shaped interval
            // must not retain the kernel gate. No game or native function runs.
            Worker(s, () => o.SetGoods(22, changed));
            Check(o.TryRead(scope, out var value) && value.Goods.Equals(changed), "Foreign supported setter did not complete.");
            Check(o.End(scope).Restored && s.Goods.GetValue(0).Equals(changed), "Foreign setter intent was lost.");
        }
    }

    private static void FailedBeginRestoresIndependentComponents()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            s.FailNextGoodsRaw = true;
            Check(!o.TryBegin(engine, 22, new NativeRecruitmentCostSnapshot(100, Scratch), out var scope, out var failure) &&
                scope == null && ReferenceEquals(failure.Primary, s.Injected) && failure.GoldRestore == null && failure.GoodsRestore == null,
                "Partial Begin failure was upgraded or masked.");
            Check(s.Gold.GetValue(22) == 12 && s.Goods.GetValue(0).Equals(Original), "Partial Begin left scratch.");
        }
    }

    private static void PartialGoldFailureRemainsUncertain()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            var scope = Begin(o, engine); s.FailGoldAfterDictionary = true; Exception? actual = null;
            try { o.SetGold(22, 91); } catch (Exception e) { actual = e; }
            Check(ReferenceEquals(actual, s.Injected) && s.GoldView.GetValue(22) == 91 && !o.TryRead(scope, out _),
                "Partial dictionary/native publication was treated as no effect.");
            var close = o.End(scope);
            Check(close.Restored && close.Invalidated && s.Gold.GetValue(22) == 12 && s.GoldView.GetValue(22) == 91,
                "Failure silently repaired managed state or lost physical restoration.");
            Check(!o.TryBegin(engine, 22, new NativeRecruitmentCostSnapshot(100, Scratch), out _, out var f) &&
                f.Kind == NativeRecruitmentCostFailureKind.PoisonedOwner, "Unknown owner automatically resumed.");
        }
    }

    private static void ControlledPostWriteFailureAndUnknownCapture()
    {
        foreach (bool loseCapture in new[] { false, true })
        {
            using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
            var changed = new NativeRecruitmentGoods(13, 0, 0, 0);
            lock (engine)
            {
                var scope = Begin(o, engine); s.FailGoodsAfterSet = true; s.FailGoodsCaptureAfterSet = loseCapture;
                Exception? actual = null;
                try { o.SetGoods(22, changed); } catch (Exception e) { actual = e; }
                Check(ReferenceEquals(actual, s.Injected) && !o.TryRead(scope, out _), "Controlled leaf failure was ignored.");
                var close = o.End(scope);
                Check(close.Invalidated && close.Restored == !loseCapture && s.Goods.GetValue(0).Equals(changed) && s.Gold.GetValue(22) == 12,
                    "Unknown intent was overwritten with stale preimage or claimed restored.");
            }
        }
    }

    private static void FailedEndAndWrongThread()
    {
        using var s = new Store(); var o = new NativeRecruitmentCostOwner(s); object engine = new object();
        lock (engine)
        {
            var scope = Begin(o, engine); NativeRecruitmentCostClose foreign = default;
            Worker(s, () => foreign = o.End(scope));
            Check(!foreign.Closed && s.Gold.GetValue(22) == 100, "Wrong thread closed/mutated the scope.");
            s.FailNextGoldRaw = true;
            var close = o.End(scope);
            Check(close.Closed && !close.Restored && ReferenceEquals(close.Failure.GoldRestore, s.Injected) &&
                s.Goods.GetValue(0).Equals(Original), "One restoration error skipped the other component.");
        }
    }

    private static void OwnedEntry(bool contextAvailable, bool invalidateAtDemand, int invalidateAtCallback = 0)
    {
        using var s = new Store(); var owner = new NativeRecruitmentCostOwner(s); object engine = new object();
        object world = new object(), publisher = new object();
        int calls = 0, admissions = 0, proofs = 0, tails = 0;
        var request = new EuropeanRecruitmentRequest(new IntPtr(123), 22, 41, 3);
        var bill = new EuropeanRecruitmentNativeBill(22, 100, 3, 4, 0, 0, 0);
        var requirement = new EuropeanRecruitmentFinalRequirement(new EuropeanRecruitmentFinalContext(
            publisher, world, engine, 1, true, true, true, false, false), bill, 76);
        var candidate = new EuropeanRecruitmentCandidate(37, 4001, 3, 22, 41, 76);
        EuropeanRecruitmentAttemptResult result;
        lock (engine)
        {
            result = EuropeanRecruitmentInvocation.InvokeWithOwnedCostsAndFinalValidation(request,
                value => {
                    admissions++;
                    if (invalidateAtCallback >= 2)
                    {
                        owner.ClearForUnload(); owner.SetGold(22, 100); owner.SetGoods(22, Scratch);
                        if (invalidateAtCallback == 3) throw new InvalidOperationException("Controlled admission after lifecycle invalidation.");
                        return false;
                    }
                    return true;
                }, engine, () => contextAvailable,
                (manager, carrier, origin, player, query) => {
                    calls++;
                    var tail = new EuropeanRecruitmentTailInputs(s.Gold.GetValue(22), 3, 4, 0, 0, true);
                    var branch = EuropeanRecruitmentInvocation.Dispatch(0, 37, 3,
                        _ => {
                            owner.SetGold(22, 100); owner.SetGoods(22, Scratch);
                            if (invalidateAtCallback == 1)
                            {
                                owner.ClearForUnload(); owner.SetGold(22, 100); owner.SetGoods(22, Scratch);
                            }
                        },
                        _ => candidate,
                        _ => {
                            proofs++;
                            var physical = new EuropeanRecruitmentNativeBill(22, s.Gold.GetValue(22),
                                s.Goods.GetValue(0).First, s.Goods.GetValue(0).Second, 0, 0, 0);
                            return new EuropeanRecruitmentFinalObservation(new EuropeanRecruitmentFinalContext(
                                publisher, world, engine, 1, true, true, true, false, false), candidate, physical, tail,
                                new EuropeanRecruitmentNativeResources(3, 200, 2, 2, 0, 0, 0, true, false), true, true);
                        });
                    if (branch == EuropeanRecruitmentBranch.Allow) { tails++; return 37; }
                    return 0;
                }, demand => { if (demand && invalidateAtDemand) owner.ClearForUnload(); }, requirement, owner);
        }
        if (!contextAvailable)
            Check(result.Kind == EuropeanRecruitmentAttemptKind.NotEntered && calls == 0 && s.RawWrites == 0,
                "Known unavailable context mutated scratch before refusal.");
        else if (invalidateAtDemand)
            Check(result.Kind == EuropeanRecruitmentAttemptKind.NotEntered && calls == 0 && admissions == 0 &&
                s.Gold.GetValue(22) == 12, "Known invalidated scope entered the callable after demand.");
        else if (invalidateAtCallback != 0)
            Check(result.Kind == EuropeanRecruitmentAttemptKind.UnknownNativeEffects && calls == 1 && tails == 0 &&
                admissions == (invalidateAtCallback == 1 ? 0 : 1) && proofs == (invalidateAtCallback == 1 ? 1 : 2) &&
                s.Gold.GetValue(22) == 100 && s.Goods.GetValue(0).Equals(Scratch),
                "Matching scalar data bypassed an invalid scope or issued a safe-refusal certificate after admission.");
        else
            Check(result.Kind == EuropeanRecruitmentAttemptKind.NativeReturned && calls == 1 && admissions == 1 && proofs == 2 && tails == 1 &&
                s.Gold.GetValue(22) == 100 && s.Goods.GetValue(0).Equals(Scratch), "Owned composition did not retain both proofs/setter intent.");
    }

    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2) throw new ArgumentException("Supply reference mode and expected Core identity.");
        var provider = typeof(ManagedNativeArray<int>).Assembly;
        string loadedHash;
        using (var input = System.IO.File.OpenRead(provider.Location))
        using (var hash = System.Security.Cryptography.SHA256.Create())
            loadedHash = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        bool sourceMode = String.Equals(arguments[0], "Source", StringComparison.OrdinalIgnoreCase);
        bool binaryMode = String.Equals(arguments[0], "Binary", StringComparison.OrdinalIgnoreCase);
        if ((!sourceMode && !binaryMode) ||
            (sourceMode && provider != typeof(CostOwnerCases).Assembly) ||
            (binaryMode && (provider.GetName().Name != "RedBird.Core" || loadedHash != arguments[1])))
            throw new InvalidOperationException("Loaded Core provider does not match the admitted reference mode.");
        Console.WriteLine("CORE_PROVIDER " + provider.FullName + " | " + provider.ManifestModule.ModuleVersionId + " | " + loadedHash);
        Console.WriteLine("RUNTIME " + Environment.Version + " | pointerBytes=" + IntPtr.Size);
        (string, Action)[] cases = {
            ("DefaultViewsAndOrdinarySetterDomain", DefaultsAndDomain),
            ("PhysicalReadAndNoSetterRestoration", ReadAndRestoreWithoutSetter),
            ("SameValueIntentAndActualCoreFirstTouchReset", EqualSetterAndRealOriginalTracking),
            ("ChangedIntentAndOldCloseCannotResurrect", ChangedSetterAndOldClose),
            ("ActualOverlayClearInvalidatesAndUnmasks", RealOverlayClearAndInvalidation),
            ("NestedAndContendingBeginRefuseBeforeWrite", NestedAndForeignBegin),
            ("ForeignSetterCompletesDuringCallerInterval", ForeignSetterIsNotHeldAcrossAction),
            ("PartialBeginRestoresIndependentComponents", FailedBeginRestoresIndependentComponents),
            ("GoldDictionaryThenPhysicalFailureIsUncertain", PartialGoldFailureRemainsUncertain),
            ("ControlledPostWriteFailureAndUnknownIntent", ControlledPostWriteFailureAndUnknownCapture),
            ("WrongThreadAndIndependentEndFailures", FailedEndAndWrongThread),
            ("ActualOwnedEntryRetainsBothChecksAndIntent", () => {
                OwnedEntry(true, false); OwnedEntry(true, false, 1); OwnedEntry(true, false, 2); OwnedEntry(true, false, 3);
            }),
            ("ActualOwnedEntryPreflightBeforeScratch", () => OwnedEntry(false, false)),
            ("ActualOwnedEntryDemandInvalidationBeforeCallable", () => OwnedEntry(true, true))
        };
        int failed = 0;
        foreach (var item in cases)
        {
            try { item.Item2(); Console.WriteLine("PASS " + item.Item1); }
            catch (UnjoinedWorker) { Console.Error.WriteLine("FAIL unjoined owned background worker; pins retained until process exit."); return 2; }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + item.Item1 + ": " + error.Message); }
        }
        Console.WriteLine("Cost owner groups=" + cases.Length + "; failed=" + failed + "; no game/native/installation authority.");
        return failed == 0 ? 0 : 1;
    }
}
