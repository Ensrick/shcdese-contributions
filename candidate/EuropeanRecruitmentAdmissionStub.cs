#nullable enable
using Iced.Intel;
using RedBird.X64.Extensions;
using RedBird.X64.Assembly;
using RedBird.X64.Hooks.Context;
using Microsoft.Extensions.Logging;
using RedBird.X64.Hooks;
using System;
using System.Runtime.InteropServices;
using static Iced.Intel.AssemblerRegisters;
using SHCDESE.API;

namespace SHCDESE.Detours;

// SOURCE-ONLY private mechanism prototype, not a native installation/backend or writer authority.
// Frame delegates must come from the exact public ContextStub.CreateGenerator owner; they are
// called with an empty displaced span so its actual RedBird context machinery is reused, not copied.
internal static unsafe class EuropeanRecruitmentAdmissionStub
{
    internal const int DisplacedBytes = 15;
    internal const int AbortDelta = 0x2C;
    internal const int ActiveOuterFrameBytes = 112; // entryRFLAGS + originalRAX + six low128 XMMs.

    // A separate, opt-in SAME-owner displacement. The real owner must capture and
    // claim all63 bytes, not supply this suffix from an unowned continuation.
    internal const int ConversionDisplacedBytes = 63;
    internal const int TraceCellBytes = 32;
    internal const int TraceStateOffset = 0, TraceRecordOffset = 8;
    internal const int TraceSlotOffset = 16, TraceNextTypeOffset = 20, TraceTokenOffset = 24;
    internal const int TraceEntered = 1, TraceCompleted = 3, TraceTainted = 4;

    internal const X64SmartCPUContextRegs OfficialRegisters =
        X64SmartCPUContextRegs.Volatile | X64SmartCPUContextRegs.RDI | X64SmartCPUContextRegs.RBP;

    // FBCB's accepted tail uses these nonvolatile bill operands. Only the opt-in
    // active frame captures them; the official/default legacy frame stays exact.
    // Capture is not final resource/bill proof or a qualified native reader.
    internal const X64SmartCPUContextRegs FinalAdmissionRegisters = OfficialRegisters |
        X64SmartCPUContextRegs.R12 | X64SmartCPUContextRegs.R13 | X64SmartCPUContextRegs.R14;

    // These factories compose the actual public dependency. They do not install/compile a hook.
    // Emission calls PinDelegate on the supplied owner, which must remain rooted with its flag
    // storage for the whole executable lifetime. A published owner may not be disposed to retire it.
    internal static InlineHookGenerator CreateLegacyFrame(X64InlineHook owner, ContextHookDelegate dispatch,
        ulong site, ILogger logger)
    {
        if (owner is null || dispatch is null || logger is null) throw new ArgumentNullException(nameof(owner));
        var options = new ContextHookOptions { Registers = OfficialRegisters };
        return ContextStub.CreateGenerator(owner, ContextStub.Guard(dispatch, options, site, logger,
            "EuropeanRecruitmentPrototypeLegacy"), options);
    }

    internal static InlineHookGenerator CreateActiveFrame(X64InlineHook owner,
        ContextHookDelegate ownedDispatch, ulong site, ILogger logger)
    {
        if (owner is null || ownedDispatch is null || logger is null)
            throw new ArgumentNullException(nameof(owner));
        var options = new ContextHookOptions { Registers = FinalAdmissionRegisters };
        ContextHookDelegate dispatch = context => {
            if (!EuropeanRecruitmentInvocation.HasOwnedScope)
            {
                // A process-wide flag is demand, not identity. Restore the outer frame and
                // run the old frame exactly once when this thread has no matching attempt.
                context.Pointer->RAX = (ulong)EuropeanRecruitmentBranch.NoOwnedScope;
                return;
            }
            context.Pointer->RAX = 0;
            try { ownedDispatch(context); }
            catch (Exception error)
            {
                context.Pointer->RAX = 0;
                EuropeanRecruitmentInvocation.ReportFrameFailure(error);
            }
        };
        return ContextStub.CreateGenerator(owner, ContextStub.Guard(dispatch, options, site, logger,
            "EuropeanRecruitmentAdmissionActive"), options);
    }

