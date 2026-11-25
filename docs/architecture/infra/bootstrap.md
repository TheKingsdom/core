# Infra/Bootstrap

Source folder: `Assets/Scripts/Core/Infra/Bootstrap`. File: `Client.cs`.

Responsibilities: [Bootstrap and lifecycle](../bootstrap-and-lifecycle.md), [Messaging](../messaging.md).

## Purpose

`Core.Infra.Bootstrap.Client` is the composition root of the application. It is a `MonoBehaviour`. `Game.Bootstrap.GameClient` is the subclass of this project. Refer to [Game/Bootstrap](../game/bootstrap.md).

`Client` builds the services and the modules by hand. It holds the `ModuleScope` for the app-lifetime modules. Refer to [module](module.md).

## Boot order

1. `Awake` makes the client `Broadcaster` and the `ModuleScope`. The scope uses the client `Broadcaster` for hooks. `Awake` calls `CreateServices()`. Then it starts `Boot()` and does not wait for the result.
2. `Boot()` stores `destroyCancellationToken` before it waits for `InitializeServices()`. The base method calls `RecordService.CreateAll()`. The records are loaded before any module exists.
3. If the client is destroyed during this load (`destroyCancellationToken`), `Boot()` stops.
4. `CreateModules()` builds the modules. The base method is empty.
5. `ModuleScope.Start()` starts each module in the order of addition. Then it attaches the hooks.
6. `Boot()` waits for `StartGame()`. The base method returns a completed `UniTask`.

`OnDestroy` disposes the module scope.

```
Client.Awake
 ├─ new Broadcaster()
 ├─ new ModuleScope(Broadcaster)
 ├─ CreateServices()
 └─ Boot()  (asynchronous)
     ├─ InitializeServices()
     ├─ CreateModules()
     ├─ ModuleScope.Start()
     └─ StartGame()
```

## Extension points

A subclass can change these `protected virtual` methods:

| Method | Base behavior |
|--------|---------------|
| `CreateServices` | Makes `ResourcesAssetService` and `RecordService`. |
| `InitializeServices` | Returns `Records.CreateAll()`. A subclass that adds work must call the base method first if the work needs the records. |
| `CreateModules` | Empty. |
| `StartGame` | Returns a completed `UniTask`. `Boot()` waits for it. |

## Members

| Member | Purpose |
|--------|---------|
| `Broadcaster` | Public property. The client event bus. Refer to [messaging](messaging.md). |
| `Modules` | Protected property. The `ModuleScope`. A subclass calls `Bind` and `BindAsync` on it to register hooks. Refer to [module](module.md#hooks). |
| `Assets` | Protected property. The `IAssetService`. |
| `Records` | Protected property. The `IRecordService`. |
| `AddModule<T>(module)` | Adds a module to the scope and returns it. |

`StartGame` runs after the hooks are attached. A game can broadcast a hook event from `StartGame` and wait for the handlers.

To use Addressables, a subclass overrides `CreateServices`. It makes an `AddressablesAssetService` for `Assets` and gives it to `RecordService`. Refer to [Services/Asset](../services/asset/README.md).

## Notes

- `Boot()` runs through `FireAndForget`. An exception during boot is written to the log. It is not thrown.
- `Client` does not dispose the services. It disposes only the modules.
