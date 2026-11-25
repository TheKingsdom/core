# Cooperative sessions

## Ownership

| Scope | Owner and resources |
|-------|---------------------|
| Application | `GameClient` owns the network, lobby directory, reconnect store, local input, windows, and optional UI input binding. It starts the module scope after service initialization. |
| Module | `SessionModule` owns the current room command, the windows it opens, and the game view. It subscribes to state and window changes in `Start` and unsubscribes in `Dispose`. |
| Runner | `FusionRunnerSession` owns the runner GameObject, Fusion callbacks, scene registration, player context, and local input binding lease. |
| Opening | Each `WindowBase` owns its opening subscriptions and clears them on `Close`. |

## Entry and reconnect

1. `SessionModule` opens the loading window before it calls the network command. Cancel signals the command's token.
2. `NetworkService` starts a fresh runner in the requested mode. The previous runner must finish cleanup first.
3. `PlayerSessionCoordinator` waits for the scene registration and local avatar. It binds local input and waits for the avatar's ready state.
4. The network service publishes the `InRoom` snapshot. The module uses that snapshot, the loaded scene, and the local avatar to initialize the game view.
5. The module closes the loading window. Gameplay becomes available when there is a game view and the window stack is empty.

Reconnect keeps the saved room locator, shuts down the old runner if necessary, and follows the same readiness sequence. The game view belongs to the new runner generation.

## Failure and exit

A failed or cancelled network command retires the runner and waits on the shutdown barrier. The barrier releases the installed player context and the scene and input resources. The module closes the loading window on completion. A failure during view creation also releases the new view and leaves the room.

Leave or connection loss removes the game view. The service can keep a reconnect locator. Module disposal cancels its operation and releases its views. Client destruction disposes its services after the modules.

## Source folders

| Part | Page |
|------|------|
| Module and views | [Game/Session](game/session.md) |
| Operation gate and state rules | [Networking/Core](networking/core.md) |
| Fusion service and runner | [Networking/Fusion](networking/fusion.md) |
| Player context | [Networking/Players](networking/players.md) |
| Window lifecycle | [Windows](ui/windows.md) |
| Resource leases | [Resources](resources.md) |

See [setup](../reuse-coop.md) and [tests](../testing.md).
