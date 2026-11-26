using MonoMod.RuntimeDetour;
using SHCDESE.Logging;
using System;
using System.Reflection;

namespace SHCDESE.ManagedHooks;

public class ManagedDetour<T> where T : Delegate
{
    public Hook Hook { get; set; }
    public T Trampoline { get; set; }

    // The deferred translation group owns an unapplied Hook while preparing its
    // trampoline, so failed preparation still has a handle to undo. The public
    // constructor and its immediate-apply behavior remain unchanged.
    internal ManagedDetour(Hook hook, T trampoline)
    {
        Hook = hook ?? throw new ArgumentNullException(nameof(hook));
        Trampoline = trampoline ?? throw new ArgumentNullException(nameof(trampoline));
    }

    public ManagedDetour(MethodBase from, Delegate to)
    {
        Hook = new Hook(from, to);
        Trampoline = Hook.GenerateTrampoline<T>();

        LogHelper.Information($"Created detour [{from.Name}]->[{to.Method.Name ?? "N/A"}]");
    }
}
