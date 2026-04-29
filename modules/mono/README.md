# How to build and run

1. Build Godot with the module enabled: `module_mono_enabled=yes`.
2. After building Godot, use it to generate the C# glue code:
   ```sh
   <godot_binary> --generate-mono-glue ./modules/mono/glue
   ```
3. Build the C# solutions:
   ```sh
   ./modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin
   ```

The paths specified in these examples assume the command is being run from
the Godot source root.

# How to deal with NuGet packages

We distribute the API assemblies, our source generators, and our custom
MSBuild project SDK as NuGet packages. This is all transparent to the user,
but it can make things complicated during development.

In order to use Godot with a development of those packages, we must create
a local NuGet source where MSBuild can find them. This can be done with
the .NET CLI:

```sh
dotnet nuget add source ~/MyLocalNugetSource --name MyLocalNugetSource
```

The Godot NuGet packages must be added to that local source. Additionally,
we must make sure there are no other versions of the package in the NuGet
cache, as MSBuild may pick one of those instead.

In order to simplify this process, the `build_assemblies.py` script provides
the following `--push-nupkgs-local` option:

```sh
./modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin \
    --push-nupkgs-local ~/MyLocalNugetSource
```

This option ensures the packages will be added to the specified local NuGet
source and that conflicting versions of the package are removed from the
NuGet cache. It's recommended to always use this option when building the
C# solutions during development to avoid mistakes.

# Double Precision Support (REAL_T_IS_DOUBLE)

Follow the above instructions but build Godot with the precision=double argument to scons

When building the NuGet packages, specify `--precision=double` - for example:
```sh
./modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin \
    --push-nupkgs-local ~/MyLocalNugetSource --precision=double
```

# Host-driven single CLR mode for LibGodot

LibGodot embedders that already run inside .NET can opt into a host-driven mode where Godot does not start `hostfxr`, CoreCLR, or load `GodotPlugins` itself. The host must enable the mode before creating the Godot instance, initialize GodotSharp in the existing CLR, and inject the managed callback table back into native code.

Initialization order:

1. Load the LibGodot dynamic library with the host's .NET runtime.
2. Call `godotsharp_host_set_single_clr_enabled(1)`.
3. Create the Godot instance through the LibGodot API.
4. Call `godotsharp_host_get_bindings(GodotSharpHost.BindingsVersion, ...)` and validate the returned structure size/version fields.
5. Call `Godot.GodotSharpHost.Initialize(...)` with the unmanaged callback table returned by native code.
6. Pass the returned `Godot.Bridge.ManagedCallbacks` table to `godotsharp_host_initialize(...)`.
7. Start the Godot instance, use `Godot.*` APIs from the host CLR, and drive frames from one thread using the `GodotInstance` object.

Exported native entry points:

- `godotsharp_host_set_single_clr_enabled(int enabled)`: must be called before the Mono module initializes to suppress engine-owned CLR startup.
- `godotsharp_host_get_bindings(uint32_t version, GodotSharpHostBindings *bindings)`: returns the GodotSharp unmanaged callback table, callback sizes, Godot version string, version hash, and API hashes when available.
- `godotsharp_host_initialize(const void *managed_callbacks, int32_t managed_callbacks_size)`: injects the host-created managed callbacks and marks GodotSharp as initialized.
- `godotsharp_host_is_single_clr_enabled()`: diagnostic helper.

The unmanaged callback table is owned by Godot and remains valid for the lifetime of the loaded engine. The managed callback table is copied by native code during `godotsharp_host_initialize`. Mismatched callback sizes or binding versions return an explicit non-zero error code instead of continuing with an unsafe layout.

The sample host in `modules/mono/samples/SingleClrHost` demonstrates the minimal sequence:

```sh
dotnet run --project modules/mono/samples/SingleClrHost -- <path-to-libgodot> --headless --path <path-to-project> --quit-after 5
```

To validate a local checkout from generated sources:

1. Install the native build dependencies for the target platform and make sure `scons` and a compatible .NET SDK are on `PATH`.
2. Build an editor binary with the Mono module enabled and use it to generate the Mono glue, for example:
   ```sh
   scons platform=linuxbsd target=editor module_mono_enabled=yes
   ./bin/godot.linuxbsd.editor.x86_64.mono --headless --editor --generate-mono-glue ./modules/mono/glue
   ```
3. Build the managed assemblies: `./modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin`.
4. Build a LibGodot shared library with the Mono module enabled and path overrides available for the sample, for example `scons platform=linuxbsd target=template_debug tools=no module_mono_enabled=yes library_type=shared_library disable_path_overrides=no`.
5. Build and run the sample host with the generated LibGodot path.

The sample validates the first-stage acceptance loop by enabling host-driven mode before Godot initialization, checking binding versions and callback structure sizes, initializing GodotSharp from the existing CLR, calling `Engine.GetVersionInfo()`, creating parent/child `Node` instances, and stepping several frames.

Threading constraints for this first-stage mode are intentionally strict: call Godot APIs and drive `GodotInstance.iteration()` from the same thread that owns the Godot main loop. UI hosts should either marshal work onto that Godot thread or drive Godot ticks from the UI thread, but should not call arbitrary Godot APIs concurrently.

Known limitations:

- The editor hot-reload path is not replicated in host-driven mode.
- The sample focuses on C# object creation and frame stepping; deep UI framework integration is left to the host.
- Script instance creation from native-owned C# scripts follows the existing GodotSharp callback path after initialization, but complex reload/unload scenarios need additional validation.
