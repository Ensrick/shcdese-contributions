// Compile/control-flow doubles, NOT SDK or native-hook implementations. Tests
// call only the actual static wrapper methods. Every installation/scanner/archive
// path throws so accidental construction cannot become silent qualification.
using System;
using SHCDESE.EventAPI.MapLoader;

namespace RedBird.Abstractions.Hooks
{
    internal readonly struct HookTarget
    {
        public static HookTarget FromExport(string name, IntPtr module) => throw FixtureOnly.Unexpected();
        public static implicit operator HookTarget(string pattern) => throw FixtureOnly.Unexpected();
    }
}
namespace RedBird.Abstractions.Hooks.Transaction
{
    internal sealed class HookHandle<T> { }
    internal sealed class DetourHandle<T> where T : Delegate
    {
        public T Original { get; set; } = null!;
    }
}
namespace RedBird.Core.Memory { internal sealed class ScanRegion { } }
namespace RedBird.X64.Hooks { internal sealed class X64InlineHook { } }
namespace RedBird.X64.Assembly
{
    [Flags] internal enum X64SmartCPUContextRegs { Volatile = 1, RDI = 2 }
}
namespace RedBird.X64.Hooks.Context
{
    internal sealed class ContextHookOptions
    {
        public RedBird.X64.Assembly.X64SmartCPUContextRegs Registers { get; set; }
    }
    internal struct FixtureCpu
    {
        public ulong RDI;
        internal FixtureCpu(ulong value) { RDI = value; }
    }
    internal unsafe readonly struct FixtureContext
    {
        public FixtureCpu* Pointer => throw FixtureOnly.Unexpected();
    }
    internal unsafe delegate void FixtureContextCallback(FixtureContext context);
}
namespace RedBird.X64.Hooks.Transaction
{
    internal sealed class HookTransaction
    {
        public void AddDetour<T>(RedBird.Abstractions.Hooks.Transaction.DetourHandle<T> handle,
            RedBird.Abstractions.Hooks.HookTarget target, T callback) where T : Delegate => throw FixtureOnly.Unexpected();
        public void AddContextHook(RedBird.Abstractions.Hooks.Transaction.HookHandle<RedBird.X64.Hooks.X64InlineHook> handle,
            RedBird.Abstractions.Hooks.HookTarget target, RedBird.X64.Hooks.Context.FixtureContextCallback callback,
            RedBird.X64.Hooks.Context.ContextHookOptions options) => throw FixtureOnly.Unexpected();
    }
}
namespace RedBird.X64.Memory.Scanners
{
    internal sealed class DataScanner
    {
        public ScanResult Scan(string pattern) => throw FixtureOnly.Unexpected();
    }
    internal readonly struct ScanResult
    {
        public bool TryReadFlowControlTarget(out ulong address) { address = 0; throw FixtureOnly.Unexpected(); }
    }
}
namespace SHCDESE.API.LowLevel { internal static class FixtureNamespace { } }
namespace SHCDESE.BepInEx.Bootstrap
{
    internal sealed class CrusaderLibrary
    {
        public static CrusaderLibrary Instance => throw FixtureOnly.Unexpected();
        public IntPtr LibraryModuleHandle => throw FixtureOnly.Unexpected();
    }
}
namespace SHCDESE.API
{
    internal sealed class GameMapArchiveManagerAPI
    {
        private static readonly GameMapArchiveManagerAPI instance = new GameMapArchiveManagerAPI();
        public static GameMapArchiveManagerAPI Instance => instance;
        public static Action<string, bool>? SetContext;
        public byte[]? PrepareMapArchiveAndGetBytes(bool map) => throw FixtureOnly.Unexpected();
        public object? GetMapArchive() => throw FixtureOnly.Unexpected();
        public static byte[] CreateEmptyZip() => throw FixtureOnly.Unexpected();
        public void SetCurrentFileContext(string path, bool save) => SetContext?.Invoke(path, save);
    }
}
namespace SHCDESE.Logging
{
    internal static class LogHelper
    {
        public static Action<string>? InformationSink;
        public static Action<Exception, string>? ErrorSink;
        public static void Information(string text) => InformationSink?.Invoke(text);
        public static void Error(Exception error, string text) => ErrorSink?.Invoke(error, text);
        public static void Error(string text) => ErrorSink?.Invoke(new InvalidOperationException(text), text);
    }
}
namespace SHCDESE.EventAPI
{
    internal sealed class FixturePublisher<T>
    {
        public Action<T>? Observer;
        public void Raise(T value) => Observer?.Invoke(value);
    }
    internal static class MapLoaderR3EventHooks
    {
        public static readonly FixturePublisher<LoadSaveGameEventArgs> OnLoadSave = new FixturePublisher<LoadSaveGameEventArgs>();
        public static readonly FixturePublisher<MapLoadEventArgs> OnLoadMap = new FixturePublisher<MapLoadEventArgs>();
        public static readonly FixturePublisher<MapUnloadEventArgs> OnUnloadMap = new FixturePublisher<MapUnloadEventArgs>();
        public static readonly FixturePublisher<MapStartEventArgs> OnStartMap = new FixturePublisher<MapStartEventArgs>();
    }
}
internal static class FixtureOnly
{
    internal static Exception Unexpected() => new InvalidOperationException("Unexpected SDK construction/scanning/archive path; only actual static wrapper control flow is admitted.");
}
