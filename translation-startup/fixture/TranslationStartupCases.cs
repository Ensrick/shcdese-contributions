#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CrusaderDE;
using MonoMod.RuntimeDetour;
using SHCDESE.API;
using SHCDESE.Logging;
using SHCDESE.ManagedHooks;

// Actual five production files, including the WHOLE genuine Manager, compile.
// ApplyLeaves supplies unrelated compile-only/control-flow leaves; no extracted
// Apply/Unload implementation or replacement Manager decision model is compiled. MonoMod, Noesis, game objects,
// registry and logging below are explicit controlled leaves: no executable
// patch, game CLR load, font/XAML rendering, native code or audio is exercised.
internal static class TranslationStartupCases
{
    internal static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static ManagedHookManager Fresh()
    {
        Driver.Reset();
        var manager = (ManagedHookManager)Activator.CreateInstance(typeof(ManagedHookManager), nonPublic: true)!;
        Driver.Manager = manager;
        manager.ApplyEarly();
        manager.Apply();
        return manager;
    }

    private static void OtherComponent()
    {
        var manager = Fresh();
        Check(Driver.TranslationPreparationAttempts == 0,
            "CAUSAL_REGRESSION: genuine Apply attempted Translate preparation before game-owned initialization.");
        Check(Hook.All.Any(h => h.From.DeclaringType == typeof(EditorDirector) && h.IsApplied),
            "Genuine Apply did not reach its final unrelated hook; swallowed setup failure is not deferred-startup success.");
        Check(!manager.TranslationFieldsReadyForFixture(), "Apply accepted must not imply translation-active.");
        Noesis.GUI.LoadComponent(new object(), "early.xaml");
        Check(!Driver.GameReady && Hook.Translations.Count == 0, "Other/early components must not construct Translate hooks.");
        Check(Driver.OriginalLoads == 1 && Driver.Bindings == 1, "Other component original/POST binding changed.");
        manager.Unload();
    }

    private static void MasterPreAndOutputs()
    {
        var manager = Fresh();
        Driver.CheckMasterOriginal = () => Check(Hook.Translations.Count == 3 && Hook.Translations.All(h => h.IsApplied), "All3 hooks must be active before original Master XAML load.");
        _ = new MasterController();
        Check(Hook.Translations.All(h => h.Manual), "Preparation must be explicitly unapplied.");
        Check(Driver.OriginalLoads == 1 && Driver.Bindings == 1, "Master load/injection must occur exactly once.");
        Check(Driver.Events.IndexOf("original") < Driver.Events.IndexOf("binding"), "POST binding moved before original.");
        GameTranslateAPI.Instance.Single["custom"] = "overridden";
        GameTranslateAPI.Instance.Extended["TEXT_GOODS_2"] = "extended";
        var translate = new Translate();
        Check(translate.lookUpText("custom") == "overridden", "String override output changed.");
        Check(translate.lookUpText("TEXT_GOODS", 2) == "extended", "Section-string override output changed.");
        Check(translate.lookUpText(Enums.eTextSections.TEXT_GOODS, 2) == "extended", "Enum override output changed.");
        Check(translate.lookUpText("native") == "native:native", "String native fallback changed.");
        Check(translate.lookUpText("TEXT_GOODS", 7) == "native:TEXT_GOODS_7", "Section-string fallback changed.");
        Check(translate.lookUpText(Enums.eTextSections.TEXT_GOODS, 7) == "native:TEXT_GOODS_7", "Enum fallback changed.");
        Check(Driver.NativeLookups == 3, "Override or fallback invoked originals an incorrect number of times.");
        int constructed = Hook.Translations.Count;
        Noesis.GUI.LoadComponent(Driver.Master!, "MasterController.xaml");
        Check(Hook.Translations.Count == constructed && Driver.OriginalLoads == 2 && Driver.Bindings == 2, "Repeated Master load installed again or lost callbacks.");
        manager.Unload();
        Check(Hook.Translations.All(h => !h.IsApplied), "Unload must undo the complete group.");
    }

    private static void PublishedBeforeAnyApply()
    {
        var manager = Fresh();
        Hook.Step = (hook, phase) =>
        {
            if (!hook.IsTranslation || phase != "apply") return;
            Check(manager.TranslationFieldsReadyForFixture(), "Apply exposed a callback before all3 wrappers/trampolines were published.");
            Exception? error = null;
            var worker = new Thread(() =>
            {
                try { Check(new Translate().lookUpText("x") == "native:x", "Concurrent lookup failed."); }
                catch (Exception ex) { error = ex; }
            });
            worker.Start();
            Check(worker.Join(2000), "Lookup callback must not wait on the startup/unload gate.");
            if (error != null) throw error;
        };
        _ = new MasterController();
        Check(Driver.OriginalLoads == 1 && Hook.Translations.Count == 3, "Publication fixture did not finish the real entry.");
        manager.Unload();
    }