    internal static void Generate(Assembler a, ReadOnlySpan<Instruction> original, ulong site,
        ulong continueAt, ulong abortAt, ulong activeFlagAddress,
        InlineHookGenerator legacyFrame, InlineHookGenerator activeFrame)
    {
        Validate(original, site, continueAt, abortAt);
        if (a is null || legacyFrame is null || activeFrame is null) throw new ArgumentNullException(nameof(a));
        if (activeFlagAddress == 0) throw new ArgumentOutOfRangeException(nameof(activeFlagAddress));
        if (IntPtr.Size != 8 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("This prototype uses the Windows-x64 context generator ABI.");

        var active = a.CreateLabel();
        var rejected = a.CreateLabel();
        var noOwnedScope = a.CreateLabel();
        // Keep this only new dormant assembly guard outside the unchanged legacy context route.
        // It does not call a new managed predicate, allocate a context object or look up game data.
        a.pushfq();
        a.push(rax);
        a.mov(rax, activeFlagAddress);
        a.cmp(__dword_ptr[rax], 0);
        a.pop(rax);
        a.jne(active);
        a.popfq();
        legacyFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        Replay(a);
        a.AddUnrestrictedJmp(continueAt);

        a.Label(ref active);
        // RFLAGS was captured BEFORE the guard. The underlying RedBird1.5.0 frame alone does
        // not preserve volatile XMMs or original flags; this wrapper applies only to opt-in calls.
        a.push(rax);
        a.sub(rsp, 96);
        a.movdqu(__xmmword_ptr[rsp], xmm0);
        a.movdqu(__xmmword_ptr[rsp + 16], xmm1);
        a.movdqu(__xmmword_ptr[rsp + 32], xmm2);
        a.movdqu(__xmmword_ptr[rsp + 48], xmm3);
        a.movdqu(__xmmword_ptr[rsp + 64], xmm4);
        a.movdqu(__xmmword_ptr[rsp + 80], xmm5);
        a.cld(); // Windows managed/native callback entry requires DF=0.
        // Its context.RSP denotes nativeSiteRSP-112, the pre-RedBird-generator RSP. An internal
        // dispatch may reconstruct the true native RSP with ActiveOuterFrameBytes; do not pretend
        // its raw field denotes the pre-wrapper frame. Legacy public payload contains no RSP.
        // Default denial belongs to the outer machine frame, before Guard can
        // suppress any callback. A skipped/faulted dispatch must never inherit an
        // incoming RAX==1 as permission. Original RAX remains in the outer save.
        a.xor(eax, eax);
        activeFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        a.cmp(rax, (int)EuropeanRecruitmentBranch.NoOwnedScope);
        a.je(noOwnedScope);
        // Combined dispatch initializes context.RAX=0 before any callback, then sets exactly1
        // only on explicit allow; the private no-scope marker is handled separately. R9 writeback is intentional finalNextUnitType and is not undone.
        a.cmp(rax, 1);
        a.jne(rejected);
        RestoreOuter(a);
        Replay(a);
        a.AddUnrestrictedJmp(continueAt);

        a.Label(ref rejected);
        RestoreOuter(a);
        // The original native frame is restored BEFORE the internal xorEAX/epilogue continuation.
        // abortAt is not a callable helper. The fixture places a marker there, not game code.
        a.AddUnrestrictedJmp(abortAt);

        a.Label(ref noOwnedScope);
        RestoreOuter(a);
        legacyFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        Replay(a);
        a.AddUnrestrictedJmp(continueAt);
    }

    // Private lowering only. Before exposing this generator, the SAME real owner
    // must admit exactly63 current bytes/ten instructions and the expanded ingress
    // and coexistence contract. This is not permission to bypass48 unowned bytes.
    // The aligned, writable cell is private process-lived native storage retained
    // with this owner. A closed caller initializes it once per attempt and binds its
    // nonzero token to this thread/call/owner/current-world BEFORE final admission.
    // The caller owns exclusive single-writer access through terminal readback; no
    // concurrent reinitialization/taint writer is allowed. State checks are NOT an
    // atomic ownership acquisition, and Completed does not merge concurrent bits.
    // A token is correlation data, not a world capability. No caller/native pointer
    // is persisted. No entry/settlement/provider API is installed by this prototype.
    //
    // Allow means entry authorization only. Entered is written before the first
    // scheduling slice read, Completed after allfive stores, never in Dispatch.
    // A stale/tainted cell after Allow is protocol uncertainty, not an authority to
    // refund an already published material charge or infer clean native refusal.
    internal static void GenerateConversionTrace(Assembler a, ReadOnlySpan<Instruction> original,
        ulong site, ulong continueAt, ulong abortAt, ulong activeFlagAddress, ulong traceCellAddress,
        InlineHookGenerator legacyFrame, InlineHookGenerator activeFrame)
    {
        ValidateConversion(original, site, continueAt, abortAt);
        if (a is null || legacyFrame is null || activeFrame is null) throw new ArgumentNullException(nameof(a));
        if (activeFlagAddress == 0) throw new ArgumentOutOfRangeException(nameof(activeFlagAddress));
        if (traceCellAddress == 0 || (traceCellAddress & 7) != 0 ||
            traceCellAddress > ulong.MaxValue - TraceCellBytes)
            throw new ArgumentOutOfRangeException(nameof(traceCellAddress));
        if (IntPtr.Size != 8 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("This prototype uses the Windows-x64 context generator ABI.");

        var active = a.CreateLabel();
        var rejected = a.CreateLabel();
        var noOwnedScope = a.CreateLabel();
        var invalidTrace = a.CreateLabel();
        a.pushfq();
        a.push(rax);
        a.mov(rax, activeFlagAddress);
        a.cmp(__dword_ptr[rax], 0);
        a.pop(rax);
        a.jne(active);
        a.popfq();
        legacyFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        Replay(a);
        ReplayConversionSlice(a);
        a.AddUnrestrictedJmp(continueAt);

        a.Label(ref active);
        a.push(rax);
        a.sub(rsp, 96);
        a.movdqu(__xmmword_ptr[rsp], xmm0);
        a.movdqu(__xmmword_ptr[rsp + 16], xmm1);
        a.movdqu(__xmmword_ptr[rsp + 32], xmm2);
        a.movdqu(__xmmword_ptr[rsp + 48], xmm3);
        a.movdqu(__xmmword_ptr[rsp + 64], xmm4);
        a.movdqu(__xmmword_ptr[rsp + 80], xmm5);
        a.cld();
        a.xor(eax, eax);
        activeFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        a.cmp(rax, (int)EuropeanRecruitmentBranch.NoOwnedScope);
        a.je(noOwnedScope);
        a.cmp(rax, 1);
        a.jne(rejected);
        // Allscratch effects below are behind explicit owned Allow. Neither the
        // dormant nor foreign/no-TLS branch touches the owned attempt's trace.
        a.mov(rax, traceCellAddress);
        a.cmp(__dword_ptr[rax + TraceStateOffset], 0);
        a.jne(invalidTrace);
        a.cmp(__qword_ptr[rax + TraceTokenOffset], 0);
        a.je(invalidTrace);
        RestoreOuter(a);
        Replay(a);
        MarkConversionEntered(a, traceCellAddress);
        ReplayConversionSlice(a);
        // No managed call, allocation, logging, new validation or native helper
        // occurs between Entered and Completed. These MOV/PUSH/POP instructions
        // preserve the post-prefix flags and the slice's real resulting RAX/RSP.
        a.push(rax);
        a.mov(rax, traceCellAddress);
        a.mov(__dword_ptr[rax + TraceStateOffset], TraceCompleted);
        a.pop(rax);
        a.AddUnrestrictedJmp(continueAt);

        a.Label(ref invalidTrace);
        a.or(__dword_ptr[rax + TraceStateOffset], TraceTainted);
        a.Label(ref rejected);
        RestoreOuter(a);
        a.AddUnrestrictedJmp(abortAt);

        a.Label(ref noOwnedScope);
        RestoreOuter(a);
        legacyFrame(a, ReadOnlySpan<Instruction>.Empty, continueAt);
        Replay(a);
        ReplayConversionSlice(a);
        a.AddUnrestrictedJmp(continueAt);
    }

    private static void MarkConversionEntered(Assembler a, ulong traceCellAddress)
    {
        a.push(rax);
        a.mov(rax, traceCellAddress);
        a.mov(__qword_ptr[rax + TraceRecordOffset], rbx);
        a.mov(__dword_ptr[rax + TraceSlotOffset], edi);
        a.mov(__dword_ptr[rax + TraceNextTypeOffset], r9d);
        a.mov(__dword_ptr[rax + TraceStateOffset], TraceEntered);
        a.pop(rax);
    }

    private static void ReplayConversionSlice(Assembler a)
    {
        a.movsxd(rax, __dword_ptr[rbx + 0x72C]);
        a.mov(__word_ptr[rbx + 0x922], r9w);
        a.mov(__word_ptr[rbx + 0x924], cx);
        a.mov(__dword_ptr[rbx + 0x918], 0x6D);
        a.mov(__word_ptr[rbx + 0x986], (ushort)0x100);
        a.mov(__word_ptr[rbx + 0x9CA], si);
    }

    private static void ValidateConversion(ReadOnlySpan<Instruction> x, ulong site, ulong next, ulong abort)
    {
        if (site > ulong.MaxValue - ConversionDisplacedBytes ||
            next != site + ConversionDisplacedBytes || x.Length != 10)
            throw new ArgumentException("Exact63-byte same-owner conversion displacement required.");
        Validate(x.Slice(0, 4), site, site + DisplacedBytes, abort);
        int[] lengths = { 7, 8, 7, 10, 9, 7 };
        for (int i = 4; i < x.Length; i++)
        {
            if (x[i].IP != x[i - 1].NextIP || x[i].Length != lengths[i - 4] ||
                x[i].HasLockPrefix || x[i].HasRepPrefix || x[i].HasRepnePrefix ||
                x[i].SegmentPrefix != Register.None)
                throw new ArgumentException("Exact whole conversion instructions required.");
        }
        if (x[4].Mnemonic != Mnemonic.Movsxd || x[4].Op0Kind != OpKind.Register ||
            x[4].Op0Register != Register.RAX || x[4].Op1Kind != OpKind.Memory ||
            !ConversionMemory(x[4], 0x72C) ||
            !StoreRegister(x[5], 0x922, Register.R9W) ||
            !StoreRegister(x[6], 0x924, Register.CX) ||
            !StoreImmediate(x[7], 0x918, OpKind.Immediate32, 0x6D) ||
            !StoreImmediate(x[8], 0x986, OpKind.Immediate16, 0x100) ||
            !StoreRegister(x[9], 0x9CA, Register.SI) || x[9].NextIP != next)
            throw new ArgumentException("Qualified native conversion scheduling slice required.");
    }

    private static bool ConversionMemory(Instruction x, ulong displacement) =>
        x.MemoryBase == Register.RBX && x.MemoryIndex == Register.None &&
        x.MemoryDisplacement64 == displacement && !x.IsIPRelativeMemoryOperand;

    private static bool StoreRegister(Instruction x, ulong displacement, Register source) =>
        x.Mnemonic == Mnemonic.Mov && x.Op0Kind == OpKind.Memory &&
        ConversionMemory(x, displacement) && x.Op1Kind == OpKind.Register && x.Op1Register == source;

    private static bool StoreImmediate(Instruction x, ulong displacement, OpKind kind, ulong value) =>
        x.Mnemonic == Mnemonic.Mov && x.Op0Kind == OpKind.Memory &&
        ConversionMemory(x, displacement) && x.Op1Kind == kind &&
        (kind == OpKind.Immediate16 ? x.Immediate16 : x.Immediate32) == value;

    private static void RestoreOuter(Assembler a)
    {
        a.movdqu(xmm0, __xmmword_ptr[rsp]);
        a.movdqu(xmm1, __xmmword_ptr[rsp + 16]);
        a.movdqu(xmm2, __xmmword_ptr[rsp + 32]);
        a.movdqu(xmm3, __xmmword_ptr[rsp + 48]);
        a.movdqu(xmm4, __xmmword_ptr[rsp + 64]);
        a.movdqu(xmm5, __xmmword_ptr[rsp + 80]);
        a.add(rsp, 96);
        a.pop(rax);
        a.popfq();
    }

    private static void Replay(Assembler a)
    {
        a.movsxd(rax, edi);
        a.imul(rcx, rax, 0x490);
        a.add(rbx, rcx);
        a.xor(ecx, ecx);
    }

    private static void Validate(ReadOnlySpan<Instruction> x, ulong site, ulong next, ulong abort)
    {
        if (site < AbortDelta || site > ulong.MaxValue - DisplacedBytes || next != site + DisplacedBytes || abort != site - AbortDelta || x.Length != 4)
            throw new ArgumentException("Exact native site/continuation/abort contract required.");
        if (x[0].IP != site || x[0].Length != 3 || x[0].Mnemonic != Mnemonic.Movsxd ||
            x[0].Op0Kind != OpKind.Register || x[0].Op0Register != Register.RAX ||
            x[0].Op1Kind != OpKind.Register || x[0].Op1Register != Register.EDI ||
            x[1].Length != 7 || x[1].Mnemonic != Mnemonic.Imul ||
            x[1].Op0Register != Register.RCX || x[1].Op1Register != Register.RAX ||
            x[1].Op2Kind != OpKind.Immediate32to64 || x[1].Immediate32to64 != 0x490 ||
            x[2].Length != 3 || x[2].Mnemonic != Mnemonic.Add ||
            x[2].Op0Kind != OpKind.Register || x[2].Op0Register != Register.RBX ||
            x[2].Op1Kind != OpKind.Register || x[2].Op1Register != Register.RCX ||
            x[3].Length != 2 || x[3].Mnemonic != Mnemonic.Xor ||
            x[3].Op0Kind != OpKind.Register || x[3].Op0Register != Register.ECX ||
            x[3].Op1Kind != OpKind.Register || x[3].Op1Register != Register.ECX)
            throw new ArgumentException("Qualified four-instruction native prefix required.");
        for (int i = 1; i < x.Length; i++)
            if (x[i].IP != x[i - 1].NextIP) throw new ArgumentException("Displaced instructions are not contiguous.");
        if (x[3].NextIP != next) throw new ArgumentException("Native continuation is not a whole instruction boundary.");
    }
}
