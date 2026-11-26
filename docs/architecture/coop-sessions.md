# Cooperative sessions

## Ownership

| Owner | Resources |
|-------|-----------|
| `GameClient` | Application services and the module scope. The client reads the module records from `RecordsStorage`. |
| `WindowsModule` | Window instances, window stack, UI root, input binding, and text input. |
| `SessionModule` | Current room command, game view instance, and local input binding. |
| `FusionRunnerSession` | Network runner, Fusion callbacks, and network scenes. The installer creates and releases the player context for that runner. |
| `WindowBase<TModule>` | One opening. The view installs opening-specific handlers in `OnOpen` and removes them in `OnClose`. |

## Entry and reconnect

1. `WindowsModule` preloads the loading window on `IGameStarted`. `SessionModule` asks `IWindowsModule` to open it before calling the network command. Cancel signals the command token.
2. `NetworkService` starts a fresh runner for the requested mode. It waits for the previous runner cleanup before it starts a replacement.
3. `PlayerSessionCoordinator` waits for scene registration, spawn, and local readiness. The session module supplies the input binding and release callbacks.
4. After the network service reports `InRoom`, the session module loads the game view by ID. It supplies the loaded scene, ready local avatar, and connection snapshot to that view.
5. The session module asks the window module to close the loading window. The `IWindowChanged` hook updates the gameplay input gate.

Reconnect keeps the saved locator, shuts down the old runner if necessary, and follows the same readiness sequence.

## Failure and exit

A failed or cancelled network command retires the runner and waits for shutdown. The installer releases the player context and the local input binding. The session module asks the window module to close the loading window. A failure during view creation releases the game view and leaves the room.

Each module releases its asset instances through `IAssetService`. `ModuleScope` disposes modules in reverse order before `GameClient` disposes the services.

## Source folders

| Part | Page |
|------|------|
| Room commands and game view | [Game/Session](game/session.md) |
| Window composition and hooks | [Game/Windows](game/windows.md) |
| Window stack and lifecycle | [Windows](ui/windows.md) |
| Operation gate and state | [Networking/Core](networking/core.md) |
| Fusion service and runner | [Networking/Fusion](networking/fusion.md) |
| Player context | [Networking/Players](networking/players.md) |

See [setup](../reuse-coop.md) and [tests](../testing.md).
