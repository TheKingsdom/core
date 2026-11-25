# Reuse cooperative sessions

## Dependencies

| Assembly | Project dependencies | Package dependencies |
|----------|----------------------|----------------------|
| `Core` | None | UniTask, Addressables |
| `Core.Resources` | None | None |
| `Core.Lifetime` | Resources | UniTask |
| `Core.Networking.Core` | None | None |
| `Core.Networking.Fusion` | Core, Lifetime, Resources, Network policies | Fusion, Photon Realtime, UniTask |
| `Core.Networking.Players` | Core, Lifetime, Resources, Fusion, Network policies | Fusion, UniTask |
| `Core.Input` | Core | Input System |
| `Core.UI` | Core, Input, Lifetime, Resources | uGUI, TextMeshPro, Input System, UniTask |
| `Game` | Core and the coop assemblies | Fusion, uGUI, Input System, UniTask |

Copy the necessary folders with their `.meta` files. Add the package references in the target project. Enable the Input System for the player. Check that the Fusion config weaves `Core.Networking.Fusion` and `Core.Networking.Players`.

## Prepare the scenes

1. Add a bootstrap scene with `Game.Bootstrap.GameClient` and a session scene to the enabled build scenes.
2. Add a scene `NetworkObject` and `PlayerSessionDirector` to the session scene. Set the Master Client Object flag.
3. Assign a baked prefab with `NetworkObject` and `PlayerAvatar` to the director. Use the Master Client Object flag for an avatar that the authority must keep on a peer departure.
4. Rebuild the Fusion prefab table. The test assets in `Assets/Tests/Integration/Fixtures` show a scene and avatar with this setup.
5. Assign the scene asset path, the view anchor, a `LoadingWindow` prefab, and a `GameSessionView` prefab in `GameClient`. Add a canvas and a raycaster to interactive windows. Assign a `PauseWindow` and the EventSystem input module if the game uses them.
6. Set the Fusion App ID in `PhotonAppSettings` for online rooms. Single sessions run locally.

## Start and stop a session

Call `SessionModule.CreateAsync`, `JoinAsync`, `JoinRandomAsync`, or `ReconnectAsync` from the game. Use the `OperationResult` to show errors. Call `LeaveAsync` when the player exits the room.

Set a startup room and optional region to join at `IGameStarted`. Enable resume on startup to use a saved locator when there is no startup room. Leave the session scene path empty for a client without the session module.

Derive the game view from `GameSessionView` and read `Data` in `ManagedStart`. The module gives it the loaded scene, ready local avatar, and current connection snapshot. Subscribe to `NetworkSnapshot` on the module broadcaster for later roster updates. Unsubscribe in `ManagedDispose`.

Read [Coop sessions](architecture/coop-sessions.md), [Windows](architecture/ui/windows.md), and [Testing](testing.md).
