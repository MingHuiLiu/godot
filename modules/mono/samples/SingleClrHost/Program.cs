using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: SingleClrHost <path-to-libgodot> [godot-args...]");
    return 2;
}

unsafe
{
    string libraryPath = args[0];
    string[] godotArgs = args.Length > 1 ? args[1..] : ["--headless", "--quit-after", "5"];

    IntPtr godotLibrary = NativeLibrary.Load(libraryPath);
    var setSingleClrEnabled = GetExport<godotsharp_host_set_single_clr_enabled_fn>(godotLibrary, "godotsharp_host_set_single_clr_enabled");
    var getBindings = GetExport<godotsharp_host_get_bindings_fn>(godotLibrary, "godotsharp_host_get_bindings");
    var initializeHost = GetExport<godotsharp_host_initialize_fn>(godotLibrary, "godotsharp_host_initialize");
    var createInstance = GetExport<libgodot_create_godot_instance_fn>(godotLibrary, "libgodot_create_godot_instance");
    var destroyInstance = GetExport<libgodot_destroy_godot_instance_fn>(godotLibrary, "libgodot_destroy_godot_instance");

    Check(setSingleClrEnabled(1), "enable host-driven single CLR mode");

    using NativeArgv argv = new([libraryPath, .. godotArgs]);
    IntPtr godotInstance = createInstance(argv.Count, argv.Values, &ExtensionInitialize);
    if (godotInstance == IntPtr.Zero)
        throw new InvalidOperationException("libgodot_create_godot_instance failed.");

    try
    {
        GodotSharpHostBindings bindings = default;
        Check(getBindings(GodotSharpHost.BindingsVersion, &bindings), "get GodotSharp bindings");
        if (bindings.Version != GodotSharpHost.BindingsVersion)
            throw new InvalidOperationException("GodotSharp host binding version mismatch.");
        if (bindings.Size != (uint)sizeof(GodotSharpHostBindings))
            throw new InvalidOperationException("GodotSharp host binding structure size mismatch.");
        if (bindings.ManagedCallbacksSize != GodotSharpHost.ManagedCallbacksSize)
            throw new InvalidOperationException("Managed callback size mismatch.");

        ManagedCallbacks managedCallbacks = GodotSharpHost.Initialize(
            godotLibrary,
            bindings.UnmanagedCallbacks,
            bindings.UnmanagedCallbacksSize
        );
        Check(initializeHost(&managedCallbacks, sizeof(ManagedCallbacks)), "initialize GodotSharp from host CLR");

        Console.WriteLine($"Godot native version: {Marshal.PtrToStringAnsi(bindings.GodotVersion)}");
        Console.WriteLine($"Godot managed version: {Engine.GetVersionInfo()["string"]}");

        GodotObject godotInstanceObject = GodotSharpHost.GetOrCreateManagedObject(godotInstance)
            ?? throw new InvalidOperationException("Unable to wrap GodotInstance.");
        godotInstanceObject.Call("start");

        using Node parent = new();
        using Node child = new();
        parent.Name = "SingleClrHostParent";
        child.Name = "SingleClrHostChild";
        parent.AddChild(child);
        Console.WriteLine($"Created Godot nodes in host CLR: {parent.Name}/{parent.GetChild(0).Name}");

        for (int i = 0; i < 5; i++)
            godotInstanceObject.Call("iteration");

        godotInstanceObject.Call("stop");
    }
    finally
    {
        destroyInstance(godotInstance);
        NativeLibrary.Free(godotLibrary);
    }
}

static T GetExport<T>(IntPtr library, string name) where T : Delegate
    => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

static void Check(int error, string operation)
{
    if (error != 0)
        throw new InvalidOperationException($"Failed to {operation}: {(GodotSharpHostInteropError)error} ({error}).");
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
static unsafe byte ExtensionInitialize(IntPtr getProcAddress, IntPtr library, GodotExtensionInitialization* initialization)
{
    initialization->MinimumInitializationLevel = 2;
    initialization->UserData = IntPtr.Zero;
    initialization->Initialize = IntPtr.Zero;
    initialization->Deinitialize = IntPtr.Zero;
    return 1;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct GodotSharpHostBindings
{
    public uint Version;
    public uint Size;
    public IntPtr UnmanagedCallbacks;
    public int UnmanagedCallbacksSize;
    public int ManagedCallbacksSize;
    public IntPtr GodotVersion;
    public IntPtr GodotVersionHash;
    public ulong ApiCoreHash;
    public ulong ApiEditorHash;
}

enum GodotSharpHostInteropError
{
    Ok = 0,
    InvalidArgument = 1,
    VersionMismatch = 2,
    NotReady = 3,
    AlreadyInitialized = 4,
    InitializationFailed = 5,
}

[StructLayout(LayoutKind.Sequential)]
struct GodotExtensionInitialization
{
    public int MinimumInitializationLevel;
    public IntPtr UserData;
    public IntPtr Initialize;
    public IntPtr Deinitialize;
}

unsafe sealed class NativeArgv : IDisposable
{
    private readonly IntPtr[] _strings;
    private readonly GCHandle _valuesHandle;

    public NativeArgv(string[] args)
    {
        _strings = new IntPtr[args.Length];
        for (int i = 0; i < args.Length; i++)
            _strings[i] = Marshal.StringToHGlobalAnsi(args[i]);

        _valuesHandle = GCHandle.Alloc(_strings, GCHandleType.Pinned);
    }

    public int Count => _strings.Length;
    public char** Values => (char**)_valuesHandle.AddrOfPinnedObject();

    public void Dispose()
    {
        _valuesHandle.Free();
        foreach (IntPtr value in _strings)
            Marshal.FreeHGlobal(value);
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
unsafe delegate int godotsharp_host_set_single_clr_enabled_fn(int enabled);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
unsafe delegate int godotsharp_host_get_bindings_fn(uint version, GodotSharpHostBindings* bindings);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
unsafe delegate int godotsharp_host_initialize_fn(void* managedCallbacks, int managedCallbacksSize);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
unsafe delegate IntPtr libgodot_create_godot_instance_fn(int argc, char** argv, delegate* unmanaged[Cdecl]<IntPtr, IntPtr, GodotExtensionInitialization*, byte> initFunc);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void libgodot_destroy_godot_instance_fn(IntPtr instance);