    private static void ReentrantLoads()
    {
        foreach (string point in new[] { "ctor", "generate", "apply" })
        {
            var manager = Fresh(); bool fired = false;
            Hook.Step = (hook, phase) =>
            {
                if (hook.IsTranslation && phase == point && !fired)
                {
                    fired = true;
                    Noesis.GUI.LoadComponent(Driver.Master!, "nested-master.xaml");
                }
            };
            _ = new MasterController();
            Check(fired && Hook.Translations.Count == 3 && Hook.Translations.All(h => h.IsApplied), "Reentrant load duplicated/abandoned the group at " + point);
            Check(Driver.OriginalLoads == 2 && Driver.Bindings == 2, "Reentrant original/POST pairing changed at " + point);
            manager.Unload();
        }
    }

    private static void PreparationAndApplyFailures()
    {
        foreach (string point in new[] { "ctor", "generate", "apply", "log" })
        for (int ordinal = 1; ordinal <= 3; ordinal++)
        {
            var manager = Fresh(); bool fired = false;
            Hook.Step = (hook, phase) =>
            {
                if (hook.IsTranslation && phase == point && hook.Ordinal == ordinal && !fired)
                { fired = true; throw Driver.Injected; }
            };
            _ = new MasterController();
            Check(fired && Hook.Translations.All(h => !h.IsApplied), "Partial group survived controlled " + point + ordinal);
            Check(Driver.OriginalLoads == 1 && Driver.Bindings == 1, "Startup failure skipped/duplicated native load or POST binding.");
            Check(LogHelper.Errors.Count != 0, "Actual startup failure was not reported.");
            int count = Hook.Translations.Count;
            _ = new MasterController();
            Check(Hook.Translations.Count == count, "Failed one-shot group retried on a later Master load.");
            Check(Driver.OriginalLoads == 2 && Driver.Bindings == 2, "Later native load pairing changed after failure.");
            manager.Unload();
        }
    }

    private static void ReentrantUnload()
    {
        foreach (string point in new[] { "ctor", "generate", "apply" })
        for (int ordinal = 1; ordinal <= 3; ordinal++)
        {
            var manager = Fresh(); bool fired = false;
            Hook.Step = (hook, phase) =>
            {
                if (hook.IsTranslation && phase == point && hook.Ordinal == ordinal && !fired)
                { fired = true; manager.Unload(); }
            };
            var master = new MasterController();
            Check(fired && Hook.Translations.All(h => !h.IsApplied), "Reentrant unload left/reapplied an active hook at " + point + ordinal);
            int count = Hook.Translations.Count;
            manager.InvokeLoadEntryForFixture(master);
            Check(Hook.Translations.Count == count && Hook.Translations.All(h => !h.IsApplied), "A late captured callback installed after unload.");
            Check(Driver.OriginalLoads == 2 && Driver.Bindings == 2, "Unload/reentry must still preserve each original/POST pair.");
        }
    }

    private static void CrossThreadUnload()
    {
        var manager = Fresh();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var unloadStarted = new ManualResetEventSlim();
        Exception? loadError = null, unloadError = null;
        Hook.Step = (hook, phase) =>
        {
            if (hook.IsTranslation && hook.Ordinal == 1 && phase == "generate")
            { entered.Set(); Check(release.Wait(2000), "Controlled preparation handshake timed out."); }
        };
        var load = new Thread(() => { try { _ = new MasterController(); } catch (Exception ex) { loadError = ex; } });
        var unload = new Thread(() => { unloadStarted.Set(); try { manager.Unload(); } catch (Exception ex) { unloadError = ex; } });
        load.Start();
        try
        {
            Check(entered.Wait(2000), "Preparation did not enter controlled handshake.");
            unload.Start(); Check(unloadStarted.Wait(2000), "Unload thread did not start.");
        }
        finally { release.Set(); }
        Check(load.Join(2000) && unload.Join(2000), "Worker failed to close after release.");
        if (loadError != null) throw loadError;
        if (unloadError != null) throw unloadError;
        Check(Hook.Translations.All(h => !h.IsApplied), "Concurrent unload left a late active hook.");
        int count = Hook.Translations.Count;
        manager.InvokeLoadEntryForFixture(Driver.Master!);
        Check(Hook.Translations.Count == count, "Late callback after concurrent unload retried initialization.");
    }

