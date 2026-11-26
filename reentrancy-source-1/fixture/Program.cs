#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SHCDESE.API;
using SHCDESE.Detours;
using Iced.Intel;
using Microsoft.Extensions.Logging.Abstractions;
using RedBird.X64.Assembly;
using RedBird.X64.Extensions;
using RedBird.X64.Hooks;
using RedBird.X64.Hooks.Context;
using static Iced.Intel.AssemblerRegisters;

// Actual candidate scope/dispatcher and public RedBird generators. Machine
// cases use an exclusively owned64KiB page. No game/SE assembly reference,
// PE read, installed hook, native recruitment, selector or payment model.
internal static unsafe partial class Program
{
    private static readonly byte[] Prefix = { 0x48,0x63,0xC7,0x48,0x69,0xC8,0x90,0x04,0,0,0x48,0x03,0xD9,0x33,0xC9 };
    private static readonly AssemblerRegister64[] Gp = { rax,rcx,rdx,rbx,rsp,rbp,rsi,rdi,r8,r9,r10,r11,r12,r13,r14,r15 };
    private static readonly AssemblerRegisterXMM[] Simd = { xmm0,xmm1,xmm2,xmm3,xmm4,xmm5,xmm6,xmm7,xmm8,xmm9,xmm10,xmm11,xmm12,xmm13,xmm14,xmm15 };
    private const ulong FinalR9 = 0x123400000000004CUL;
    private const ulong SeedFlags = 0xAD7; // Defined user arithmetic flags; no TF/privileged flags.
    private const ulong PreservedFlags = 0xCD5; // CF/PF/AF/ZF/SF/DF/OF.
    private const ulong XorFlags = 0xCC5; // XOR leaves AF architecturally undefined.
    private const ulong SimdSeed = 0x61728394A5B6C700UL, LeafSeed = 0xBADC0FFEE0000000UL;
    private const ulong Guard = 0x7162534455667788UL;
    private const int ContinueMarker = 1, AbortMarker = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NativeEntry();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ClobberLeaf();
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

