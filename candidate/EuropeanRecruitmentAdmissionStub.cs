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