    private static void UndoFailureRetainsOwner()
    {
        var manager = Fresh(); bool applyFailed = false, undoFailed = false;
        Hook.Step = (hook, phase) =>
        {
            if (!hook.IsTranslation || hook.Ordinal != 2) return;
            if (phase == "apply" && !applyFailed) { applyFailed = true; throw Driver.Injected; }
            if (phase == "undo" && !undoFailed) { undoFailed = true; throw Driver.Injected; }
        };
        _ = new MasterController();
        Check(applyFailed && undoFailed && LogHelper.Errors.Count >= 2, "Both primary/cleanup failures must be observable.");
        Check(manager.TranslationFieldsReadyForFixture(), "Undo failure discarded callback/trampoline owners.");
        Check(Hook.Translations.Where(h => h.Ordinal != 2).All(h => !h.IsApplied), "One Undo failure skipped remaining cleanup.");
        Check(new Translate().lookUpText("TEXT_GOODS", 8) == "native:TEXT_GOODS_8", "A retained active callback lost its original trampoline.");
        manager.Unload();
        Check(Hook.Translations.All(h => !h.IsApplied), "A retained handle could not be cleaned on unload.");
    }

    private static void ProviderVetoesAreObserved()
    {
        foreach (int ordinal in new[] { 1, 2, 3 })
        {
            var manager = Fresh();
            Hook.OnDetour = (hook, from, to, target) => !hook.IsTranslation || hook.Ordinal != ordinal;
            _ = new MasterController();
            Check(Hook.Translations.All(h => !h.IsApplied) && LogHelper.Errors.Count != 0,
                "A nonthrowing Apply veto was marked installed or left a partial group.");
            int count = Hook.Translations.Count;
            _ = new MasterController();
            Check(Hook.Translations.Count == count && Driver.OriginalLoads == 2 && Driver.Bindings == 2,
                "A vetoed one-shot activation retried or lost original/POST pairing.");
            manager.Unload();
        }
        {
            var manager = Fresh(); _ = new MasterController();
            Hook.OnUndo = hook => !hook.IsTranslation || hook.Ordinal != 2;
            manager.Unload();
            Check(Hook.Translations.Single(h => h.Ordinal == 2).IsApplied && LogHelper.Errors.Count != 0,
                "A nonthrowing Undo veto was reported as successful cleanup.");
            Check(manager.TranslationFieldsReadyForFixture() && Hook.Translations.Where(h => h.Ordinal != 2).All(h => !h.IsApplied),
                "Undo veto lost an owner or skipped independent cleanup.");
            Hook.OnUndo = null; manager.Unload();
            Check(Hook.Translations.All(h => !h.IsApplied), "Retained vetoed handle could not be cleaned after veto removal.");
        }
    }

    private static void UndoAndDiagnosticFailureStillCleans()
    {
        var manager = Fresh(); _ = new MasterController();
        bool fired = false; LogHelper.ThrowErrorOnce = true;
        Hook.Step = (hook, phase) =>
        {
            if (hook.IsTranslation && hook.Ordinal == 3 && phase == "undo" && !fired)
            { fired = true; throw Driver.Injected; }
        };
        manager.Unload();
        Check(fired && Driver.FallbackDiagnostics.Count == 1, "Undo plus diagnostic failure did not reach the independent Unity sink.");
        Check(Hook.Translations.Where(h => h.Ordinal != 3).All(h => !h.IsApplied), "Diagnostic failure aborted remaining rollback.");
        Check(Hook.All.Where(h => h.From.DeclaringType == typeof(Noesis.GUI)).All(h => !h.IsApplied),
            "Diagnostic failure aborted the existing GUI entry unload path.");
        // The former fixture created only this GUI entry outside translations.
        // Real unrelated hooks omitted by legacy Unload remain outside this fix.
        Check(manager.TranslationFieldsReadyForFixture(), "Failed cleanup discarded retained owners.");
        manager.Unload();
        Check(Hook.Translations.All(h => !h.IsApplied), "Retained failed handle could not be retried by explicit unload.");
    }