    private static int Main(string[] arguments)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8)
        { Console.Error.WriteLine("Windows x64 is required; no skip credit."); return 2; }
        if (arguments.Length != 1) throw new ArgumentException("Supply the admitted public reference manifest.");
        using (var admitted = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(arguments[0])))
        {
            var providers = new[] {
                typeof(Iced.Intel.Assembler).Assembly,
                typeof(RedBird.Core.Memory.NativeStateBlock).Assembly,
                typeof(RedBird.Abstractions.Hooks.Transaction.HookHandle<RedBird.X64.Hooks.X64InlineHook>).Assembly,
                typeof(RedBird.X64.Hooks.X64InlineHook).Assembly,
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly
            };
            foreach (var expected in admitted.RootElement.GetProperty("publicFrameReferences").EnumerateArray())
            {
                string name = expected.GetProperty("name").GetString()!;
                var provider = providers.Single(item => item.GetName().Name + ".dll" == name);
                string actual;
                using (var input = System.IO.File.OpenRead(provider.Location))
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    actual = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (actual != expected.GetProperty("sha256").GetString())
                    throw new InvalidOperationException("Loaded public frame provider identity differs: " + name);
                Console.WriteLine("FRAME_PROVIDER " + name + " | " + provider.ManifestModule.ModuleVersionId + " | " + actual);
            }
        }
        Console.WriteLine("RUNTIME " + Environment.Version + " | pointerBytes=" + IntPtr.Size);
        var cases = ManagedCases().Concat(FinalAdmissionCases()).Concat(new (string Name, Action Body)[] {
            ("Stub.DormantMatchesIndependentRealRedBirdBaseline", () => LegacyRoute(false)),
            ("Stub.DemandWithoutOwnedScopeUsesLegacyOnce", () => LegacyRoute(true)),
            ("Stub.OwnedAllowRestoresStateThenReplays", () => Active(true, false)),
            ("Stub.OwnedRefusalRestoresStateBeforeAbort", () => Active(false, false)),
            ("Stub.OwnedPredicateExceptionRestoresStateBeforeAbort", () => Active(false, true)),
            ("Stub.InvalidPrefixAndBoundsEmitNothing", InvalidInputs),
            ("Stub.OwnedRecursiveGeneratedFrameAbortsWithoutInnerTail", RecursiveGeneratedFrame),
            ("Stub.GuardSuppressedPreDispatchFaultDefaultsToAbort", GuardSuppressedFrameFault),
            ("Trace.OwnedAllowRecordsExactSchedulingStores", TraceAllowed),
            ("Trace.DormantAndNoScopeMatchIndependent63ByteBaseline", TraceLegacyRoutes),
            ("Trace.RejectionLeavesTraceAndRecordsUntouched", TraceRejected),
            ("Trace.RecursiveDispatchPreventsOuterCompletion", TraceNested),
            ("Trace.RepeatedOrTamperedCellRefusesScheduling", TraceInvalidCells),
            ("Trace.InvalidDisplacementEmitsNothing", TraceInvalidGenerators)
        }).ToArray();
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error)
            { failures.Add(test.Name); Console.WriteLine("FAIL " + test.Name + " :: " + error.GetType().Name + ": " + error.Message); }
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
            schema="european-recruitment-admission-scope-and-stub/2", totalCases=cases.Length,
            failedNames=failures, rawExit=failures.Count == 0 ? 0 : 1,
            scope="Actual candidate scope/dispatcher and RedBird frames on owned pages only; no game function, installed SE, recruitment/payment, lifecycle backend, AVX-upper or unwind qualification"
        }));
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal(ulong expected, ulong actual, string message)
    { Check(expected == actual, message + "; expected=0x" + expected.ToString("X") + ", actual=0x" + actual.ToString("X")); }
    private static ulong SeedGp(int index) => index == 7 ? 37UL : index == 5 ? 3UL : 0x1111000000000000UL + (ulong)index * 0x100UL;
    private static Instruction[] Decode(byte[] bytes, ulong site)
    {
        var reader = new ByteArrayCodeReader(bytes);
        var decoder = Decoder.Create(64, reader); decoder.IP = site;
        var result = new List<Instruction>();
        while (reader.CanReadByte) { decoder.Decode(out Instruction instruction); result.Add(instruction); }
        return result.ToArray();
    }

    private static void LegacyRoute(bool demand)
    {
        using var baseline = new Fixture(false, true);
        using var proposed = new Fixture(demand, false);
        baseline.CollectAndCheckRoots(); proposed.CollectAndCheckRoots();
        Check(!EuropeanRecruitmentInvocation.HasOwnedScope, "Previous case leaked TLS.");
        baseline.Run(false); proposed.Run(false);
        CheckCommon(baseline, false); CheckCommon(proposed, false);
        Check(baseline.Marker == ContinueMarker && proposed.Marker == ContinueMarker, "Legacy continuation changed.");
        Check(baseline.LegacyCalls == 1 && proposed.LegacyCalls == 1 && proposed.ActiveCalls == 0,
            "Demand alone must deliver legacy once without owned dispatch/admission.");
        for (int i = 0; i < Gp.Length; i++)
            if (i != 4) Equal(baseline.Register(i), proposed.Register(i), "Independent legacy comparison: " + Gp[i]);
        Equal(baseline.Flags & XorFlags, proposed.Flags & XorFlags, "Defined legacy replay flags differ.");
        // Legacy has no flag/XMM preservation. Its real full-prefix baseline,
        // rather than entry seeds, defines dormant and no-owned-scope behavior.
        CheckSimd(proposed, 6);
    }

    private static void Active(bool allow, bool predicateThrows)
    {
        foreach (bool direction in new[] { false, true })
        {
            using var fixture = new Fixture(true, false);
            fixture.CollectAndCheckRoots();
            var result = fixture.RunScoped(direction, allow, predicateThrows);
            CheckCommon(fixture, true);
            bool continued = allow && !predicateThrows;
            Outcome(result, continued ? EuropeanRecruitmentAttemptKind.NativeReturned : EuropeanRecruitmentAttemptKind.RejectedBeforeConversion,
                predicateThrows ? EuropeanRecruitmentFailure.AdmissionFailed : allow ? EuropeanRecruitmentFailure.None : EuropeanRecruitmentFailure.AdmissionRejected);
            Check(fixture.Marker == (continued ? ContinueMarker : AbortMarker), "Wrong owned exit.");
            Check(fixture.LegacyCalls == 1 && fixture.ActiveCalls == 1 && fixture.AdmissionCalls == 1 && fixture.NativeCalls == 1,
                "Actual Core must deliver legacy then admission exactly once.");
            Check(fixture.AdmissionSawFinalCandidate && fixture.ActiveFlagValue == 0 && !EuropeanRecruitmentInvocation.HasOwnedScope,
                "Candidate binding, demand retirement or TLS cleanup failed.");
            Check(fixture.CapturedTailOperandRegisters,
                "The opt-in active context did not capture the R12/R13/R14 native bill operand registers.");
            if (predicateThrows) Check(result.ErrorMessage == "Owned predicate failure.", "Unrelated swallowed assertion replaced intended failure.");
            for (int i = 0; i < Gp.Length; i++)
            {
                if (i == 4) continue;
                ulong expected = i == 9 ? FinalR9 : SeedGp(i);
                if (continued && i == 0) expected = 37;
                if (continued && i == 1) expected = 0;
                if (continued && i == 3) expected = unchecked(SeedGp(3) + 37UL * 0x490UL);
                Equal(expected, fixture.Register(i), "Active machine value: " + Gp[i]);
            }
            ulong entry = SeedFlags | (direction ? 0x400UL : 0);
            if (continued) Equal(0x44UL | (direction ? 0x400UL : 0), fixture.Flags & XorFlags, "Defined XOR replay flags.");
            else Equal(entry & PreservedFlags, fixture.Flags & PreservedFlags, "Refusal must restore flags before abort.");
            CheckSimd(fixture, 0);
        }
    }

    private static void CheckCommon(Fixture fixture, bool active, bool leafExecuted = true)
    {
        Equal(fixture.EntryRsp, fixture.ExitRsp, "The exact native aperture RSP was not restored.");
        Equal(0, fixture.EntryRsp & 15, "The synthetic interior aperture is aligned.");
        Equal(fixture.EntryRsp, fixture.ContextNativeRsp, "Public context.RSP did not reconstruct the original native RSP.");
        Equal(fixture.EntryRsp - (active ? 112UL : 0), fixture.ContextRawRsp, "The wrapped frame's public RSP contract changed.");
        if (leafExecuted)
        {
            Equal(8, fixture.LeafRsp & 15, "The owned clobber leaf's Windows ABI entry alignment changed.");
            Equal(0, fixture.LeafFlags & 0x400, "The clobber leaf called from managed code must enter with DF clear.");
            Check(fixture.LeafCalls == 1, "The owned ABI leaf must really execute.");
            for (int i = 0; i < 6; i++)
            {
                Equal(LeafSeed + (ulong)i, fixture.LeafSimd(i, 0), "The callback leaf did not overwrite its volatile SIMD low lane.");
                Equal(LeafSeed + (ulong)i, fixture.LeafSimd(i, 1), "The callback leaf did not overwrite its volatile SIMD high lane.");
            }
        }
        else Check(fixture.LeafCalls == 0, "A denied inner frame unexpectedly reached the legacy clobber leaf.");
        Equal(Guard, fixture.FirstGuard, "Low native scratch guard changed.");
        Equal(Guard, fixture.LastGuard, "High native scratch guard changed.");
        fixture.CheckRootUntouched();
        // LeafRsp/LeafFlags describe the leaf entered FROM managed code, not the
        // ContextStub -> CLR thunk's first instruction. This is not unwind or
        // arbitrary CLR-transition/exception safety qualification.
    }

    private static void CheckSimd(Fixture fixture, int first)
    {
        for (int i = first; i < Simd.Length; i++)
        {
            Equal(SimdSeed + (ulong)i, fixture.Vector(i, 0), "SIMD low128 low lane: " + i);
            Equal(~(SimdSeed + (ulong)i), fixture.Vector(i, 1), "SIMD low128 high lane: " + i);
        }
    }

    // Actual nested generated frames on different owned pages. The inner raw Run
    // observes outer TLS while Dispatching; it does not enter/reset another Invoke.
    private static void RecursiveGeneratedFrame()
    {
        foreach (bool direction in new[] { false, true })
        {
            using var outer = new Fixture(true, false);
            using var inner = new Fixture(true, false);
            outer.CollectAndCheckRoots(); inner.CollectAndCheckRoots();
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope, "Previous case leaked TLS.");
            outer.DuringLegacy = () => {
                Check(EuropeanRecruitmentInvocation.HasOwnedScope && outer.ActiveFlagValue == 1,
                    "Nested generated call did not run inside the active outer legacy callback.");
                inner.Run(direction);
            };
            var result = outer.RunScoped(direction, true, false);
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedSelection);
            Check(outer.Marker == AbortMarker && inner.Marker == AbortMarker,
                "A recursive TLS-owned generated frame escaped through a native continuation.");
            Check(outer.LegacyCalls == 1 && outer.ActiveCalls == 1 && outer.AdmissionCalls == 0 && outer.NativeCalls == 1,
                "Outer generated reentry lost legacy pairing or reached admission.");
            Check(inner.ActiveFlagValue == 1 && inner.ActiveCalls == 1 && inner.LegacyCalls == 0 &&
                inner.AdmissionCalls == 0 && inner.NativeCalls == 0,
                "Inner raw generated frame borrowed legacy, admission or another native callable.");
            Check(outer.ActiveFlagValue == 0 && !EuropeanRecruitmentInvocation.HasOwnedScope,
                "Outer failed scope did not retire demand and TLS.");
            CheckAbortFrame(outer, direction, SeedGp(0), FinalR9, true);
            CheckAbortFrame(inner, direction, SeedGp(0), SeedGp(9), false);
        }
    }

    // Bypass only CreateActiveFrame's managed catch to isolate its outer machine
    // default. The actual public Guard suppresses this controlled callback throw;
    // the actual public CreateGenerator and production outer Generate are retained.
    private static void GuardSuppressedFrameFault()
    {
        foreach (bool direction in new[] { false, true })
        {
            using var fixture = new Fixture(true, false, guardBeforeDispatch: true);
            fixture.CollectAndCheckRoots();
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope, "Guard-isolation case inherited TLS.");
            fixture.Run(direction, incomingRax: 1);
            Check(fixture.GuardCallbackCalls == 1 && fixture.ActiveCalls == 1,
                "Actual Guard did not execute the controlled pre-Dispatch throwing callback once.");
            Equal(0, fixture.GuardIncomingRax, "Outer Generate did not default the active result before Guard.");
            Check(fixture.Marker == AbortMarker && fixture.LegacyCalls == 0 && fixture.AdmissionCalls == 0 && fixture.NativeCalls == 0,
                "Suppressed pre-Dispatch exception inherited allow or reached legacy/admission/tail.");
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope && fixture.ActiveFlagValue == 1,
                "Raw guard-isolation case invented or consumed an owned scope.");
            CheckAbortFrame(fixture, direction, 1, SeedGp(9), true);
        }
    }

    private static void CheckAbortFrame(Fixture fixture, bool direction, ulong incomingRax, ulong expectedR9, bool leafExecuted)
    {
        CheckCommon(fixture, true, leafExecuted);
        for (int i = 0; i < Gp.Length; i++)
        {
            if (i == 4) continue;
            ulong expected = i == 0 ? incomingRax : i == 9 ? expectedR9 : SeedGp(i);
            Equal(expected, fixture.Register(i), "Denied generated frame machine value: " + Gp[i]);
        }
        ulong entry = SeedFlags | (direction ? 0x400UL : 0);
        Equal(entry & PreservedFlags, fixture.Flags & PreservedFlags, "Denied generated frame did not restore original flags.");
        CheckSimd(fixture, 0);
    }

    // Trace fixtures own exactly one64KiB code page and one64KiB data page.
    // Sequential baseline snapshots avoid having two trace fixtures alive together.
    private sealed class TraceLegacySnapshot
    {
        internal ulong[] Registers = new ulong[16], Vectors = new ulong[32];
        internal ulong Flags;
    }
    private static void CheckTraceMachine(Fixture fixture, bool direction, bool activeFrame, bool scheduled)
    {
        CheckCommon(fixture, activeFrame);
        for (int i = 0; i < Gp.Length; i++)
        {
            if (i == 4) continue;
            ulong expected = i == 0 && scheduled ? fixture.ExpectedSliceRax :
                i == 1 && scheduled ? 0 : i == 3 ? (scheduled ? fixture.RecordAddress : fixture.RecordManager) :
                i == 9 ? FinalR9 : SeedGp(i);
            Equal(expected, fixture.Register(i), "Trace frame register: " + Gp[i]);
        }
        if (scheduled) Equal(0x44UL | (direction ? 0x400UL : 0), fixture.Flags & XorFlags,
            "Trace writes changed the scheduling slice's defined replay flags.");
        else Equal((SeedFlags | (direction ? 0x400UL : 0)) & PreservedFlags, fixture.Flags & PreservedFlags,
            "Trace refusal did not restore incoming flags.");
        CheckSimd(fixture, activeFrame ? 0 : 6);
    }
    private static void TraceAllowed()
    {
        foreach (bool direction in new[] { false, true })
        {
            using var fixture = new Fixture(true, false, traceMode: true);
            fixture.CollectAndCheckRoots();
            ulong token = fixture.TraceToken;
            Check(token != 0 && fixture.TraceState == 0, "Trace did not start with a fresh correlated cell.");
            var result = fixture.RunScoped(direction, true, false);
            Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
            Check(fixture.Marker == ContinueMarker && fixture.LegacyCalls == 1 && fixture.ActiveCalls == 1 &&
                fixture.AdmissionCalls == 1 && fixture.NativeCalls == 1,
                "Trace allow changed the existing once-only admission/native order.");
            Check(fixture.TraceState == EuropeanRecruitmentAdmissionStub.TraceCompleted && fixture.TraceToken == token,
                "Scheduling completion or caller-owned trace token changed.");
            Equal(fixture.RecordAddress, fixture.TraceRecord, "Trace did not capture the exact owned native record address.");
            Check(fixture.TraceUnitSlot == 37 && fixture.TraceNextType == 76,
                "Trace did not capture native EDI/R9D scheduling operands.");
            fixture.CheckRecordStores(true); CheckTraceMachine(fixture, direction, true, true);
            Check(fixture.ActiveFlagValue == 0 && !EuropeanRecruitmentInvocation.HasOwnedScope,
                "Trace allow did not retire demand/TLS.");
        }
    }
    private static TraceLegacySnapshot ObserveTraceLegacy(bool active, bool independent, bool direction)
    {
        using var fixture = new Fixture(active, independent, traceMode: true);
        fixture.CollectAndCheckRoots();
        byte[] before = fixture.SnapshotTrace(); fixture.Run(direction);
        Check(fixture.Marker == ContinueMarker && fixture.LegacyCalls == 1 && fixture.ActiveCalls == 0 &&
            fixture.AdmissionCalls == 0 && fixture.NativeCalls == 0,
            "Dormant/no-owned trace route changed ordinary legacy publication.");
        Check(fixture.SnapshotTrace().SequenceEqual(before), "Dormant/no-owned route wrote the trace cell.");
        fixture.CheckRecordStores(true); CheckTraceMachine(fixture, direction, false, true);
        var result = new TraceLegacySnapshot { Flags = fixture.Flags & XorFlags };
        for (int i = 0; i < Gp.Length; i++)
            if (i != 4) result.Registers[i] = i == 3 ? fixture.Register(i) - fixture.RecordManager : fixture.Register(i);
        // Legacy's volatile SIMD values have no preservation promise. Compare its
        // actual independent baseline for nonvolatile low128 only, as in old cases.
        for (int i = 6; i < Simd.Length; i++)
        { result.Vectors[i * 2] = fixture.Vector(i, 0); result.Vectors[i * 2 + 1] = fixture.Vector(i, 1); }
        return result;
    }
    private static void TraceLegacyRoutes()
    {
        // The existing unwrapped legacy baseline is qualified with DF clear.
        // Incoming DF=true is tested only behind the active wrapper's CLD path.
        foreach (bool direction in new[] { false })
        {
            TraceLegacySnapshot baseline = ObserveTraceLegacy(false, true, direction);
            foreach (bool active in new[] { false, true })
            {
                TraceLegacySnapshot proposed = ObserveTraceLegacy(active, false, direction);
                Check(baseline.Registers.SequenceEqual(proposed.Registers) && baseline.Flags == proposed.Flags &&
                    baseline.Vectors.SequenceEqual(proposed.Vectors),
                    "Trace dormant/no-TLS path differs from independent actual RedBird ten-instruction baseline.");
            }
        }
        TraceForeignNoScope();
    }
    private static void TraceForeignNoScope()
    {
        // Main owns TLS; this one foreign raw generated call sees no TLS despite
        // the active native flag. It may follow Vanilla but cannot forge our cell.
        var fixture = new Fixture(true, false, traceMode: true);
        Thread worker = null; Exception workerError = null; bool foreignNoTls = false;
        try
        {
            byte[] trace = fixture.SnapshotTrace();
            var outer = new ScopeCase();
            var result = outer.Run(candidate => false, current => {
                Check(EuropeanRecruitmentInvocation.HasOwnedScope, "Main thread did not own the outer attempt.");
                worker = new Thread(() => {
                    try { foreignNoTls = !EuropeanRecruitmentInvocation.HasOwnedScope; fixture.Run(false); }
                    catch (Exception error) { workerError = error; }
                }) { IsBackground = true };
                worker.Start();
                if (!worker.Join(3000)) ExitRetainingTracePages(fixture, "Foreign trace worker exceeded its bounded join.");
                if (workerError != null) throw new InvalidOperationException("Foreign raw trace fixture failed.", workerError);
                current.Dispatch(); return 0;
            });
            Outcome(result, EuropeanRecruitmentAttemptKind.RejectedBeforeConversion, EuropeanRecruitmentFailure.AdmissionRejected);
            Check(foreignNoTls && fixture.ActiveFlagValue == 1 && fixture.ActiveCalls == 0 && fixture.LegacyCalls == 1 &&
                fixture.AdmissionCalls == 0 && fixture.NativeCalls == 0 && fixture.Marker == ContinueMarker,
                "Actual foreign no-TLS frame borrowed the outer admission or changed its ordinary route.");
            Check(fixture.SnapshotTrace().SequenceEqual(trace) && fixture.TraceState == 0,
                "A foreign unowned call forged the main attempt's Entered/Completed cell.");
            fixture.CheckRecordStores(true); CheckTraceMachine(fixture, false, false, true);
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope && worker.Join(0), "Foreign trace did not close or main TLS leaked.");
        }
        finally
        {
            // No timeout assertion may unwind through native page disposal while
            // the worker can still execute them. A failed join cannot become PASS.
            if (worker?.IsAlive == true) ExitRetainingTracePages(fixture, "Foreign worker remains live during trace cleanup.");
            fixture.Dispose();
        }
    }
    private static void ExitRetainingTracePages(Fixture fixture, string message)
    {
        Console.Error.WriteLine("FAIL Trace.ForeignNoScope :: " + message);
        GC.KeepAlive(fixture); // The worker closure also retains its fixture/delegates.
        Environment.Exit(2); // Owned fixture process only; OS teardown releases the pages.
        throw new InvalidOperationException("Owned process exit unexpectedly returned.");
    }
    private static void TraceRejected()
    {
        foreach (bool direction in new[] { false, true })
        foreach (bool predicateThrows in new[] { false, true })
        {
            using var fixture = new Fixture(true, false, traceMode: true);
            byte[] trace = fixture.SnapshotTrace();
            var result = fixture.RunScoped(direction, false, predicateThrows);
            Outcome(result, EuropeanRecruitmentAttemptKind.RejectedBeforeConversion,
                predicateThrows ? EuropeanRecruitmentFailure.AdmissionFailed : EuropeanRecruitmentFailure.AdmissionRejected);
            Check(fixture.Marker == AbortMarker && fixture.LegacyCalls == 1 && fixture.ActiveCalls == 1 && fixture.AdmissionCalls == 1,
                "Trace rejection changed legacy/admission pairing.");
            Check(fixture.SnapshotTrace().SequenceEqual(trace), "Rejected admission wrote the trace cell.");
            fixture.CheckRecordStores(false); CheckTraceMachine(fixture, direction, true, false);
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope && fixture.ActiveFlagValue == 0, "Rejected trace leaked TLS/demand.");
        }
    }
    private static void TraceNested()
    {
        // This new63-byte case uses a direct recursive Dispatch to keep exactly
        // two owned pages. The old38 retain separate-page generated reentry proof.
        using var fixture = new Fixture(true, false, traceMode: true);
        int innerLegacy = 0, innerReads = 0;
        EuropeanRecruitmentBranch branch = EuropeanRecruitmentBranch.Allow;
        fixture.DuringLegacy = () => {
            branch = EuropeanRecruitmentInvocation.Dispatch(IntPtr.Zero, 37, 3,
                frame => innerLegacy++, frame => { innerReads++; return default; });
        };
        byte[] trace = fixture.SnapshotTrace();
        var result = fixture.RunScoped(false, true, false);
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedSelection);
        Check(branch == EuropeanRecruitmentBranch.Abort && innerLegacy == 0 && innerReads == 0 &&
            fixture.Marker == AbortMarker && fixture.AdmissionCalls == 0,
            "Recursive dispatch reached a nested callback, outer admission or scheduling continuation.");
        Check(fixture.SnapshotTrace().SequenceEqual(trace), "Poisoned outer call wrote trace completion.");
        fixture.CheckRecordStores(false); CheckTraceMachine(fixture, false, true, false);
        Check(!EuropeanRecruitmentInvocation.HasOwnedScope && fixture.ActiveFlagValue == 0, "Nested trace leaked TLS/demand.");
    }
    private static void TraceInvalidCells()
    {
        foreach (int state in new[] { 0, EuropeanRecruitmentAdmissionStub.TraceEntered,
            EuropeanRecruitmentAdmissionStub.TraceCompleted, EuropeanRecruitmentAdmissionStub.TraceTainted })
        {
            using var fixture = new Fixture(true, false, traceMode: true);
            fixture.InitializeTraceCell(state, state == 0 ? 0UL : 0x1234UL);
            byte[] before = fixture.SnapshotTrace();
            var result = fixture.RunScoped(false, true, false);
            Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedNativeReturn);
            Check(fixture.Marker == AbortMarker && fixture.AdmissionCalls == 1 &&
                fixture.TraceState == (state | EuropeanRecruitmentAdmissionStub.TraceTainted),
                "Invalid trace phase/token did not taint/refuse after entry authorization.");
            byte[] expected = (byte[])before.Clone();
            Array.Copy(BitConverter.GetBytes(state | EuropeanRecruitmentAdmissionStub.TraceTainted), 0, expected, 0, 4);
            Check(fixture.SnapshotTrace().SequenceEqual(expected), "Invalid trace mutated fields beyond its taint status.");
            fixture.CheckRecordStores(false); CheckTraceMachine(fixture, false, true, false);
        }
        using var repeated = new Fixture(true, false, traceMode: true);
        var first = repeated.RunScoped(false, true, false);
        Outcome(first, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
        repeated.CheckRecordStores(true);
        repeated.ChangeRecordSentinel(); byte[] record = repeated.SnapshotRecord(), traceBefore = repeated.SnapshotTrace();
        var second = repeated.RunScoped(false, true, false);
        Outcome(second, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedNativeReturn);
        Check(repeated.Marker == AbortMarker && repeated.TraceState ==
            (EuropeanRecruitmentAdmissionStub.TraceCompleted | EuropeanRecruitmentAdmissionStub.TraceTainted),
            "Repeated uncleared cell was reused as a fresh scheduling trace.");
        byte[] traceExpected = (byte[])traceBefore.Clone();
        Array.Copy(BitConverter.GetBytes(EuropeanRecruitmentAdmissionStub.TraceCompleted |
            EuropeanRecruitmentAdmissionStub.TraceTainted), 0, traceExpected, 0, 4);
        Check(repeated.SnapshotTrace().SequenceEqual(traceExpected) && repeated.SnapshotRecord().SequenceEqual(record),
            "Repeated trace reran scheduling stores or replaced the original receipt.");
        CheckTraceMachine(repeated, false, true, false);
        Check(!EuropeanRecruitmentInvocation.HasOwnedScope && repeated.ActiveFlagValue == 0, "Repeated trace leaked TLS/demand.");
    }
    private static void TraceInvalidGenerators()
    {
        using var fixture = new Fixture(false, false, traceMode: true);
        fixture.CheckInvalidTraceGenerators();
    }

    private static void InvalidInputs()
    {
        using var fixture = new Fixture(false, false);
        fixture.CheckInvalidGenerators(); fixture.CheckRootUntouched();
    }

    private static (string Name, Action Body)[] ManagedCases() => new (string, Action)[] {
        ("Scope.InvalidBoundaryNeverPublishesDemandOrCallsOriginal", InvalidBoundary),
        ("Scope.NativeArgumentsAndFinalCandidateFollowOwnedOrder", OrderedScope),
        ("Scope.RawZeroWithoutSelectionIsNotARejectionCertificate", RawZero),
        ("Scope.CoherentAllowRequiresTheSelectedNativeReturn", AllowReturn),
        ("Scope.FalseAndPredicateExceptionCanCertifyOnlyCoherentAbort", CertifiedRefusal),
        ("Scope.RejectionNeedsNormalZeroNativeReturn", RejectReturn),
        ("Scope.DuplicateAndRecursiveSitesCannotCertify", DuplicateSites),
        ("Scope.CandidateMismatchAndPostAdmissionMutationAreUnknown", CandidateMismatch),
        ("Scope.ContextChangesCannotCertifyRejection", ChangedContext),
        ("Scope.LegacyReadAndFrameFaultsRemainUnknown", DispatcherFaults),
        ("Scope.NestedOwnedCallCannotReplaceOuterAttempt", NestedAttempt),
        ("Scope.RealForeignThreadCannotBorrowOwnedAttempt", ForeignThread),
        ("Scope.DemandPublicationAndRetirementFaultsClearTls", DemandFaults),
        ("Scope.TerminalResultsRemainImmutableAfterLaterCalls", ImmutableResults)
    };

    private static void Outcome(EuropeanRecruitmentAttemptResult result,
        EuropeanRecruitmentAttemptKind kind, EuropeanRecruitmentFailure failure, bool invoked = true)
    {
        Check(result != null && result.Kind == kind && result.Failure == failure && result.NativeInvoked == invoked,
            "Unexpected outcome: " + result?.Kind + "/" + result?.Failure + "/" + result?.NativeInvoked);
    }

    // Scalar observations only; no native record layout, selector or payment algorithm.
    // Actual Core owns TLS, sequencing, matching, selection count and result evidence.
    private sealed class Observation
    { internal int Unit = 37, Player = 3, NextType = 12; internal uint Global = 4001; }

    private sealed class ScopeCase
    {
        internal readonly object Engine = new object();
        internal readonly Observation Frame = new Observation();
        internal readonly List<string> Events = new List<string>();
        internal readonly EuropeanRecruitmentRequest Request = new EuropeanRecruitmentRequest(new IntPtr(17), 7, 41, 3);
        internal bool Context = true, DemandScopeValid = true;
        internal int NativeCalls, LegacyCalls, ReadCalls, AdmissionCalls;
        internal void Legacy(Observation frame) { Events.Add("legacy"); LegacyCalls++; frame.NextType = 76; }
        internal EuropeanRecruitmentCandidate Read(Observation frame)
        {
            Events.Add("read"); ReadCalls++;
            return new EuropeanRecruitmentCandidate(frame.Unit, frame.Global, frame.Player, 7, 41, frame.NextType);
        }
        internal EuropeanRecruitmentBranch Dispatch(Action<Observation> legacy = null,
            Func<Observation, EuropeanRecruitmentCandidate> reader = null, int? selected = null, int? player = null) =>
            EuropeanRecruitmentInvocation.Dispatch(Frame, selected ?? Frame.Unit, player ?? Frame.Player, legacy ?? Legacy, reader ?? Read);
        internal EuropeanRecruitmentAttemptResult Run(EuropeanRecruitmentAdmission admission, Func<ScopeCase, int> native,
            Action<bool> demand = null)
        {
            EuropeanRecruitmentAttemptResult result;
            lock (Engine)
                result = EuropeanRecruitmentInvocation.Invoke(Request, candidate => {
                    Events.Add("admission"); AdmissionCalls++; return admission(candidate);
                }, Engine, () => Context, (manager, carrier, origin, player, query) => {
                    Events.Add("native"); NativeCalls++;
                    Check(manager == Request.Manager && carrier == 7 && origin == 41 && player == 3 && query == 0,
                        "Native callable arguments changed.");
                    return native(this);
                }, value => {
                    Events.Add(value ? "on" : "off");
                    EuropeanRecruitmentRequest owned;
                    DemandScopeValid &= EuropeanRecruitmentInvocation.TryReadOwnedRequest(out owned) && owned.Manager == Request.Manager &&
                        owned.Carrier == 7 && owned.NativeOrigin == 41 && owned.Player == 3;
                    demand?.Invoke(value);
                });
            Check(!EuropeanRecruitmentInvocation.HasOwnedScope, "Invoke did not clear TLS after retirement.");
            Check(DemandScopeValid, "Demand changed outside its exact TLS owner.");
            return result;
        }
    }

    private static void InvalidBoundary()
    {
        object engine = new object(); int native = 0, demand = 0;
        EuropeanRecruitmentNativeCall original = (m, c, o, p, q) => { native++; return 0; };
        Action<bool> writer = value => demand++;
        var good = new EuropeanRecruitmentRequest(new IntPtr(17), 7, 41, 3);
        Outcome(EuropeanRecruitmentInvocation.Invoke(good, c => true, engine, () => true, original, writer),
            EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.MissingEngineMonitor, false);
        lock (engine)
        {
            foreach (var request in new[] { new EuropeanRecruitmentRequest(IntPtr.Zero, 7, 41, 3),
                new EuropeanRecruitmentRequest(new IntPtr(17), 7, 41, 0), new EuropeanRecruitmentRequest(new IntPtr(17), 7, 41, 9) })
                Outcome(EuropeanRecruitmentInvocation.Invoke(request, c => true, engine, () => true, original, writer),
                    EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.InvalidInput, false);
            Outcome(EuropeanRecruitmentInvocation.Invoke(good, null, engine, () => true, original, writer),
                EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.InvalidInput, false);
            Outcome(EuropeanRecruitmentInvocation.Invoke(good, c => true, engine, () => false, original, writer),
                EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.InvalidContext, false);
            Outcome(EuropeanRecruitmentInvocation.Invoke(good, c => true, engine,
                () => throw new InvalidOperationException("Context unavailable."), original, writer),
                EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.InvalidContext, false);
        }
        Check(native == 0 && demand == 0 && !EuropeanRecruitmentInvocation.HasOwnedScope,
            "Refused input crossed the demand/native boundary.");
    }

    private static void OrderedScope()
    {
        var c = new ScopeCase(); bool facts = false;
        var result = c.Run(candidate => {
            facts = candidate.UnitId == 37 && candidate.GlobalId == 4001 && candidate.PlayerId == 3 &&
                candidate.NativePaymentCarrier == 7 && candidate.NativeOrigin == 41 && candidate.NextUnitType == 76 &&
                Monitor.IsEntered(c.Engine) && EuropeanRecruitmentInvocation.HasOwnedScope;
            return true;
        }, x => { Check(x.Dispatch() == EuropeanRecruitmentBranch.Allow, "Coherent admission did not allow."); return 37; });
        Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
        Check(facts && c.Events.SequenceEqual(new[] { "on", "native", "legacy", "read", "admission", "read", "off" }),
            "Actual owner ordering or final legacy type observation changed.");
        Check(result.Candidate.HasValue && result.Candidate.Value.NextUnitType == 76 && result.AdmissionCalled && c.NativeCalls == 1,
            "Scalar evidence was not retained.");
    }

    private static void RawZero()
    {
        var c = new ScopeCase(); var result = c.Run(candidate => false, x => 0);
        Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
        Check(result.NativeReturn == 0 && !result.Candidate.HasValue && !result.AdmissionCalled && c.LegacyCalls == 0,
            "Raw zero fabricated selected-candidate or rejection evidence.");
        c = new ScopeCase(); result = c.Run(candidate => true, x => 37);
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedNativeReturn);
    }

    private static void AllowReturn()
    {
        foreach (int returned in new[] { 37, 0, 38 })
        {
            var c = new ScopeCase(); var result = c.Run(candidate => true, x => {
                Check(x.Dispatch() == EuropeanRecruitmentBranch.Allow, "Allow dispatch changed."); return returned;
            });
            Outcome(result, returned == 37 ? EuropeanRecruitmentAttemptKind.NativeReturned : EuropeanRecruitmentAttemptKind.UnknownNativeEffects,
                returned == 37 ? EuropeanRecruitmentFailure.None : EuropeanRecruitmentFailure.UnexpectedNativeReturn);
        }
    }

    private static void CertifiedRefusal()
    {
        foreach (bool throws in new[] { false, true })
        {
            var c = new ScopeCase(); var result = c.Run(candidate => {
                if (throws) throw new InvalidOperationException("Explicit admission failure."); return false;
            }, x => { Check(x.Dispatch() == EuropeanRecruitmentBranch.Abort, "Refusal did not select abort."); return 0; });
            Outcome(result, EuropeanRecruitmentAttemptKind.RejectedBeforeConversion,
                throws ? EuropeanRecruitmentFailure.AdmissionFailed : EuropeanRecruitmentFailure.AdmissionRejected);
            Check(result.Candidate.HasValue && result.AdmissionCalled && c.ReadCalls == 2 && result.NativeReturn == 0,
                "Certificate lacks coherent before/after observation and normal zero return.");
            if (throws) Check(result.ErrorMessage == "Explicit admission failure.", "Unrelated callback error supplied the certificate.");
        }
    }

    private static void RejectReturn()
    {
        var c = new ScopeCase(); var result = c.Run(candidate => false, x => { x.Dispatch(); return 37; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedNativeReturn);
        c = new ScopeCase(); result = c.Run(candidate => false, x => {
            x.Dispatch(); throw new InvalidOperationException("Native callable failed after refusal.");
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.NativeInvocationFailed);
        Check(!result.NativeReturn.HasValue, "Thrown native callable fabricated a normal return.");
    }

    private static void DuplicateSites()
    {
        var c = new ScopeCase(); EuropeanRecruitmentBranch duplicate = EuropeanRecruitmentBranch.Abort;
        var result = c.Run(candidate => true, x => { x.Dispatch(); duplicate = x.Dispatch(); return 37; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedSelection);
        Check(duplicate == EuropeanRecruitmentBranch.Abort && c.LegacyCalls == 1 && c.AdmissionCalls == 1,
            "Duplicate site stole the owned dispatch.");
        c = new ScopeCase(); EuropeanRecruitmentBranch recursive = EuropeanRecruitmentBranch.Abort;
        result = c.Run(candidate => true, x => {
            x.Dispatch(frame => { x.Legacy(frame); recursive = x.Dispatch(); }); return 0;
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.UnexpectedSelection);
        Check(recursive == EuropeanRecruitmentBranch.Abort && c.LegacyCalls == 1 && c.AdmissionCalls == 0,
            "Recursive site reached admission or borrowed the owner.");
    }

    private static void CandidateMismatch()
    {
        var c = new ScopeCase(); var result = c.Run(candidate => false, x => { x.Dispatch(selected: 38); return 0; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.CandidateMismatch);
        Check(c.AdmissionCalls == 0, "Mismatched candidate reached admission.");
        c = new ScopeCase(); result = c.Run(candidate => { c.Frame.Global++; c.Frame.NextType++; return false; }, x => { x.Dispatch(); return 0; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.CandidateMismatch);
        Check(result.Candidate.HasValue && result.Candidate.Value.GlobalId == 4001 && result.Candidate.Value.NextUnitType == 76,
            "Mutable later observation rewrote retained candidate evidence.");
    }

    private static void ChangedContext()
    {
        var c = new ScopeCase(); var result = c.Run(candidate => { c.Context = false; return false; }, x => { x.Dispatch(); return 0; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.CandidateMismatch);
        c = new ScopeCase(); result = c.Run(candidate => false, x => { x.Dispatch(); x.Context = false; return 0; });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.ContextChanged);
    }

    private static void DispatcherFaults()
    {
        var c = new ScopeCase(); var result = c.Run(candidate => false, x => {
            x.Dispatch(frame => throw new InvalidOperationException("Legacy failed.")); return 0;
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.LegacyDispatchFailed);
        Check(c.AdmissionCalls == 0 && c.ReadCalls == 0, "Legacy failure reached later stages.");
        c = new ScopeCase(); result = c.Run(candidate => false, x => {
            x.Dispatch(reader: frame => throw new InvalidOperationException("Candidate unreadable.")); return 0;
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.CandidateReadFailed);
        c = new ScopeCase(); result = c.Run(candidate => false, x => {
            EuropeanRecruitmentInvocation.ReportFrameFailure(new InvalidOperationException("Frame failed.")); return 0;
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.UnknownNativeEffects, EuropeanRecruitmentFailure.FrameDispatchFailed);
    }

    private static void NestedAttempt()
    {
        foreach (bool inAdmission in new[] { false, true })
        {
            var c = new ScopeCase(); EuropeanRecruitmentAttemptResult nested = null; int innerCalls = 0;
            Action enter = () => nested = EuropeanRecruitmentInvocation.Invoke(c.Request, candidate => true, c.Engine, () => true,
                (m, a, b, p, q) => { innerCalls++; return 0; }, value => innerCalls++);
            var result = c.Run(candidate => { if (inAdmission) enter(); return true; }, x => {
                x.Dispatch(frame => { x.Legacy(frame); if (!inAdmission) enter(); }); return 37;
            });
            Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
            Outcome(nested, EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.ReentrantAttempt, false);
            Check(innerCalls == 0 && c.NativeCalls == 1 && c.AdmissionCalls == 1, "Nested call entered or damaged outer owner.");
        }
    }

    private static void ForeignThread()
    {
        var c = new ScopeCase(); Exception workerError = null; EuropeanRecruitmentBranch branch = EuropeanRecruitmentBranch.Abort;
        EuropeanRecruitmentAttemptResult foreign = null; int borrowedCallbacks = 0;
        var result = c.Run(candidate => true, x => {
            var thread = new Thread(() => {
                try
                {
                    branch = EuropeanRecruitmentInvocation.Dispatch(x.Frame, 37, 3,
                        frame => Interlocked.Increment(ref borrowedCallbacks), frame => { Interlocked.Increment(ref borrowedCallbacks); return x.Read(frame); });
                    foreign = EuropeanRecruitmentInvocation.Invoke(x.Request, candidate => true, x.Engine, () => true,
                        (m, a, b, p, q) => { Interlocked.Increment(ref borrowedCallbacks); return 0; }, value => Interlocked.Increment(ref borrowedCallbacks));
                }
                catch (Exception error) { workerError = error; }
            }) { IsBackground = true };
            thread.Start(); Check(thread.Join(3000), "Owned BCL worker exceeded bound.");
            if (workerError != null) throw new InvalidOperationException("Foreign-thread fixture failed.", workerError);
            x.Dispatch(); return 37;
        });
        Outcome(result, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
        Outcome(foreign, EuropeanRecruitmentAttemptKind.NotEntered, EuropeanRecruitmentFailure.MissingEngineMonitor, false);
        Check(branch == EuropeanRecruitmentBranch.NoOwnedScope && borrowedCallbacks == 0 && c.AdmissionCalls == 1,
            "Foreign thread borrowed TLS ownership or invoked owned leaves.");
    }

    private static void DemandFaults()
    {
        foreach (bool publication in new[] { true, false })
        {
            var c = new ScopeCase(); var result = c.Run(candidate => true, x => { x.Dispatch(); return 37; }, value => {
                if (value == publication) throw new InvalidOperationException("Demand write failed.");
            });
            Outcome(result, publication ? EuropeanRecruitmentAttemptKind.NotEntered : EuropeanRecruitmentAttemptKind.UnknownNativeEffects,
                publication ? EuropeanRecruitmentFailure.DemandPublicationFailed : EuropeanRecruitmentFailure.DemandRetirementFailed, !publication);
            Check(c.NativeCalls == (publication ? 0 : 1) && c.Events.First() == "on" && c.Events.Last() == "off",
                "Demand failure lost its retirement bookend.");
        }
        var fresh = new ScopeCase(); Outcome(fresh.Run(candidate => true, x => { x.Dispatch(); return 37; }),
            EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
    }

    private static void ImmutableResults()
    {
        var c = new ScopeCase(); var retained = c.Run(candidate => throw new InvalidOperationException(new string('x', 400)), x => { x.Dispatch(); return 0; });
        Outcome(retained, EuropeanRecruitmentAttemptKind.RejectedBeforeConversion, EuropeanRecruitmentFailure.AdmissionFailed);
        Check(retained.ErrorMessage.Length == 256, "Diagnostic bound changed.");
        var later = new ScopeCase(); later.Frame.Global = 9001; later.Frame.Unit = 44;
        var laterResult = later.Run(candidate => true, x => { x.Dispatch(); return 44; });
        Outcome(laterResult, EuropeanRecruitmentAttemptKind.NativeReturned, EuropeanRecruitmentFailure.None);
        Check(retained.Candidate.HasValue && retained.Candidate.Value.UnitId == 37 && retained.Candidate.Value.GlobalId == 4001 &&
            retained.NativeReturn == 0 && retained.ErrorMessage == new string('x', 256) && retained.AdmissionCalled,
            "Later attempt mutated terminal scalar evidence.");
    }

    private sealed class Fixture : IDisposable
    {
        private const int Bytes = 0x10000;
        private const int Entry = 0, Stub = 0x1000, Site = 0x3000, Leaf = 0x4000;
        private const int ContinueCapture = 0x5000, AbortCapture = 0x6000, Receipt = 0x7000, Input = 0x8000, ActiveFlag = 0x8200;
        private const int SavedSimd = 0x200, FrameBytes = 0x308;
        private const int MarkerOffset = 0, LegacyCount = 4, ActiveCount = 8, LeafCount = 12;
        private const int EntryRspOffset = 16, ExitRspOffset = 24, ContextRaw = 32, ContextNative = 40;
        private const int LeafRspOffset = 48, LeafFlagsOffset = 56, FlagsOffset = 64;
        private const int Registers = 80, Vectors = 208, LeafVectors = 480;
        private const int FirstGuardOffset = 576, LastGuardOffset = 584;
        private IntPtr memory;
        private IntPtr recordMemory;
        private readonly bool traceMode;
        private byte[] originalBytes, initialRecordBytes;
        private const int TraceCell = 0x8300, TraceSlot = 37, RecordStride = 0x490;
        private const int RecordReadSeed = unchecked((int)0x81234567);
        private const ulong TraceTokenSeed = 0x61728394ABCDEF01UL;
        internal ulong RecordManager => unchecked((ulong)recordMemory.ToInt64());
        internal ulong RecordAddress => RecordManager + TraceSlot * RecordStride;
        internal int TraceState => Marshal.ReadInt32(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceStateOffset));
        internal ulong TraceRecord => unchecked((ulong)Marshal.ReadInt64(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceRecordOffset)));
        internal int TraceUnitSlot => Marshal.ReadInt32(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceSlotOffset));
        internal int TraceNextType => Marshal.ReadInt32(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceNextTypeOffset));
        internal ulong TraceToken => unchecked((ulong)Marshal.ReadInt64(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceTokenOffset)));
        private X64InlineHook owner;
        private NativeEntry invoke;
        private ClobberLeaf clobber;
        private WeakReference legacyRoot, activeRoot;
        private readonly bool active;
        private readonly bool independentBaseline;
        private readonly object engineMonitor = new object();
        internal int NativeCalls, AdmissionCalls;
        internal Action DuringLegacy;
        internal int GuardCallbackCalls;
        internal ulong GuardIncomingRax;
        internal bool AdmissionSawFinalCandidate;
        internal bool CapturedTailOperandRegisters;
        internal int ActiveFlagValue => Marshal.ReadInt32(At(ActiveFlag));
        private ulong Base => unchecked((ulong)memory.ToInt64());
        private ulong Address(int offset) => Base + (ulong)offset;
        private IntPtr At(int offset) => IntPtr.Add(memory, offset);
        private ulong Read64(int offset) => unchecked((ulong)Marshal.ReadInt64(At(Receipt + offset)));
        internal int Marker => Marshal.ReadInt32(At(Receipt + MarkerOffset));
        internal int LegacyCalls => Marshal.ReadInt32(At(Receipt + LegacyCount));
        internal int ActiveCalls => Marshal.ReadInt32(At(Receipt + ActiveCount));
        internal int LeafCalls => Marshal.ReadInt32(At(Receipt + LeafCount));
        internal ulong EntryRsp => Read64(EntryRspOffset);
        internal ulong ExitRsp => Read64(ExitRspOffset);
        internal ulong ContextRawRsp => Read64(ContextRaw);
        internal ulong ContextNativeRsp => Read64(ContextNative);
        internal ulong LeafRsp => Read64(LeafRspOffset);
        internal ulong LeafFlags => Read64(LeafFlagsOffset);
        internal ulong Flags => Read64(FlagsOffset);
        internal ulong FirstGuard => Read64(FirstGuardOffset);
        internal ulong LastGuard => Read64(LastGuardOffset);
        internal ulong Register(int index) => Read64(Registers + index * 8);
        internal ulong Vector(int index, int half) => Read64(Vectors + index * 16 + half * 8);
        internal ulong LeafSimd(int index, int half) => Read64(LeafVectors + index * 16 + half * 8);

        internal Fixture(bool active, bool independentBaseline, bool guardBeforeDispatch = false, bool traceMode = false)
        {
            this.active = active; this.independentBaseline = independentBaseline; this.traceMode = traceMode;
            memory = VirtualAlloc(IntPtr.Zero, (UIntPtr)Bytes, 0x3000, 0x40);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned fixture allocation failed.");
            try
            {
                if (traceMode)
                {
                    // A separate owned data page; no game manager, borrowed pointer or game bytes.
                    recordMemory = VirtualAlloc(IntPtr.Zero, (UIntPtr)Bytes, 0x3000, 0x04);
                    if (recordMemory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned record page allocation failed.");
                    InitializeRecordPage(); InitializeTraceCell();
                    originalBytes = AssembleTraceOriginal();
                    Check(originalBytes.Length == EuropeanRecruitmentAdmissionStub.ConversionDisplacedBytes,
                        "Owned assembler did not emit exactly63 bytes for ten instructions.");
                }
                else originalBytes = Prefix;
                Marshal.Copy(originalBytes, 0, At(Site), originalBytes.Length);
                // Constructor reads ONLY our own prefix. No Compile/Generate/Enable
                // is called on this owner; only its public frame/delegate facilities.
                owner = new X64InlineHook(Address(Site), originalBytes.Length, NullLogger.Instance, "owned-recruitment-fixture");
                Check(owner.DisplacedByteCount == originalBytes.Length, "Owned root decoded another displacement boundary.");
                var options = new ContextHookOptions { Registers = X64SmartCPUContextRegs.Volatile | X64SmartCPUContextRegs.RDI | X64SmartCPUContextRegs.RBP };
                Check((int)options.Registers == 0xF6D && (int)options.ErrorMode == 1 && (int)options.Placement == 1,
                    "The fixture requires the exact qualified legacy context options.");
                ContextHookDelegate oldCallback = context => PublishLegacy(context.Pointer);
                ContextHookDelegate newCallback = context => {
                    Marshal.WriteInt32(At(Receipt + ActiveCount), ActiveCalls + 1);
                    Marshal.WriteInt64(At(Receipt + ContextRaw), unchecked((long)context.Pointer->RSP));
                    Marshal.WriteInt64(At(Receipt + ContextNative), unchecked((long)(context.Pointer->RSP + 112UL)));
                    if (guardBeforeDispatch)
                    {
                        GuardCallbackCalls++;
                        GuardIncomingRax = context.Pointer->RAX;
                        clobber();
                        throw new InvalidOperationException("Controlled Guard exception before Dispatch.");
                    }
                    CapturedTailOperandRegisters = context.Pointer->R12 == SeedGp(12) &&
                        context.Pointer->R13 == SeedGp(13) && context.Pointer->R14 == SeedGp(14);
                    context.Pointer->RAX = (ulong)EuropeanRecruitmentInvocation.Dispatch((IntPtr)context.Pointer,
                        unchecked((int)context.Pointer->RDI), unchecked((int)context.Pointer->RBP),
                        frame => PublishLegacy((X64SmartCPUContext*)frame), frame => ReadCandidate((X64SmartCPUContext*)frame));
                };
                legacyRoot = new WeakReference(oldCallback); activeRoot = new WeakReference(newCallback);
                InlineHookGenerator legacyFrame = independentBaseline
                    ? ContextStub.CreateGenerator(owner,
                        ContextStub.Guard(oldCallback, options, Address(Site), NullLogger.Instance, "independent-baseline"), options)
                    : EuropeanRecruitmentAdmissionStub.CreateLegacyFrame(owner, oldCallback, Address(Site), NullLogger.Instance);
                InlineHookGenerator activeFrame;
                if (guardBeforeDispatch)
                {
                    var guardedOptions = new ContextHookOptions { Registers = EuropeanRecruitmentAdmissionStub.FinalAdmissionRegisters };
                    activeFrame = ContextStub.CreateGenerator(owner,
                        ContextStub.Guard(newCallback, guardedOptions, Address(Site), NullLogger.Instance,
                            "controlled-pre-dispatch-guard-fault"), guardedOptions);
                }
                else activeFrame = EuropeanRecruitmentAdmissionStub.CreateActiveFrame(owner,
                    newCallback, Address(Site), NullLogger.Instance);
                // Emitting these actual generators calls the real owner's PinDelegate.
                // No fixture field retains either raw callback or generator delegate.
                var body = new Assembler(64);
                Instruction[] original = Decode(originalBytes, Address(Site));
                int displaced = originalBytes.Length;
                if (independentBaseline)
                {
                    // Independent REAL RedBird replay: the helper receives the full
                    // decoded prefix, unlike the prototype's empty-frame composition.
                    legacyFrame(body, original, Address(Site + displaced));
                    body.AddUnrestrictedJmp(Address(Site + displaced));
                }
                else if (traceMode) EuropeanRecruitmentAdmissionStub.GenerateConversionTrace(body, original, Address(Site),
                    Address(Site + displaced), Address(Site - 0x2C), Address(ActiveFlag), Address(TraceCell), legacyFrame, activeFrame);
                else EuropeanRecruitmentAdmissionStub.Generate(body, original, Address(Site),
                    Address(Site + Prefix.Length), Address(Site - 0x2C), Address(ActiveFlag), legacyFrame, activeFrame);
                Write(Stub, body, 0x1000);
                Write(Leaf, MakeLeaf(), 0x1000);
                Write(ContinueCapture, Capture(ContinueMarker), 0x1000);
                Write(AbortCapture, Capture(AbortMarker), 0x1000);
                WriteJump(Site + displaced, Address(ContinueCapture));
                WriteJump(Site - 0x2C, Address(AbortCapture));
                Write(Entry, MakeEntry(), 0x1000);
                if (!FlushInstructionCache(GetCurrentProcess(), memory, (UIntPtr)Bytes))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned code cache flush failed.");
                clobber = (ClobberLeaf)Marshal.GetDelegateForFunctionPointer(At(Leaf), typeof(ClobberLeaf));
                invoke = (NativeEntry)Marshal.GetDelegateForFunctionPointer(At(Entry), typeof(NativeEntry));
            }
            catch { Dispose(); throw; }
        }

        private void PublishLegacy(X64SmartCPUContext* cpu)
        {
            Marshal.WriteInt32(At(Receipt + LegacyCount), LegacyCalls + 1);
            Marshal.WriteInt64(At(Receipt + ContextRaw), unchecked((long)cpu->RSP));
            ulong outer = EuropeanRecruitmentInvocation.HasOwnedScope ? 112UL : 0;
            Marshal.WriteInt64(At(Receipt + ContextNative), unchecked((long)(cpu->RSP + outer)));
            clobber();
            cpu->RAX = 1; // Legacy scratch must not itself grant admission.
            cpu->R9 = FinalR9;
            DuringLegacy?.Invoke();
        }

        private EuropeanRecruitmentCandidate ReadCandidate(X64SmartCPUContext* cpu)
        {
            Check(LegacyCalls == 1 && LeafCalls == 1, "Candidate read preceded legacy.");
            Equal(FinalR9, cpu->R9, "Final legacy R9 was lost.");
            return new EuropeanRecruitmentCandidate(unchecked((int)cpu->RDI), 4001,
                unchecked((int)cpu->RBP), 7, 41, unchecked((int)cpu->R9));
        }

        internal EuropeanRecruitmentAttemptResult RunScoped(bool direction, bool allow, bool predicateThrows)
        {
            var request = new EuropeanRecruitmentRequest(new IntPtr(17), 7, 41, 3);
            lock (engineMonitor)
                return EuropeanRecruitmentInvocation.Invoke(request, candidate => {
                    AdmissionCalls++;
                    AdmissionSawFinalCandidate = candidate.UnitId == 37 && candidate.GlobalId == 4001 && candidate.PlayerId == 3 &&
                        candidate.NativePaymentCarrier == 7 && candidate.NativeOrigin == 41 && candidate.NextUnitType == 76 &&
                        LegacyCalls == 1 && ActiveCalls == 1;
                    if (predicateThrows) throw new InvalidOperationException("Owned predicate failure.");
                    return allow;
                }, engineMonitor, () => true, (manager, carrier, origin, player, query) => {
                    NativeCalls++;
                    Check(manager == request.Manager && carrier == 7 && origin == 41 && player == 3 && query == 0,
                        "Actual wrapper changed native arguments/queryOnly.");
                    Run(direction);
                    return Marker == ContinueMarker ? 37 : 0;
                }, value => Marshal.WriteInt32(At(ActiveFlag), value ? 1 : 0));
        }

        internal void CollectAndCheckRoots()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(legacyRoot.IsAlive && (independentBaseline || activeRoot.IsAlive), "The real root lost a pinned callback.");
            CheckRootUntouched();
        }

        internal void Run(bool direction, ulong? incomingRax = null)
        {
            for (int at = 0; at < 640; at += 8) Marshal.WriteInt64(At(Receipt + at), 0);
            Marshal.WriteInt32(At(ActiveFlag), active ? 1 : 0);
            for (int i = 0; i < Gp.Length; i++) Marshal.WriteInt64(At(Input + i * 8), unchecked((long)SeedGp(i)));
            if (incomingRax.HasValue) Marshal.WriteInt64(At(Input), unchecked((long)incomingRax.Value));
            if (traceMode) Marshal.WriteInt64(At(Input + 3 * 8), unchecked((long)RecordManager));
            for (int i = 0; i < Simd.Length; i++)
            {
                ulong seed = SimdSeed + (ulong)i;
                Marshal.WriteInt64(At(Input + 128 + i * 16), unchecked((long)seed));
                Marshal.WriteInt64(At(Input + 136 + i * 16), unchecked((long)~seed));
            }
            Marshal.WriteInt64(At(Input + 400), unchecked((long)(SeedFlags | (direction ? 0x400UL : 0))));
            uint previous = GetErrorMode();
            try
            {
                const uint noDialogs = 0x8003;
                SetErrorMode(previous | noDialogs);
                Check((GetErrorMode() & noDialogs) == noDialogs, "Owned process fault-dialog suppression failed.");
                invoke();
            }
            finally { SetErrorMode(previous); }
            GC.KeepAlive(owner); GC.KeepAlive(clobber);
        }

        private byte[] AssembleTraceOriginal(int multiplier = 0x490, int state = 0x6D, int finalOffset = 0x9CA)
        {
            // Own assembler instructions only; no byte blob or decoding of a game image.
            var a = new Assembler(64);
            a.movsxd(rax, edi); a.imul(rcx, rax, multiplier); a.add(rbx, rcx); a.xor(ecx, ecx);
            a.movsxd(rax, __dword_ptr[rbx + 0x72C]);
            a.mov(__word_ptr[rbx + 0x922], r9w); a.mov(__word_ptr[rbx + 0x924], cx);
            a.mov(__dword_ptr[rbx + 0x918], state); a.mov(__word_ptr[rbx + 0x986], (ushort)0x100);
            a.mov(__word_ptr[rbx + finalOffset], si);
            using var stream = new MemoryStream(); a.Assemble(new StreamCodeWriter(stream), Address(Site));
            return stream.ToArray();
        }
        private IntPtr RecordAt(int offset) => IntPtr.Add(recordMemory, TraceSlot * RecordStride + offset);
        private void InitializeRecordPage()
        {
            Check(TraceSlot * RecordStride + 0xA08 <= Bytes && TraceSlot * RecordStride >= 0,
                "Highest owned record field/guard escaped its64KiB data page.");
            Check((TraceCell & 7) == 0 && TraceCell >= ActiveFlag + 4 &&
                TraceCell + EuropeanRecruitmentAdmissionStub.TraceCellBytes <= Bytes,
                "Owned aligned trace cell escaped or overlapped live flag storage.");
            Marshal.WriteInt32(RecordAt(0x72C), RecordReadSeed);
            Marshal.WriteInt32(RecordAt(0x918), unchecked((int)0x55667788));
            Marshal.WriteInt16(RecordAt(0x922), unchecked((short)0xA101));
            Marshal.WriteInt16(RecordAt(0x924), unchecked((short)0xA202));
            Marshal.WriteInt16(RecordAt(0x986), unchecked((short)0xA303));
            Marshal.WriteInt16(RecordAt(0x9CA), unchecked((short)0xA404));
            Marshal.WriteInt64(RecordAt(0x700), unchecked((long)Guard));
            Marshal.WriteInt64(RecordAt(0xA00), unchecked((long)Guard));
            initialRecordBytes = SnapshotRecord();
        }
        internal void InitializeTraceCell(int state = 0, ulong token = TraceTokenSeed)
        {
            Check(traceMode, "Trace initialization requires the owned trace fixture.");
            for (int offset = 0; offset < EuropeanRecruitmentAdmissionStub.TraceCellBytes; offset += 8)
                Marshal.WriteInt64(At(TraceCell + offset), 0);
            Marshal.WriteInt32(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceStateOffset), state);
            Marshal.WriteInt64(At(TraceCell + EuropeanRecruitmentAdmissionStub.TraceTokenOffset), unchecked((long)token));
        }
        internal byte[] SnapshotTrace()
        {
            var bytes = new byte[EuropeanRecruitmentAdmissionStub.TraceCellBytes];
            Marshal.Copy(At(TraceCell), bytes, 0, bytes.Length); return bytes;
        }
        internal byte[] SnapshotRecord()
        {
            var bytes = new byte[Bytes]; Marshal.Copy(recordMemory, bytes, 0, bytes.Length); return bytes;
        }
        internal void ChangeRecordSentinel() => Marshal.WriteInt32(RecordAt(0x918), unchecked((int)0x7ABCDEF0));
        internal void CheckRecordStores(bool scheduled)
        {
            byte[] expected = (byte[])initialRecordBytes.Clone();
            int row = TraceSlot * RecordStride;
            if (scheduled)
            {
                Array.Copy(BitConverter.GetBytes(0x6D), 0, expected, row + 0x918, 4);
                Array.Copy(BitConverter.GetBytes(unchecked((ushort)FinalR9)), 0, expected, row + 0x922, 2);
                Array.Copy(BitConverter.GetBytes((ushort)0), 0, expected, row + 0x924, 2);
                Array.Copy(BitConverter.GetBytes((ushort)0x100), 0, expected, row + 0x986, 2);
                Array.Copy(BitConverter.GetBytes(unchecked((ushort)SeedGp(6))), 0, expected, row + 0x9CA, 2);
            }
            Check(SnapshotRecord().SequenceEqual(expected), "Owned scheduling stores or surrounding record guards changed unexpectedly.");
        }
        internal ulong ExpectedSliceRax => unchecked((ulong)(long)RecordReadSeed);
        internal void CheckInvalidTraceGenerators()
        {
            Check(traceMode, "Trace validator requires own assembled ten-instruction span.");
            var options = new ContextHookOptions { Registers = EuropeanRecruitmentAdmissionStub.FinalAdmissionRegisters };
            ContextHookDelegate callback = context => { context.Pointer->RAX = 0; };
            InlineHookGenerator frame = ContextStub.CreateGenerator(owner,
                ContextStub.Guard(callback, options, Address(Site), NullLogger.Instance, "invalid-trace-fixture"), options);
            Instruction[] original = Decode(originalBytes, Address(Site));
            void Refuse(Instruction[] instructions, ulong site, ulong next, ulong abort, ulong flag, ulong cell)
            {
                var a = new Assembler(64); bool refused = false;
                try { EuropeanRecruitmentAdmissionStub.GenerateConversionTrace(a, instructions, site, next, abort, flag, cell, frame, frame); }
                catch (ArgumentException) { refused = true; }
                Check(refused && a.Instructions.Count == 0, "Invalid conversion span emitted partial code or was accepted.");
            }
            ulong site = Address(Site), next = Address(Site + 63), abort = Address(Site - 0x2C), flag = Address(ActiveFlag), cell = Address(TraceCell);
            Refuse(Decode(AssembleTraceOriginal(multiplier: 0x491), site), site, next, abort, flag, cell);
            Refuse(Decode(AssembleTraceOriginal(state: 0x6E), site), site, next, abort, flag, cell);
            Refuse(Decode(AssembleTraceOriginal(finalOffset: 0x9CC), site), site, next, abort, flag, cell);
            Refuse(original.Take(9).ToArray(), site, next, abort, flag, cell);
            Refuse(original, site, next - 1, abort, flag, cell);
            Refuse(original, site, next, abort + 1, flag, cell);
            Refuse(original, site + 1, next, abort, flag, cell);
            Refuse(original, site, next, abort, 0, cell);
            Refuse(original, site, next, abort, flag, 0);
            Refuse(original, site, next, abort, flag, cell + 1);
            Refuse(original, site, next, abort, flag, ulong.MaxValue - 15);
            CheckRootUntouched();
        }

        private Assembler MakeEntry()
        {
            var a = new Assembler(64);
            a.push(rbx); a.push(rbp); a.push(rsi); a.push(rdi); a.push(r12); a.push(r13); a.push(r14); a.push(r15);
            a.sub(rsp, FrameBytes);
            for (int i = 6; i < Simd.Length; i++) a.movdqu(__xmmword_ptr[rsp + SavedSimd + (i - 6) * 16], Simd[i]);
            a.mov(rax, Address(Receipt)); a.mov(__qword_ptr[rax + EntryRspOffset], rsp);
            a.mov(rax, Guard); a.mov(__qword_ptr[rsp + 0x100], rax); a.mov(__qword_ptr[rsp + 0x1F0], rax);
            a.mov(rax, Address(Input));
            for (int i = 0; i < Simd.Length; i++) a.movdqu(Simd[i], __xmmword_ptr[rax + 128 + i * 16]);
            for (int i = 1; i < Gp.Length; i++) if (i != 4) a.mov(Gp[i], __qword_ptr[rax + i * 8]);
            a.push(__qword_ptr[rax + 400]); a.popfq(); a.mov(rax, __qword_ptr[rax]);
            a.AddUnrestrictedJmp(Address(Stub)); return a;
        }

        private Assembler MakeLeaf()
        {
            var a = new Assembler(64); a.mov(r11, Address(Receipt));
            a.mov(__qword_ptr[r11 + LeafRspOffset], rsp); a.pushfq(); a.pop(__qword_ptr[r11 + LeafFlagsOffset]);
            a.inc(__dword_ptr[r11 + LeafCount]);
            for (int i = 0; i < 6; i++)
            {
                a.mov(rax, LeafSeed + (ulong)i); a.movq(Simd[i], rax); a.punpcklqdq(Simd[i], Simd[i]);
                a.movdqu(__xmmword_ptr[r11 + LeafVectors + i * 16], Simd[i]);
            }
            // Use exactly the leaf's legal Windows home space. No nonvolatile
            // register or RSP is modified, and DF must be clear when returning.
            a.mov(rax, Guard);
            for (int offset = 8; offset <= 32; offset += 8) a.mov(__qword_ptr[rsp + offset], rax);
            a.mov(rcx, 0xA100UL); a.mov(rdx, 0xA200UL); a.mov(r8, 0xA800UL); a.mov(r9, 0xA900UL);
            a.mov(r10, 0xAA00UL); a.mov(r11, 0xAB00UL); a.mov(rax, 0xA000UL); a.stc(); a.cld(); a.ret(); return a;
        }

        private Assembler Capture(int marker)
        {
            var a = new Assembler(64);
            a.pushfq(); a.push(rax); a.push(r11); a.mov(r11, Address(Receipt));
            for (int i = 1; i < Gp.Length; i++) if (i != 4 && i != 11) a.mov(__qword_ptr[r11 + Registers + i * 8], Gp[i]);
            a.mov(rax, __qword_ptr[rsp + 8]); a.mov(__qword_ptr[r11 + Registers], rax);
            a.mov(rax, __qword_ptr[rsp]); a.mov(__qword_ptr[r11 + Registers + 11 * 8], rax);
            a.lea(rax, __[rsp + 24]); a.mov(__qword_ptr[r11 + Registers + 4 * 8], rax); a.mov(__qword_ptr[r11 + ExitRspOffset], rax);
            a.mov(rax, __qword_ptr[rsp + 16]); a.mov(__qword_ptr[r11 + FlagsOffset], rax);
            for (int i = 0; i < Simd.Length; i++) a.movdqu(__xmmword_ptr[r11 + Vectors + i * 16], Simd[i]);
            a.mov(rax, __qword_ptr[rsp + 24 + 0x100]); a.mov(__qword_ptr[r11 + FirstGuardOffset], rax);
            a.mov(rax, __qword_ptr[rsp + 24 + 0x1F0]); a.mov(__qword_ptr[r11 + LastGuardOffset], rax);
            a.mov(__dword_ptr[r11 + MarkerOffset], marker);
            a.pop(r11); a.pop(rax); a.popfq(); a.cld();
            for (int i = 6; i < Simd.Length; i++) a.movdqu(Simd[i], __xmmword_ptr[rsp + SavedSimd + (i - 6) * 16]);
            a.add(rsp, FrameBytes); a.pop(r15); a.pop(r14); a.pop(r13); a.pop(r12); a.pop(rdi); a.pop(rsi); a.pop(rbp); a.pop(rbx); a.ret();
            return a;
        }

        private void Write(int offset, Assembler a, int bound)
        {
            using var stream = new MemoryStream(); a.Assemble(new StreamCodeWriter(stream), Address(offset));
            byte[] bytes = stream.ToArray(); Check(bytes.Length <= bound && offset >= 0 && offset + bound <= Bytes, "Owned code page bound exceeded.");
            Marshal.Copy(bytes, 0, At(offset), bytes.Length);
        }
        private void WriteJump(int offset, ulong target)
        { var a = new Assembler(64); a.AddUnrestrictedJmp(target); Write(offset, a, 14); }

        internal void CheckRootUntouched()
        {
            Check(owner != null && !owner.IsInstalled && owner.StubAddress == IntPtr.Zero, "The root must never be generated or installed.");
            byte[] live = new byte[originalBytes.Length]; Marshal.Copy(At(Site), live, 0, live.Length);
            Check(live.SequenceEqual(originalBytes), "Fixture generation changed the owned original prefix.");
        }

        internal void CheckInvalidGenerators()
        {
            var options = new ContextHookOptions { Registers = X64SmartCPUContextRegs.Volatile | X64SmartCPUContextRegs.RDI | X64SmartCPUContextRegs.RBP };
            ContextHookDelegate callback = context => { context.Pointer->RAX = 0; };
            var guarded = ContextStub.Guard(callback, options, Address(Site), NullLogger.Instance, "invalid-input-fixture");
            InlineHookGenerator frame = ContextStub.CreateGenerator(owner, guarded, options);
            Instruction[] original = Decode(Prefix, Address(Site));
            void Refuse(Instruction[] instructions, ulong site, ulong next, ulong abort, ulong flag)
            {
                var a = new Assembler(64); bool refused = false;
                try { EuropeanRecruitmentAdmissionStub.Generate(a, instructions, site, next, abort, flag, frame, frame); }
                catch (ArgumentException) { refused = true; }
                Check(refused && a.Instructions.Count == 0, "Malformed input was accepted or emitted a partial stub.");
            }
            Refuse(Decode(Prefix.Take(Prefix.Length - 1).ToArray(), Address(Site)), Address(Site), Address(Site + 15), Address(Site - 0x2C), Address(ActiveFlag));
            Refuse(Decode(Prefix.Concat(new byte[] { 0x90 }).ToArray(), Address(Site)), Address(Site), Address(Site + 15), Address(Site - 0x2C), Address(ActiveFlag));
            byte[] changed = (byte[])Prefix.Clone(); changed[6] = 0x91;
            Refuse(Decode(changed, Address(Site)), Address(Site), Address(Site + 15), Address(Site - 0x2C), Address(ActiveFlag));
            Refuse(Decode(Prefix, Address(Site) + 1), Address(Site), Address(Site + 15), Address(Site - 0x2C), Address(ActiveFlag));
            Refuse(original, Address(Site), Address(Site + 14), Address(Site - 0x2C), Address(ActiveFlag));
            Refuse(original, Address(Site), Address(Site + 15), Address(Site - 0x2C) + 1, Address(ActiveFlag));
            Refuse(original, Address(Site), Address(Site + 15), Address(Site - 0x2C), 0);
        }

        public void Dispose()
        {
            // No calls are made after this point. The never-installed root releases
            // its callback roots before the exclusively owned allocation is freed.
            owner?.Dispose(); owner = null; invoke = null; clobber = null;
            try
            {
                if (memory != IntPtr.Zero)
                {
                    IntPtr owned = memory; memory = IntPtr.Zero;
                    if (!VirtualFree(owned, UIntPtr.Zero, 0x8000)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned fixture release failed.");
                }
            }
            finally
            {
                if (recordMemory != IntPtr.Zero)
                {
                    IntPtr owned = recordMemory; recordMemory = IntPtr.Zero;
                    if (!VirtualFree(owned, UIntPtr.Zero, 0x8000)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Owned record page release failed.");
                }
            }
        }
    }
}
