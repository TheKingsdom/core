# Game/Session

Source folder `Assets/Scripts/Game/Session`. Files include `SessionModule.cs`, `ISessionModule.cs`, `ISessionModuleInternal.cs`, `SessionRecord.cs`, `SessionRecordAsset.cs`, and `GameSessionView.cs`.

`SessionModule` owns the room operation, the game view instance, and the local input binding. Other modules issue room commands through `ISessionModule`. The session module asks `IWindowsModule` to open and close its loading and pause windows.

`SessionRecord` supplies the scene path, game view asset ID, and startup settings. The module passes the loaded scene, ready local avatar, and current network snapshot to the view. The view uses `ISessionModuleInternal` to issue view commands.

The module implements `IGameStarted` for configured startup join or resume. It implements `INetworkSnapshotChanged` for network changes and `IWindowChanged` for input gating. The module broadcasts later network snapshots to its own views.

See [coop sessions](../coop-sessions.md) and [record setup](../../reuse-coop.md).
