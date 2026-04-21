# Extension Demo

This sample shows the intended `pie-unity` extension path for UPM users:

- import a package sample from Package Manager
- treat the imported `Assets/Samples/...` files as staging content, not the final project location
- let the sample install a project extension into `Assets/Pie/Extensions/`
- export a module function, for example `module.exports = (api) => { ... }`
- call Unity/C# host capabilities through `api.callHost(name, args)`

## Included pieces

- `Editor/PieExtensionSampleInstaller.cs`
  Copies the demo extension file into `Assets/Pie/Extensions/` and ensures the Editor host handlers are registered.

- `Editor/PieExtensionSampleHost.cs`
  Registers sample host handlers via `PieHostBridge.Register(...)` for Editor testing.

- `Scripts/PieExtensionDemoRuntime.cs`
  Optional runtime-side host registration component for Play Mode or Player testing.

- `Extensions/demo-host-tools.js.txt`
  The project extension source that gets installed into `Assets/Pie/Extensions/demo-host-tools.js`.

## Suggested flow

1. Import this sample from Package Manager.
2. Wait for script compilation to finish.
3. Open `Tools > Pie > Pie Chat`.
4. Run `/extensions` or `/extensions reload`.
5. Ask Pie to use `read_runtime_flag` with a key like `demo_status`.
6. Ask Pie to use `echo_runtime_payload` with a short message.

## Notes

- Unity imports the sample into `Assets/Samples/...`, but the actual project extension is installed into `Assets/Pie/Extensions/...`.
- The installer copies the extension file only when it is missing. If you customize the installed file, later reloads will leave your edits alone.
- The installer also re-registers the Editor host handlers, so the sample can recover after domain reloads.
- The sample does not add a dedicated test menu item; setup happens through sample import plus the auto-installer script.
- For runtime testing, add `PieExtensionDemoRuntime` to a GameObject. It temporarily overrides the demo host handlers during play and restores the previous handlers on teardown.
