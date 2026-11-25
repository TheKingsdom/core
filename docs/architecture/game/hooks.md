# Game/Hooks

Source folder: `Assets/Scripts/Game/Hooks`. Files: `GameStarted.cs`, `IGameStarted.cs`.

Responsibilities: [Messaging](../messaging.md#hooks), [Game starter](../game-starter.md).

## Purpose

This folder holds the hooks of the game. A hook has an event class and an interface. A module implements the interface to receive the hook. Refer to [Infra/Module](../infra/module.md#hooks) for the mechanism.

The namespace is `Game.Hooks`.

## Hooks

| Event | Interface | When it fires |
|-------|-----------|---------------|
| `GameStarted` (`AsyncEvent`) | `IGameStarted` with `UniTask OnGameStarted()` | `GameClient.StartGame` broadcasts it with `BroadcastAsync`, after the modules start. It waits for each handler. |

`GameClient.CreateModules` connects the event to the interface:

```csharp
Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
```

## How to receive a hook

1. Implement the interface in the module. For example `InventoryModule : BaseModule, IInventoryModule, IGameStarted`.
2. Write the method of the interface. The method runs after all modules in the scope have started. `StartGame` waits until the method ends, before it calls the next module.

## How to add a hook

1. Make an event class in this folder. Use `Event` for a synchronous hook. Use `AsyncEvent` for an asynchronous hook.
2. Make an interface with one method. An asynchronous method returns `UniTask`.
3. Add a `Modules.Bind` or `Modules.BindAsync` call in `GameClient.CreateModules`.
4. Broadcast the event with `Broadcast` or `BroadcastAsync` on the client bus, at the moment that the hook marks.
5. Add the hook to the table on this page.

## Notes

- A hook without a `Bind` call reaches no module. The code does not report it.
- A module that is added after the event does not receive that event. The scope does not replay events.
