# Reuse cooperative sessions

## Dependencies

| Assembly | Project dependencies | Package dependencies |
|----------|----------------------|----------------------|
| `Core` | None | UniTask, Addressables |
| `Core.Lifetime` | None | None |
| `Core.Networking.Core` | None | None |
| `Core.Networking.Fusion` | Core, Network policies | Fusion, Photon Realtime, UniTask |
| `Core.Networking.Players` | Core, Fusion, Network policies | Fusion, UniTask |
| `Core.Input` | Core | Input System |
| `Core.UI` | Core, Input, Lifetime | uGUI, TextMeshPro, Input System, UniTask |
| `Game` | Core and the coop assemblies | Fusion, uGUI, TextMeshPro, Input System, UniTask |

Copy the necessary folders with their `.meta` files. Add the package references in the target project. Enable the Input System for the player. Check that the Fusion config weaves `Core.Networking.Fusion` and `Core.Networking.Players`.

## Configure the game

1. Add a bootstrap scene with `Game.Bootstrap.GameClient` and a session scene to the enabled build scenes.
2. Add a scene `NetworkObject` and `PlayerSessionDirector` to the session scene. Set the Master Client Object flag.
3. Assign a baked prefab with `NetworkObject` and `PlayerAvatar` to the director. Use the Master Client Object flag for an avatar that the authority must keep on a peer departure. Rebuild the Fusion prefab table.
4. Create the assets with `Assets > Create > Core > Coop > Session Settings` and `Windows Settings`. Add both assets to `Resources/RecordsStorage`.
5. Set the scene asset path and game view asset ID in `SessionRecord`. Set the loading window asset ID and the optional pause window asset ID in `WindowsRecord`. With the default asset service each ID is a Resources path without a file extension.
6. Add a canvas and a raycaster to each interactive window prefab. Assign the cancel button in `LoadingWindow`. A pause prefab can call `Cancel`, `Leave`, and `Disconnect` on `PauseWindow`.
7. Set the Fusion App ID in `PhotonAppSettings` for online rooms. Single sessions run locally.

## Start and stop a session

Use `ISessionModule` to create, join, or reconnect. Use the `OperationResult` to show errors. Call `LeaveAsync` when the player exits the room.

Set a startup room and optional region in `SessionRecord` to join at `IGameStarted`. Enable resume on startup to use a saved locator when there is no startup room. Leave the session scene path empty for the default client startup.

Derive the game view from `GameSessionView` and read `Data` in `ManagedStart`. The module supplies the loaded scene, the ready local avatar, and the current connection snapshot. Use `ISessionModuleInternal` for view commands and the module broadcaster for later snapshots. Unsubscribe in `ManagedDispose`.

Read [Coop sessions](architecture/coop-sessions.md), [Game/Windows](architecture/game/windows.md), and [Testing](testing.md).
