---
name: create-hook
description: Create a module hook in this Unity Core project. A hook is an event plus an interface that modules implement, connected with ModuleScope.Bind or BindAsync. Use when the user asks to add a hook such as IGameStarted or IUserConnected, or wants modules to react to a moment in the game.
---

# Create a hook

A hook lets every module that implements an interface react to one event on the client bus. Read these pages first:

- `docs/architecture/infra/module.md#hooks` for the mechanism.
- `docs/architecture/game/bootstrap.md#hooks` for the existing hooks and the steps.
- `docs/code-style.md` for the code style, and `docs/conventions.md` before you write a doc.

## 1. Collect the facts

Find out these facts. Ask the user only for what you cannot find in the code.

| Fact | Rule |
|------|------|
| Name of the moment | Past tense, for example `UserConnected`. The event has this name. The interface is `I<Name>`. The method is `On<Name>`. |
| Sync or async | Sync uses `Event`, `Bind` and `Broadcast`. Async uses `AsyncEvent`, `BindAsync` and `BroadcastAsync`, and the method returns `UniTask`. Use async if a handler must finish before the invoker continues. |
| Payload | The data that a handler needs. It goes on the event as read-only properties. |
| Invoker | The code that broadcasts the event. |

## 2. Put the files next to the invoker

The event and the interface live in the same folder and have the same namespace as the invoker. The namespace of a file is the path of its folder under `Assets/Scripts`.

- Invoker `Assets/Scripts/Game/Bootstrap/GameClient.cs`: the files go in `Assets/Scripts/Game/Bootstrap`, namespace `Game.Bootstrap`.
- Invoker `Assets/Scripts/Game/Connection/ConnectionModule.cs`: the files go in `Assets/Scripts/Game/Connection`, namespace `Game.Connection`.
- Do not make a `Hooks` folder. Do not put a hook in `Core`. Core defines no events and no hooks. Do not put a file directly in `Assets/Scripts/Game`, because this folder holds only `Game.asmdef` and folders.

Make one file for the event (`<Name>.cs`) and one for the interface (`I<Name>.cs`). Mark the event class `sealed`.

Sync hook:

```csharp
using Core.Infra.Messaging;

namespace Game.Connection
{
    public sealed class UserConnected : Event
    {
        public UserConnected(UserInfo user)
        {
            User = user;
        }

        public UserInfo User { get; }
    }
}
```

```csharp
namespace Game.Connection
{
    public interface IUserConnected
    {
        void OnUserConnected(UserInfo user);
    }
}
```

Async hook: the event derives from `AsyncEvent`. The interface method returns `UniTask` (`using Cysharp.Threading.Tasks;`).

If the hook has no payload, the event has no members and the method has no parameters.

Follow `docs/code-style.md`: the opening bracket on the next line, `var` where possible, no unneeded comments, and no `try` or `catch`.

## 3. Add the .meta files

The repository tracks `.meta` files. Make one for each new `.cs` file, with a new GUID. In PowerShell, `[guid]::NewGuid().ToString('N')` gives a GUID.

```
fileFormatVersion: 2
guid: <new guid>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

If you make a new folder, it needs a `<Folder>.meta` file with `folderAsset: yes` and a `DefaultImporter` block.

## 4. Bind the hook

Add one call in `GameClient.CreateModules`, after `base.CreateModules()`:

```csharp
Modules.Bind<UserConnected, IUserConnected>((module, message) => module.OnUserConnected(message.User));
Modules.BindAsync<UserConnected, IUserConnected>((module, message) => module.OnUserConnected(message.User));
```

Use the first line for a sync hook and the second for an async hook. A hook without a `Bind` call reaches no module, and the code does not report it.

## 5. Broadcast the event

The invoker broadcasts the event on the client bus (`Client.Broadcaster`, or the `IBroadcaster` that the module receives in its constructor). Never use `BaseModule.Broadcaster`. This bus is only for a module and its views.

```csharp
Broadcaster.Broadcast(new UserConnected(user));
await Broadcaster.BroadcastAsync(new UserConnected(user));
```

- A module receives hooks only after `ModuleScope.Start` has ended. If the invoker runs inside a `Start` method, no module receives the event.
- A module that is added later receives only the events that are broadcast after it is attached. There is no replay.
- A handler that throws is logged, and the next handler runs.

## 6. Write the docs

Docs are in ASD-STE100 Simplified Technical English. Follow `docs/conventions.md`. Change the docs in the same commit.

1. Add a row to the table in the Hooks section of the folder page of the invoker. For `Assets/Scripts/Game/Bootstrap` this is `docs/architecture/game/bootstrap.md`. For any other folder it is `docs/architecture/game/<name>.md`. Add the hook there if the Hooks section is not there.
2. Add the new file names to the `Files:` line of that page.
3. If you add a folder page, add it to the tables in `docs/architecture/overview.md`.

## 7. Check

- Look in the Unity Editor log (`%LOCALAPPDATA%\Unity\Editor\Editor.log`) for `error CS` after the editor recompiles. If the editor has not recompiled, tell the user.
- Make sure that the namespace of each new file equals the folder path under `Assets/Scripts`.
- Match the line endings of the files next to the new files. The `.cs`, `.meta` and most docs files use CRLF.
- Do not commit unless the user asks.
