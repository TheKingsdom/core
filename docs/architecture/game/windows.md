# Game/Windows

Source folder `Assets/Scripts/Game/Windows`. Files include `WindowsModule.cs`, `IWindowsModule.cs`, `IWindowsModuleInternal.cs`, `WindowsRecord.cs`, `WindowsRecordAsset.cs`, `LoadingWindow.cs`, `PauseWindow.cs`, `WindowChanged.cs`, and `IWindowChanged.cs`.

Other modules use `IWindowsModule` to open and close windows. The window module owns its UI root and loads window instances by ID through `IAssetService`. It opens each view with its typed `IWindowsModuleInternal` interface.

The module uses its inherited broadcaster for text input changes and provider failures. It unsubscribes in `Dispose`. It cancels active text input before sending cancel to a window.

## Hooks

| Event | Interface | Invoker | When |
|-------|-----------|---------|------|
| `WindowChanged` | `IWindowChanged.OnWindowChanged(bool)` | `WindowsModule` | The active window changes. The session module updates its input gate. |

`GameClient` binds the hook on the client broadcaster. The window module also implements `IGameStarted` to preload the loading view before the session hook.

See [window lifecycle](../ui/windows.md) and [record setup](../../reuse-coop.md).
