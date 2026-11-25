# Game/Bootstrap

Source folder: `Assets/Scripts/Game/Bootstrap`. Files: `GameClient.cs`, `GameStarted.cs`, `IGameStarted.cs`. The namespace is `Game.Bootstrap`.

Responsibilities: [Game starter](../game-starter.md), [Messaging](../messaging.md#hooks), [Bootstrap and lifecycle](../bootstrap-and-lifecycle.md).

## Purpose

This folder holds the class that starts the game and the hooks that this class broadcasts.

## Types

### GameClient

`Game.Bootstrap.GameClient` is a subclass of `Core.Infra.Bootstrap.Client`. It is a `MonoBehaviour`. The GameObject `GameClient` in `ClientScene` has this component. Refer to [Infra/Bootstrap](../infra/bootstrap.md).

| Method | Behavior now |
|--------|--------------|
| `CreateModules` | Calls the base method. Has no modules. Add the modules of the game here. Binds `GameStarted` to `IGameStarted` with `BindAsync`. |
| `StartGame` | Calls the base method. Writes "Game started" to the log. Broadcasts `GameStarted` and waits for the handlers. Start the game here. |

A method that you add to these calls must call the base method first.

## Hooks

A hook has an event class and an interface. A module implements the interface to receive the hook. Refer to [Infra/Module](../infra/module.md#hooks) for the mechanism.

A hook lives in the same folder and has the same namespace as its invoker. The invoker is the code that broadcasts the event. The invoker, the event and the interface are then in one place.

| Event | Interface | Invoker | When it fires |
|-------|-----------|---------|---------------|
| `GameStarted` (`AsyncEvent`) | `IGameStarted` with `UniTask OnGameStarted()` | `GameClient` | `GameClient.StartGame` broadcasts it with `BroadcastAsync`, after the modules start. It waits for each handler. |

`GameClient.CreateModules` connects the event to the interface:

```csharp
Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
```

### How to receive a hook

1. Implement the interface in the module. For example `InventoryModule : BaseModule, IInventoryModule, IGameStarted`.
2. Write the method of the interface. The method runs after all modules in the scope have started. `StartGame` waits until the method ends, before it calls the next module.

### How to add a hook

Use the skill [`create-hook`](../../../.claude/skills/create-hook/SKILL.md). It does these steps.

1. Find the code that broadcasts the event. This is the invoker.
2. Make an event class in the folder of the invoker. Use `Event` for a synchronous hook. Use `AsyncEvent` for an asynchronous hook. Use the namespace of the invoker.
3. Make an interface with one method in the same folder. An asynchronous method returns `UniTask`.
4. Add a `Modules.Bind` or `Modules.BindAsync` call in `GameClient.CreateModules`.
5. Broadcast the event with `Broadcast` or `BroadcastAsync` on the client bus, at the moment that the hook marks.
6. Add the hook to the table on this page. If the invoker is in a module folder, add it to the folder page of that module.

## Notes

- A hook without a `Bind` call reaches no module. The code does not report it.
- A module that is added after the event does not receive that event. The scope does not replay events.

## Session startup

When the session scene is configured, `GameClient` creates the game-wide services and adds `SessionModule`. The module handles `IGameStarted` for an optional startup join or reconnect. It awaits that operation, so later hooks start after it finishes. The module opens the loading window before the command and creates the game view after scene registration and local player readiness.

`Client.OnDestroy` releases the module scope before it calls `DisposeServices`.
