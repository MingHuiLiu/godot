using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot.Bridge;
using Godot.NativeInterop;

namespace Godot;

public static unsafe class GodotSharpHost
{
    public const int BindingsVersion = 1;

    private static bool _dllImportResolverConfigured;

    public static int ManagedCallbacksSize => sizeof(ManagedCallbacks);

    public static ManagedCallbacks Initialize(
        IntPtr godotDllHandle,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize,
        bool editorHint = false
    )
    {
        ConfigureDllImportResolver(godotDllHandle);
        AlcReloadCfg.Configure(editorHint);
        NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
        return ManagedCallbacks.Create();
    }

    public static GodotObject? GetOrCreateManagedObject(IntPtr unmanaged)
        => InteropUtils.UnmanagedGetManaged(unmanaged);

    private static void ConfigureDllImportResolver(IntPtr godotDllHandle)
    {
        if (_dllImportResolverConfigured)
            return;

        Assembly coreApiAssembly = typeof(GodotObject).Assembly;
        NativeLibrary.SetDllImportResolver(
            coreApiAssembly,
            new GodotDllImportResolver(godotDllHandle).OnResolveDllImport
        );

        _dllImportResolverConfigured = true;
    }
}
