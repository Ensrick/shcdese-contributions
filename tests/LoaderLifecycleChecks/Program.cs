using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SHCDESE.Detours;
using SHCDESE.EventAPI;
using SHCDESE.EventAPI.MapLoader;
using SHCDESE.Logging;

// Actual complete production wrapper source plus original payload classes. SDK
// bindings/publishers are explicit doubles; this is not a full SDK/native test.
internal static class Program
{
    private const ulong NativeResult = 0xFEDCBA9876543210UL;
    private const ulong ChangedResult = 0x123456789ABCDEF0UL;
#if NETFRAMEWORK
    private const string MemorySha = "d5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d";
#endif
    private enum Kind { LoadSave, LoadMap, Unload, StartCampaign, StartSkirmish, StartNonSkirmish }
    private sealed class Case
    {
        internal readonly string Name;
        internal readonly Action Body;
        internal Case(string name, Action body) { Name = name; Body = body; }
    }

    private static int Main(string[] arguments)
    {
        if (IntPtr.Size != 8) { Console.Error.WriteLine("x64 required; no skip credit."); return 2; }
#if NETFRAMEWORK
        if (arguments.Length != 2 || arguments[0] != "--system-memory-sha" || arguments[1] != MemorySha)
            throw new ArgumentException("Supply the admitted real System.Memory SHA for net481.");
        var spanProvider = typeof(ReadOnlySpan<byte>).Assembly;
        string loadedHash;
        using (var stream = File.OpenRead(spanProvider.Location))
        using (var hash = SHA256.Create())
            loadedHash = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (spanProvider.GetName().Name != "System.Memory" || loadedHash != MemorySha)
            throw new InvalidOperationException("Loaded Span declaration provider differs from the admitted DLL.");
        Console.WriteLine("SPAN_PROVIDER " + spanProvider.FullName + " | " + spanProvider.ManifestModule.ModuleVersionId + " | " + loadedHash);
#else
        if (arguments.Length != 0) throw new ArgumentException("net10 requires no provider argument.");
        Console.WriteLine("SPAN_PROVIDER " + typeof(ReadOnlySpan<byte>).Assembly.FullName + " | runtime declaration only");
#endif
        Console.WriteLine("RUNTIME " + Environment.Version + " | pointerBytes=" + IntPtr.Size);
        Console.WriteLine("SCOPE Actual linked wrapper/payload source; compile-control-flow SDK doubles; no constructor, scanner, hook, game or native Original executed.");
        var cases = Cases();
        var failed = new List<string>();
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error)
            {
                failed.Add(test.Name);
                string detail = error.GetType().Name + ": " + error.Message;
                Console.WriteLine("FAIL " + test.Name + " :: " + detail.Substring(0, Math.Min(1024, detail.Length)).Replace('\r', ' ').Replace('\n', ' '));
            }
        }
        string failedJson = failed.Count == 0 ? "[]" : "[\"" + string.Join("\",\"", failed) + "\"]";
        Console.WriteLine("SUMMARY {\"schema\":\"loader-lifecycle-source-cases/1\",\"totalGroups\":" + cases.Count +
            ",\"failedNames\":" + failedJson + ",\"rawExit\":" + (failed.Count == 0 ? 0 : 1) + "}");
        return failed.Count == 0 ? 0 : 1;
    }

    private static List<Case> Cases()
    {
        var cases = new List<Case>();
        foreach (Kind kind in Enum.GetValues(typeof(Kind)))
        {
            Kind selected = kind;
            cases.Add(new Case(kind + ".NormalForwardsRawArgumentsOnce", () => With(selected, Normal)));
            cases.Add(new Case(kind + ".PreFaultForwardsOnceWithoutPost", () => With(selected, PreFault)));
            cases.Add(new Case(kind + ".PostFaultKeepsFirstNativeResult", () => With(selected, PostFault)));
            cases.Add(new Case(kind + ".OriginalFaultDoesNotRetry", () => With(selected, NativeFault)));
            cases.Add(new Case(kind + ".SkipFlagRemainsIgnored", () => With(selected, SkipFlag)));
            cases.Add(new Case(kind + ".PostReturnContractIsPreserved", () => With(selected, PostReturn)));
            cases.Add(new Case(kind + ".LoggerFaultAfterPostDoesNotRetry", () => With(selected, LoggerFault)));
            if (IsStart(kind))
            {
                cases.Add(new Case(kind + ".UnloadSuppressionEndsBeforePost", () => With(selected, SuppressionWindow)));
                cases.Add(new Case(kind + ".NestedStartRestoresOuterSuppression", () => With(selected, NestedStart)));
            }
        }
        cases.Add(new Case("Save.NormalStillForwardsOnce", () => SaveControl(false)));
        cases.Add(new Case("Save.ContextFaultStillForwardsOnce", () => SaveControl(true)));
        return cases;
    }

    private static bool IsStart(Kind kind) => kind == Kind.StartCampaign || kind == Kind.StartSkirmish || kind == Kind.StartNonSkirmish;
    private static void With(Kind kind, Action<Harness> test)
    {
        ResetThroughSuccessfulStart();
        using (var harness = new Harness(kind))
        {
            try { test(harness); }
            finally { ResetThroughSuccessfulStart(); }
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal(ulong expected, ulong actual, string message)
    { Check(expected == actual, message + "; expected=0x" + expected.ToString("X") + ", actual=0x" + actual.ToString("X")); }
    private static Exception? Catch(Action action)
    { try { action(); return null; } catch (Exception error) { return error; } }

    private static void Normal(Harness h)
    {
        Equal(NativeResult, h.Invoke(), "Native result changed");
        h.ExpectCalls(1);
        h.ExpectRawArguments();
        h.ExpectTrace("Pre", "Original", "Post");
    }
    private static void PreFault(Harness h)
    {
        var fault = new InvalidOperationException("pre marker");
        h.Observer = args => { if (args.Phase == EventHookPhase.Pre) { args.SkipOriginalFunction = true; SetReturn(args, ChangedResult); throw fault; } };
        Equal(NativeResult, h.Invoke(), "Pre fault did not use original raw call");
        h.ExpectCalls(1);
        h.ExpectRawArguments();
        h.ExpectTrace("Pre", "Original");
    }
    private static void PostFault(Harness h)
    {
        var fault = new InvalidOperationException("post marker");
        h.Observer = args => { if (args.Phase == EventHookPhase.Post) { SetReturn(args, ChangedResult); throw fault; } };
        Equal(NativeResult, h.Invoke(), "Post fault replaced the first native result");
        h.ExpectCalls(1);
        h.ExpectRawArguments();
        h.ExpectTrace("Pre", "Original", "Post");
    }
    private static void NativeFault(Harness h)
    {
        var fault = new InvalidOperationException("original marker");
        h.NativeBody = _ => throw fault;
        Exception? caught = Catch(() => { h.Invoke(); });
        Check(ReferenceEquals(fault, caught), "Original exception identity was not preserved");
        h.ExpectCalls(1);
        h.ExpectTrace("Pre", "Original");
        if (IsStart(h.Selected))
        {
            h.ProbeUnload();
            Check(h.UnloadNotifications == 2 && h.UnloadNativeCalls == 1,
                "Native failure leaked unload-notification suppression");
        }
    }
    private static void SkipFlag(Harness h)
    {
        h.Observer = args => { if (args.Phase == EventHookPhase.Pre) { args.SkipOriginalFunction = true; SetReturn(args, ChangedResult); } };
        Equal(NativeResult, h.Invoke(), "Existing ignored Skip/Pre return semantics changed");
        h.ExpectCalls(1);
        h.ExpectTrace("Pre", "Original", "Post");
    }
    private static void PostReturn(Harness h)
    {
        h.Observer = args => { if (args.Phase == EventHookPhase.Post) SetReturn(args, ChangedResult); };
        Equal(h.Selected == Kind.LoadSave ? NativeResult : ChangedResult, h.Invoke(), "Existing Post return asymmetry changed");
        h.ExpectCalls(1);
    }
    private static void LoggerFault(Harness h)
    {
        var post = new InvalidOperationException("post marker");
        var logger = new InvalidOperationException("logger marker");
        h.Observer = args => { if (args.Phase == EventHookPhase.Post) throw post; };
        LogHelper.ErrorSink = (error, text) => throw logger;
        Exception? caught = Catch(() => { h.Invoke(); });
        Check(ReferenceEquals(logger, caught), "Existing unguarded error-logger failure behavior changed");
        h.ExpectCalls(1);
        h.ExpectTrace("Pre", "Original", "Post");
    }
    private static void SuppressionWindow(Harness h)
    {
        h.NativeBody = _ => {
            h.ProbeUnload();
            Check(h.UnloadNotifications == 0, "Unload notified during native start");
            return NativeResult;
        };
        h.Observer = args => {
            if (args.Phase == EventHookPhase.Post)
            {
                h.ProbeUnload();
                Check(h.UnloadNotifications == 2, "Suppression still active during outer Post");
            }
        };
        Equal(NativeResult, h.Invoke(), "Native start result changed");
        h.ExpectCalls(1);
        Check(h.UnloadNativeCalls == 2, "Suppressed unload still must execute its Original once");
    }
    private static void NestedStart(Harness h)
    {
        int depth = 0;
        h.AllowNestedStartPayload = true;
        h.NativeBody = _ => {
            if (depth == 0)
            {
                depth++;
                Equal(NativeResult, BulkMapLoaderDetours.c_game_start_campaign_game_handler_hook_impl(99), "Nested result changed");
                depth--;
                h.ProbeUnload();
                Check(h.UnloadNotifications == 0, "Nested start cleared outer suppression");
            }
            else
            {
                h.ProbeUnload();
                Check(h.UnloadNotifications == 0, "Nested Original did not suppress unload");
            }
            return NativeResult;
        };
        Equal(NativeResult, h.Invoke(), "Outer result changed");
        h.ExpectCalls(2);
        h.ProbeUnload();
        Check(h.UnloadNotifications == 2 && h.UnloadNativeCalls == 3, "Outer completion did not restore notifications");
    }

    private static void ResetThroughSuccessfulStart()
    {
        // Public wrapper behavior resets the known old leaked flag before each
        // baseline case. No private field reflection/name or production seam.
        MapLoaderR3EventHooks.OnLoadSave.Observer = null;
        MapLoaderR3EventHooks.OnLoadMap.Observer = null;
        MapLoaderR3EventHooks.OnUnloadMap.Observer = null;
        MapLoaderR3EventHooks.OnStartMap.Observer = null;
        LogHelper.InformationSink = null;
        LogHelper.ErrorSink = null;
        SHCDESE.API.GameMapArchiveManagerAPI.SetContext = null;
        BulkMapLoaderDetours.c_game_start_campaign_game_handler.Original = _ => NativeResult;
        Equal(NativeResult, BulkMapLoaderDetours.c_game_start_campaign_game_handler_hook_impl(0), "Fixture reset start failed");
    }

    private static void SaveControl(bool contextFault)
    {
        ResetThroughSuccessfulStart();
        using (var strings = new OwnedStrings())
        {
            int calls = 0, contexts = 0;
            SHCDESE.API.GameMapArchiveManagerAPI.SetContext = (path, save) => {
                contexts++;
                Check(path == OwnedStrings.FileName && save, "Save file context arguments changed");
                if (contextFault) throw new InvalidOperationException("context marker");
            };
            BulkMapLoaderDetours.c_game_dll_savesavegame_hook.Original = (data, length, sx, sy, rx, ry, locked, temp, map) => {
                calls++;
                Check(data == strings.File && length == strings.FileBytes && sx == 2 && sy == 3 && rx == 4 && ry == 5 && locked == 1 && temp == 1 && map == 0,
                    "Save raw arguments changed");
                return unchecked((long)NativeResult);
            };
            try
            {
                Equal(NativeResult, unchecked((ulong)BulkMapLoaderDetours.c_game_dll_savesavegame_hook_impl(strings.File, strings.FileBytes, 2, 3, 4, 5, 1, 1, 0)), "Save result changed");
                Check(calls == 1 && contexts == 1, "Save control forwarded more than once");
            }
            finally { ResetThroughSuccessfulStart(); }
        }
    }

    private static void SetReturn(EventHookBase args, ulong value)
    {
        if (args is LoadSaveGameEventArgs loadSave) loadSave.ReturnValue = unchecked((long)value);
        else if (args is MapLoadEventArgs loadMap) loadMap.ReturnValue = value;
        else if (args is MapUnloadEventArgs unload) unload.ReturnValue = value;
        else if (args is MapStartEventArgs start) start.ReturnValue = value;
        else throw new InvalidOperationException("Unexpected event payload");
    }
    private static ulong GetReturn(EventHookBase args)
    {
        if (args is LoadSaveGameEventArgs loadSave) return unchecked((ulong)loadSave.ReturnValue);
        if (args is MapLoadEventArgs loadMap) return loadMap.ReturnValue;
        if (args is MapUnloadEventArgs unload) return unload.ReturnValue;
        if (args is MapStartEventArgs start) return start.ReturnValue;
        throw new InvalidOperationException("Unexpected event payload");
    }

    private sealed class OwnedStrings : IDisposable
    {
        internal const string FileName = "loader-fixture.sav", MapName = "loader-fixture.map";
        internal readonly IntPtr File = Marshal.StringToHGlobalUni(FileName);
        internal readonly IntPtr Map = Marshal.StringToHGlobalUni(MapName);
        internal int FileBytes => (FileName.Length + 1) * 2;
        internal int MapBytes => (MapName.Length + 1) * 2;
        public void Dispose() { Marshal.FreeHGlobal(File); Marshal.FreeHGlobal(Map); }
    }
    private sealed class NativeCall
    {
        internal readonly Kind Kind;
        internal readonly object[] Arguments;
        internal NativeCall(Kind kind, object[] arguments) { Kind = kind; Arguments = arguments; }
    }
    private sealed class Harness : IDisposable
    {
        internal readonly Kind Selected;
        private readonly OwnedStrings strings = new OwnedStrings();
        private readonly List<NativeCall> calls = new List<NativeCall>();
        private readonly List<string> trace = new List<string>();
        private EventHookBase? pre;
        internal Action<EventHookBase>? Observer;
        internal Func<Kind, ulong>? NativeBody;
        internal bool AllowNestedStartPayload = false;
        internal int UnloadNotifications = 0, UnloadNativeCalls = 0;
        private static readonly IntPtr RetData = new IntPtr(0x2222);
        private const ulong Unknown = 0x1234567890UL;

        internal Harness(Kind selected)
        {
            Selected = selected;
            MapLoaderR3EventHooks.OnLoadSave.Observer = OnEvent;
            MapLoaderR3EventHooks.OnLoadMap.Observer = OnEvent;
            MapLoaderR3EventHooks.OnStartMap.Observer = OnEvent;
            MapLoaderR3EventHooks.OnUnloadMap.Observer = args => {
                if (Selected == Kind.Unload) OnEvent(args);
                else UnloadNotifications++;
            };
            BulkMapLoaderDetours.c_game_dll_loadsavegame_hook.Original = (file, length, ret, editor) =>
                unchecked((long)Original(Kind.LoadSave, file, length, ret, editor));
            BulkMapLoaderDetours.c_game_dll_loadmaptoplay_hook.Original = (campaign, file, length, ret, dummy, map, mapLength, multi, trail, trailId, bedouin) =>
                Original(Kind.LoadMap, campaign, file, length, ret, dummy, map, mapLength, multi, trail, trailId, bedouin);
            BulkMapLoaderDetours.c_game_unload_map_hook.Original = tile => {
                if (Selected == Kind.Unload) return Original(Kind.Unload, tile);
                UnloadNativeCalls++; return NativeResult;
            };
            BulkMapLoaderDetours.c_game_start_campaign_game_handler.Original = campaign => Original(Kind.StartCampaign, campaign);
            BulkMapLoaderDetours.c_game_start_skirmish_game_handler.Original = (a1, multi, a3) => Original(Kind.StartSkirmish, a1, multi, a3);
            BulkMapLoaderDetours.c_game_start_non_skirmish_game_handler.Original = (a1, a2) => Original(Kind.StartNonSkirmish, a1, a2);
        }
        internal ulong Invoke()
        {
            switch (Selected)
            {
                case Kind.LoadSave: return unchecked((ulong)BulkMapLoaderDetours.c_game_dll_loadsavegame_hook_impl(strings.File, strings.FileBytes, RetData, 1));
                case Kind.LoadMap: return BulkMapLoaderDetours.c_game_dll_loadmaptoplay_hook_impl(7, strings.File, strings.FileBytes, RetData, 3, strings.Map, strings.MapBytes, 1, 9, 11, 1);
                case Kind.Unload: return BulkMapLoaderDetours.c_game_unload_map_hook_impl(new IntPtr(0x3333));
                case Kind.StartCampaign: return BulkMapLoaderDetours.c_game_start_campaign_game_handler_hook_impl(7);
                case Kind.StartSkirmish: return BulkMapLoaderDetours.c_game_start_skirmish_game_handler_hook_impl(strings.File, 1, Unknown);
                case Kind.StartNonSkirmish: return BulkMapLoaderDetours.c_game_start_non_skirmish_game_handler_hook_impl(strings.File, Unknown);
                default: throw new InvalidOperationException("Unknown wrapper");
            }
        }
        private ulong Original(Kind kind, params object[] arguments)
        {
            calls.Add(new NativeCall(kind, arguments));
            trace.Add("Original");
            return NativeBody == null ? NativeResult : NativeBody(kind);
        }
        private void OnEvent(EventHookBase args)
        {
            trace.Add(args.Phase.ToString());
            if (!AllowNestedStartPayload) CheckPayload(args);
            if (args.Phase == EventHookPhase.Pre) pre = args;
            else
            {
                Check(!ReferenceEquals(pre, args), "Pre/Post payload object was reused");
                Equal(NativeResult, GetReturn(args), "Post was not seeded with the original native result");
                Check(!args.SkipOriginalFunction, "Pre Skip leaked into fresh Post");
            }
            Observer?.Invoke(args);
        }
        private void CheckPayload(EventHookBase args)
        {
            if (args is LoadSaveGameEventArgs save)
                Check(save.FileName == OwnedStrings.FileName && save.RetData == RetData && save.LoadingEditorMap, "LoadSave payload input changed");
            else if (args is MapLoadEventArgs map)
                Check(map.CampaignMapID == 7 && map.FileName == OwnedStrings.FileName && map.RetData == RetData && map.MapName == OwnedStrings.MapName &&
                    map.bMultiplayerSave == 1 && map.TrailType == 9 && map.TrailID == 11 && map.AllowClassicBedouins == 1, "LoadMap payload input changed");
            else if (args is MapUnloadEventArgs unload)
                Check(unload.TileManager == new IntPtr(0x3333), "Unload payload input changed");
            else if (args is MapStartEventArgs start)
                Check(start.CampaignMapId == (Selected == Kind.StartCampaign ? 7 : 0) && start.Unknown1 == (Selected == Kind.StartCampaign ? IntPtr.Zero : strings.File) &&
                    start.bMultiplayerSave == (Selected == Kind.StartSkirmish ? 1 : 0) && start.Unknown3 == (Selected == Kind.StartCampaign ? 0UL : Unknown), "Start payload input changed");
            else throw new InvalidOperationException("Unexpected payload type");
        }
        internal void ProbeUnload() => Equal(NativeResult, BulkMapLoaderDetours.c_game_unload_map_hook_impl(new IntPtr(0x7777)), "Unload probe changed native result");
        internal void ExpectCalls(int count) => Check(calls.Count == count, "Expected " + count + " native delegate calls; observed " + calls.Count);
        internal void ExpectTrace(params string[] expected) => Check(string.Join("|", trace) == string.Join("|", expected), "Observer/native ordering changed: " + string.Join("|", trace));
        internal void ExpectRawArguments()
        {
            Check(calls.Count == 1 && calls[0].Kind == Selected, "Wrong original wrapper was called");
            object[] expected;
            switch (Selected)
            {
                case Kind.LoadSave: expected = new object[] { strings.File, strings.FileBytes, RetData, (byte)1 }; break;
                case Kind.LoadMap: expected = new object[] { 7U, strings.File, strings.FileBytes, RetData, 3, strings.Map, strings.MapBytes, (byte)1, 9, 11, (byte)1 }; break;
                case Kind.Unload: expected = new object[] { new IntPtr(0x3333) }; break;
                case Kind.StartCampaign: expected = new object[] { 7 }; break;
                case Kind.StartSkirmish: expected = new object[] { strings.File, (byte)1, Unknown }; break;
                case Kind.StartNonSkirmish: expected = new object[] { strings.File, Unknown }; break;
                default: throw new InvalidOperationException("Unknown wrapper");
            }
            Check(expected.Length == calls[0].Arguments.Length, "Native argument count changed");
            for (int i = 0; i < expected.Length; i++)
                Check(Equals(expected[i], calls[0].Arguments[i]), "Native raw argument " + i + " changed");
        }
        public void Dispose() => strings.Dispose();
    }
}