    private static void UnexpectedDiagnosticFailurePairing()
    {
        var manager = Fresh(); bool fired = false;
        LogHelper.ThrowErrorOnce = true;
        Hook.Step = (hook, phase) =>
        {
            if (hook.IsTranslation && phase == "generate" && !fired)
            { fired = true; throw Driver.Injected; }
        };
        _ = new MasterController();
        Check(fired && Driver.OriginalLoads == 1 && Driver.Bindings == 1,
            "Unexpected diagnostic exception skipped original or existing POST binding.");
        Check(Hook.Translations.All(h => !h.IsApplied), "Unexpected diagnostic exception skipped rollback.");
        manager.Unload();
    }

    private static void OriginalFailurePairing()
    {
        var manager = Fresh(); Driver.ThrowOriginal = true;
        Exception? seen = null;
        try { _ = new MasterController(); } catch (Exception ex) { seen = ex; }
        Check(ReferenceEquals(seen, Driver.Injected), "Original exception identity/propagation changed.");
        Check(Driver.OriginalLoads == 1 && Driver.Bindings == 0, "Original failure caused retry or POST injection.");
        manager.Unload();
    }

    private static void BindingFailurePairing()
    {
        var manager = Fresh(); Driver.ThrowBinding = true;
        _ = new MasterController();
        Check(Driver.OriginalLoads == 1 && Driver.Bindings == 1 && LogHelper.Errors.Count == 1, "Existing POST binding failure isolation changed.");
        manager.Unload();
    }

    private static void UnloadBeforeFirstMaster()
    {
        var manager = Fresh(); manager.Unload();
        var master = new MasterController();
        manager.InvokeLoadEntryForFixture(master);
        Check(Hook.Translations.Count == 0, "A pending or captured callback installed after unload.");
        Check(Driver.OriginalLoads == 2 && Driver.Bindings == 1, "Unloaded native path/captured callback pairing changed.");
    }

    private static int Main(string[] args)
    {
        var groups = new (string, Action)[]
        {
            ("Translation.OtherComponent", OtherComponent),
            ("Translation.MasterPreAndOutputs", MasterPreAndOutputs),
            ("Translation.PublishedBeforeAnyApply", PublishedBeforeAnyApply),
            ("Translation.ReentrantLoads", ReentrantLoads),
            ("Translation.PreparationAndApplyFailures", PreparationAndApplyFailures),
            ("Translation.ReentrantUnload", ReentrantUnload),
            ("Translation.CrossThreadUnload", CrossThreadUnload),
            ("Translation.UndoFailureRetainsOwner", UndoFailureRetainsOwner),
            ("Translation.ProviderVetoesAreObserved", ProviderVetoesAreObserved),
            ("Translation.UndoAndDiagnosticFailureStillCleans", UndoAndDiagnosticFailureStillCleans),
            ("Translation.UnexpectedDiagnosticFailurePairing", UnexpectedDiagnosticFailurePairing),
            ("Translation.OriginalFailurePairing", OriginalFailurePairing),
            ("Translation.BindingFailurePairing", BindingFailurePairing),
            ("Translation.UnloadBeforeFirstMaster", UnloadBeforeFirstMaster),
        };
        bool baselineApply = args.SequenceEqual(new[] { "--baseline-apply" });
        if (args.Length != 0 && !baselineApply) return 2;
        if (baselineApply) groups = groups.Take(1).ToArray();
        Console.WriteLine("RUNTIME " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription +
            " | " + Environment.Version + " | x64=" + Environment.Is64BitProcess);
        int failed = 0;
        foreach (var group in groups)
        {
            try { group.Item2(); Console.WriteLine("PASS " + group.Item1); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + group.Item1 + ": " + ex); }
            finally { Driver.Manager?.Unload(); }
        }
        Console.WriteLine("LIMIT: actual production managed source over explicit MonoMod/Noesis/game/registry/log leaves; no executable hooks or native/XAML text equivalence.");
        return failed == 0 ? 0 : 1;
    }
}

public class Enums { public enum eTextSections { TEXT_GOODS = 2 } }

