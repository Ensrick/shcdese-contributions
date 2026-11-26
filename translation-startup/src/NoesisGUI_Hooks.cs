using CrusaderDE;
using SHCDESE.API;
using SHCDESE.Logging;
using System;

namespace SHCDESE.ManagedHooks;

public partial class ManagedHookManager
{
    //
    // NoesisGUI
    //

    internal static ManagedDetour<NoesisGUI_LoadComponent_Delegate> noesisGUI_LoadComponent_Hook;
    internal delegate void NoesisGUI_LoadComponent_Delegate(object component, string filename);
    internal void NoesisGUI_LoadComponent_Hook(object component, string filename)
    {
        Exception? startupFailure = null;
        if (component is MasterController)
        {
            try
            {
                ApplyTranslationHooksBeforeMasterControllerLoad();
            }
            catch (Exception ex)
            {
                // Even an unexpected preparation/diagnostic failure must not skip
                // the game's original load or move binding injection ahead of it.
                startupFailure = ex;
            }
        }
        noesisGUI_LoadComponent_Hook.Trampoline(component, filename);
        try
        {
            if (GameXAMLManagerAPI.Instance != null)
            {
                LogHelper.Debug($"Loading component: {component.GetType().Name} for filename: [{filename}]");
                GameXAMLManagerAPI.Instance.InjectBindings(component);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(ex, "Exception during Noesis GUI LoadComponent");
        }
        if (startupFailure != null)
        {
            LogHelper.Error(startupFailure, "Deferred translation startup failed before MasterController load");
        }
    }
}
