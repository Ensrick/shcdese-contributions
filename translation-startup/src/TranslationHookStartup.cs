using CrusaderDE;
using MonoMod.RuntimeDetour;
using SHCDESE.Logging;
using System;
using System.Reflection;
using System.Threading;

namespace SHCDESE.ManagedHooks;

public partial class ManagedHookManager
{
    private enum TranslationStartupState { Pending, Preparing, Applying, Installed, Failed, Unloaded }
    private readonly object translationStartupGate = new object();
    private TranslationStartupState translationStartupState;
    // Retain every returned handle, including a failed/undone one. In particular,
    // an Undo failure must not discard the only owner of a still-live callback.
    private Hook? preparedTranslationHook1;
    private Hook? preparedTranslationHook2;
    private Hook? preparedTranslationHook3;
    // Bound fault ownership if the normal and Unity diagnostic sinks both fail;
    // reporting must not prevent cleanup or the original GUI/legacy unload path.
    private Exception? translationStartupFailure;
    private Exception? translationUndoFailure;
    private Exception? translationDiagnosticFailure;
    private Exception? translationDiagnosticFallbackFailure;

    // MasterController calls MainViewModel.INIT and stores its DataContext before
    // GUI.LoadComponent. That is the game-owned construction boundary: no timer,
    // singleton manufacture, readiness predicate, new hook or Update polling.
    // This private gate serializes preparation/application against cross-thread
    // unload. Lookup callbacks never take the gate. All three trampolines/fields
    // are published before any callback can become active; reentrant loads skip.
    private void ApplyTranslationHooksBeforeMasterControllerLoad()
    {
        lock (translationStartupGate)
        {
            if (translationStartupState != TranslationStartupState.Pending)
                return;
            translationStartupState = TranslationStartupState.Preparing;
            bool committed = false;
            try
            {
                var first = PrepareTranslationHook<translate_lookUpTextDelegate>(ref preparedTranslationHook1,
                    typeof(Translate).GetMethod(nameof(Translate.lookUpText), BindingFlags.Public | BindingFlags.Instance,
                        null, new Type[] { typeof(string) }, null), Translate_lookUpText_Hook);
                if (first == null) return;
                var second = PrepareTranslationHook<translate_lookUpText2Delegate>(ref preparedTranslationHook2,
                    typeof(Translate).GetMethod(nameof(Translate.lookUpText), BindingFlags.Public | BindingFlags.Instance,
                        null, new Type[] { typeof(string), typeof(int) }, null), Translate_lookUpText2_Hook);
                if (second == null) return;
                var third = PrepareTranslationHook<translate_lookUpTextExDelegate>(ref preparedTranslationHook3,
                    typeof(Translate).GetMethod(nameof(Translate.lookUpText), BindingFlags.Public | BindingFlags.Instance,
                        null, new Type[] { typeof(Enums.eTextSections), typeof(int) }, null), Translate_lookUpTextEx_Hook);
                if (third == null) return;

                Volatile.Write(ref translate_lookUpText_hook, first);
                Volatile.Write(ref translate_lookUpText2_hook, second);
                Volatile.Write(ref translate_lookUpTextEx_hook, third);
                translationStartupState = TranslationStartupState.Applying;
                preparedTranslationHook1!.Apply();
                if (translationStartupState != TranslationStartupState.Applying) return;
                if (!preparedTranslationHook1.IsApplied)
                    throw new InvalidOperationException("Deferred translation hook1 application was declined or did not become active.");
                preparedTranslationHook2!.Apply();
                if (translationStartupState != TranslationStartupState.Applying) return;
                if (!preparedTranslationHook2.IsApplied)
                    throw new InvalidOperationException("Deferred translation hook2 application was declined or did not become active.");
                preparedTranslationHook3!.Apply();
                if (translationStartupState != TranslationStartupState.Applying) return;
                if (!preparedTranslationHook3.IsApplied)
                    throw new InvalidOperationException("Deferred translation hook3 application was declined or did not become active.");
                LogHelper.Information("Applied three translation hooks before MasterController XAML load");
                if (translationStartupState != TranslationStartupState.Applying) return;
                translationStartupState = TranslationStartupState.Installed;
                committed = true;
            }
            catch (Exception ex)
            {
                if (translationStartupState != TranslationStartupState.Unloaded)
                    translationStartupState = TranslationStartupState.Failed;
                translationStartupFailure ??= ex;
                ReportTranslationHookFailure(ex, "Failed to initialize deferred translation hooks; retaining owned handles for rollback");
            }
            finally
            {
                if (!committed)
                    UndoPreparedTranslationHooks();
            }
        }
    }

    private ManagedDetour<T>? PrepareTranslationHook<T>(ref Hook? owned, MethodInfo from, T callback) where T : Delegate
    {
        // ManualApply only prevents publication of executable hooks. It is not
        // assumed to prevent compilation/static initialization, so this method
        // is called only at the proved game-owned MainViewModel boundary.
        owned = new Hook(from, callback, new HookConfig { ManualApply = true });
        if (owned.IsApplied)
            throw new InvalidOperationException("Translation hook preparation unexpectedly became active before publication.");
        if (translationStartupState != TranslationStartupState.Preparing) return null;
        T trampoline = owned.GenerateTrampoline<T>();
        if (translationStartupState != TranslationStartupState.Preparing) return null;
        var prepared = new ManagedDetour<T>(owned, trampoline);
        LogHelper.Information($"Prepared translation detour [{from.Name}]->[{callback.Method.Name}]");
        return translationStartupState == TranslationStartupState.Preparing ? prepared : null;
    }

    private void StopTranslationHookStartup()
    {
        lock (translationStartupGate)
        {
            // Terminal before cleanup/callouts: a reentrant or delayed component
            // load must never create/reapply this group after unload starts.
            translationStartupState = TranslationStartupState.Unloaded;
            UndoPreparedTranslationHooks();
        }
    }

    private void UndoPreparedTranslationHooks()
    {
        UndoPreparedTranslationHook(preparedTranslationHook3);
        UndoPreparedTranslationHook(preparedTranslationHook2);
        UndoPreparedTranslationHook(preparedTranslationHook1);
    }

    private void UndoPreparedTranslationHook(Hook? hook)
    {
        if (hook == null) return;
        try
        {
            hook.Undo();
            if (hook.IsApplied)
                throw new InvalidOperationException("Deferred translation hook undo was declined or remained active; retaining its owner.");
        }
        catch (Exception ex)
        {
            translationUndoFailure ??= ex;
            ReportTranslationHookFailure(ex, "Failed to undo a retained deferred translation hook");
        }
    }

    private void ReportTranslationHookFailure(Exception error, string message)
    {
        try { LogHelper.Error(error, message); }
        catch (Exception diagnosticFailure)
        {
            translationDiagnosticFailure ??= diagnosticFailure;
            try
            {
                UnityEngine.Debug.LogException(new AggregateException(message, error, diagnosticFailure));
            }
            catch (Exception fallbackFailure)
            {
                // Keep bounded ownership of both diagnostic failures. There is
                // no further reliable sink; every hook must still be attempted.
                translationDiagnosticFallbackFailure ??= fallbackFailure;
            }
        }
    }
}