internal static class Driver
{
    internal static ManagedHookManager? Manager;
    internal static MasterController? Master;
    internal static bool GameReady, ThrowOriginal, ThrowBinding;
    internal static int OriginalLoads, Bindings, NativeLookups, TranslationPreparationAttempts;
    internal static Action? CheckMasterOriginal;
    internal static readonly Exception Injected = new InvalidOperationException("Controlled fixture fault; not a game failure.");
    internal static readonly List<string> Events = new List<string>();
    internal static readonly List<Exception> FallbackDiagnostics = new List<Exception>();
    internal static void Reset()
    {
        Manager = null; Master = null; GameReady = ThrowOriginal = ThrowBinding = false;
        OriginalLoads = Bindings = NativeLookups = TranslationPreparationAttempts = 0; CheckMasterOriginal = null;
        Events.Clear(); FallbackDiagnostics.Clear(); Hook.All.Clear(); Hook.Step = null; Hook.OnDetour = null; Hook.OnUndo = null; LogHelper.Errors.Clear(); LogHelper.ThrowErrorOnce = false;
        GameTranslateAPI.Instance.Single.Clear(); GameTranslateAPI.Instance.Extended.Clear();
    }
}

namespace MonoMod.RuntimeDetour
{
    public struct HookConfig { public bool ManualApply; }
    public sealed class Hook
    {
        internal static readonly List<Hook> All = new List<Hook>();
        internal static Action<Hook, string>? Step;
        public static Func<Hook, MethodBase, MethodBase, object?, bool>? OnDetour;
        public static Func<Hook, bool>? OnUndo;
        internal readonly MethodBase From;
        internal readonly Delegate Target;
        internal readonly bool Manual;
        public bool IsApplied { get; private set; }
        internal bool IsTranslation => From.DeclaringType == typeof(Translate);
        internal int Ordinal { get; }
        internal static List<Hook> Translations => All.Where(h => h.IsTranslation).ToList();
        public Hook(MethodBase from, Delegate to) : this(from, to, default) { }
        public Hook(MethodBase from, Delegate to, HookConfig config)
        {
            From = from; Target = to; Manual = config.ManualApply;
            Ordinal = IsTranslation ? Translations.Count + 1 : 0;
            if (IsTranslation) Driver.TranslationPreparationAttempts++;
            if (IsTranslation) TranslationStartupCases.Check(Driver.GameReady, "Premature Translate preparation before game-owned MainVM completion.");
            All.Add(this); Step?.Invoke(this, "ctor");
            if (!Manual) Apply();
        }
        public T GenerateTrampoline<T>() where T : Delegate
        {
            Step?.Invoke(this, "generate");
            Type type; string name;
            if (!IsTranslation && From.DeclaringType != typeof(Noesis.GUI))
                return (T)(Target.Target == null ? Delegate.CreateDelegate(typeof(T), Target.Method)
                    : Delegate.CreateDelegate(typeof(T), Target.Target, Target.Method));
            if (From.DeclaringType == typeof(Noesis.GUI)) { type = typeof(Noesis.GUI); name = nameof(Noesis.GUI.Original); }
            else
            {
                type = typeof(Translate); var args = From.GetParameters();
                name = args.Length == 1 ? nameof(Translate.OriginalString) : args[0].ParameterType == typeof(string) ? nameof(Translate.OriginalSection) : nameof(Translate.OriginalEnum);
            }
            return (T)Delegate.CreateDelegate(typeof(T), type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!);
        }
        public void Apply()
        {
            if (OnDetour?.Invoke(this, From, Target.Method, Target.Target) == false) return;
            IsApplied = true; Step?.Invoke(this, "apply");
        }
        public void Undo()
        {
            if (OnUndo?.Invoke(this) == false) return;
            Step?.Invoke(this, "undo"); IsApplied = false;
        }
        // MonoMod accepts any compatible Delegate by method/target signature;
        // C# may infer Action/Func at a call whose parameter is just Delegate.
        // Bridge the controlled leaf to the requested ABI delegate instead of
        // requiring the incoming delegate's runtime class to be identical.
        internal T TargetFor<T>() where T : Delegate
        {
            if (Target is T exact) return exact;
            return (T)(Target.Target == null
                ? Delegate.CreateDelegate(typeof(T), Target.Method)
                : Delegate.CreateDelegate(typeof(T), Target.Target, Target.Method));
        }
        internal static Hook? Find(MethodBase method) => All.LastOrDefault(h => h.IsApplied && h.From == method);
    }
}
namespace CrusaderDE
{
    public sealed partial class MainViewModel
    {
        internal static MainViewModel? Instance;
        internal static MainViewModel INIT() { var ready = new MainViewModel(); Instance = ready; Driver.GameReady = true; return ready; }
    }
    public sealed class MasterController
    {
        public MasterController()
        {
            // Explicit game leaf follows independently qualified actual BC8 IL.
            _ = MainViewModel.INIT(); Driver.Master = this;
            Noesis.GUI.LoadComponent(this, "Assets/GUI/XAML/MasterController.xaml");
        }
    }
    public sealed class Translate
    {
        public string lookUpText(string index)
        {
            var method = typeof(Translate).GetMethod(nameof(lookUpText), new[] { typeof(string) })!;
            return Hook.Find(method) is Hook hook ? hook.TargetFor<ManagedHookManager.translate_lookUpTextDelegate>()(this, index) : OriginalString(this, index);
        }
        public string lookUpText(string section, int index)
        {
            var method = typeof(Translate).GetMethod(nameof(lookUpText), new[] { typeof(string), typeof(int) })!;
            return Hook.Find(method) is Hook hook ? hook.TargetFor<ManagedHookManager.translate_lookUpText2Delegate>()(this, section, index) : OriginalSection(this, section, index);
        }
        public string lookUpText(Enums.eTextSections section, int index)
        {
            var method = typeof(Translate).GetMethod(nameof(lookUpText), new[] { typeof(Enums.eTextSections), typeof(int) })!;
            return Hook.Find(method) is Hook hook ? hook.TargetFor<ManagedHookManager.translate_lookUpTextExDelegate>()(this, section, index) : OriginalEnum(this, section, index);
        }
        public static string OriginalString(Translate self, string value) { Driver.NativeLookups++; return "native:" + value; }
        public static string OriginalSection(Translate self, string section, int value) { Driver.NativeLookups++; return "native:" + section + "_" + value; }
        public static string OriginalEnum(Translate self, Enums.eTextSections section, int value) { Driver.NativeLookups++; return "native:" + section + "_" + value; }
    }
}
namespace Noesis
{
    public static class GUI
    {
        public static void LoadComponent(object component, string filename)
        {
            var method = typeof(GUI).GetMethod(nameof(LoadComponent))!;
            if (Hook.Find(method) is Hook hook) hook.TargetFor<ManagedHookManager.NoesisGUI_LoadComponent_Delegate>()(component, filename);
            else Original(component, filename);
        }
        public static void Original(object component, string filename)
        {
            Driver.OriginalLoads++; Driver.Events.Add("original");
            if (component is MasterController) Driver.CheckMasterOriginal?.Invoke();
            if (Driver.ThrowOriginal) throw Driver.Injected;
        }
    }
}
namespace UnityEngine
{
    public static class Debug
    {
        public static void LogException(Exception error) => Driver.FallbackDiagnostics.Add(error);
    }
}
namespace SHCDESE.API
{
    public sealed class GameXAMLManagerAPI
    {
        public static readonly GameXAMLManagerAPI Instance = new GameXAMLManagerAPI();
        public void InjectBindings(object component)
        {
            Driver.Bindings++; Driver.Events.Add("binding");
            if (Driver.ThrowBinding) throw Driver.Injected;
        }
    }
    public sealed class GameTranslateAPI
    {
        public static readonly GameTranslateAPI Instance = new GameTranslateAPI();
        internal readonly Dictionary<string, string> Single = new Dictionary<string, string>();
        internal readonly Dictionary<string, string> Extended = new Dictionary<string, string>();
        public string GetOverwrittenLookUpText(string key) => Single.TryGetValue(key, out var value) ? value : "";
        public string GetOverwrittenLookUpTextEx(string section, int index) => Extended.TryGetValue(section + "_" + index, out var value) ? value : "";
    }
}
namespace SHCDESE.Logging
{
    public static class LogHelper
    {
        internal static readonly List<string> Errors = new List<string>();
        internal static bool ThrowErrorOnce;
        public static void Information(string value)
        {
            if (value.StartsWith("Prepared translation detour", StringComparison.Ordinal))
                Hook.Step?.Invoke(Hook.Translations.Last(), "log");
        }
        public static void Debug(string value) { }
        public static void Error(Exception error, string value)
        {
            Errors.Add(value);
            if (ThrowErrorOnce) { ThrowErrorOnce = false; throw Driver.Injected; }
        }
    }
}
namespace SHCDESE.ManagedHooks
{
    public partial class ManagedHookManager
    {
        internal bool TranslationFieldsReadyForFixture() => translate_lookUpText_hook?.Trampoline != null &&
            translate_lookUpText2_hook?.Trampoline != null && translate_lookUpTextEx_hook?.Trampoline != null;
        internal void InvokeLoadEntryForFixture(MasterController master) => NoesisGUI_LoadComponent_Hook(master, "late-master.xaml");
    }
}
