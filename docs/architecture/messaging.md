# Messaging

## Responsibility

This responsibility sends events between parts of the code that do not know each other. It gives a synchronous bus and an asynchronous bus.

## Parts

| Part | What it does | Folder page |
|------|--------------|-------------|
| `IBroadcaster`, `Broadcaster` | The event bus. It keys the subscribers by event type. | [Infra/Messaging](infra/messaging.md) |
| `Event`, `AsyncEvent` | The base classes of the events. | [Infra/Messaging](infra/messaging.md) |
| `Client.Broadcaster` | The client bus. | [Infra/Bootstrap](infra/bootstrap.md) |
| `BaseModule.Broadcaster` | The bus of one module. Only for the module and its views. | [Infra/Module](infra/module.md) |
| `ModuleScope.Bind`, `ModuleScope.BindAsync` | Connect an event to an interface. Each module that implements the interface receives the event as a hook. | [Infra/Module](infra/module.md#hooks) |
| Hooks of the game | The events and interfaces of the game, for example `GameStarted` and `IGameStarted`. | [Game/Bootstrap](game/bootstrap.md#hooks) |

## How it works

- A sender calls `Broadcast` for an `Event`. Each handler runs. The sender does not wait.
- A sender calls `BroadcastAsync` for an `AsyncEvent`. The handlers run one after the other. The sender waits for each handler.
- A failing handler does not stop the other handlers. The bus writes the failure to the log. A cancellation stops an asynchronous broadcast.
- The subscriber list is copy-on-write. A handler can subscribe or unsubscribe during a broadcast.

## Two buses

| Bus | Use |
|-----|-----|
| Client bus | Events between modules. |
| Module bus | Events from a module to its own views. Only for this communication. |

## Hooks

A hook is an event on the client bus. A module receives it through an interface instead of a subscription that it makes itself.

1. The game defines an event and an interface. For example `GameStarted` and `IGameStarted`.
2. A module implements the interface.
3. The game calls `Modules.Bind` or `Modules.BindAsync` one time. This call connects the event to the interface.
4. `ModuleScope` subscribes each matching module to the client bus. It does this after the module starts.
5. Any code that has the client bus broadcasts the event. Each matching module receives the call, in the order of addition.
6. `ModuleScope` unsubscribes a module before it disposes the module.

A module that is added after the start receives only the later events. The bus and the scope do not replay an event. Refer to [Infra/Module](infra/module.md#hooks) for the details. To add a hook, use the skill [`create-hook`](../../.claude/skills/create-hook/SKILL.md).

## Rules

- A module subscribes in `Start` and unsubscribes in `Dispose`. A module that uses a hook does not subscribe. `ModuleScope` does it.
- Use the module bus (`BaseModule.Broadcaster`) only for events between a module and its views. Use the client bus for events between modules.
- Make the `Bind` call for each hook interface. A hook without a `Bind` call reaches no module, and the code does not report it.
- A view subscribes in `ManagedAwake` and unsubscribes in `ManagedDispose`.
- Core defines no events or hooks. A game project defines its own.
- A hook lives in the same folder and namespace as the code that broadcasts its event.

## Related responsibilities

- [Modules and views](modules-and-views.md): the owners of the module bus.
- [Diagnostics](diagnostics.md): how the bus reports handler failures.
