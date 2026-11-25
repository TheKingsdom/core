# Bootstrap and lifecycle

## Responsibility

This responsibility starts the application. It builds the services and the modules in the correct order. It starts the modules. It stops them in the reverse order.

## Parts

| Part | What it does | Folder page |
|------|--------------|-------------|
| `Client` | The composition root. It runs the boot sequence. | [Infra/Bootstrap](infra/bootstrap.md) |
| `IModule`, `ModuleScope` | The lifecycle contract of a module. The scope starts and disposes a group of modules. | [Infra/Module](infra/module.md) |

## How it works

1. `Client.Awake` makes the client event bus and the services.
2. `Client` waits for the services to initialize. The record service loads the records.
3. `Client` builds the modules by hand, in dependency order. It adds each module to the `ModuleScope`.
4. The `ModuleScope` starts the modules in the order of addition.
5. `Client` calls `StartGame`. A game subclass starts the game here.
6. When `Client` is destroyed, the `ModuleScope` disposes the modules in reverse order.

There is no DI container and no service locator. A constructor receives each dependency. A module can only use a dependency that was made before the module.

## Module or service

A service and a module do different jobs. A service has no common base type. It is an interface, for example `IAssetService`.

| | Service | Module |
|---|---------|--------|
| Job | Does work when a caller asks. Gives data. | Holds behavior and state. Reacts to events. |
| Lifetime | The application. | From `Start` to `Dispose`. |
| Made in | `Client.CreateServices` | `Client.CreateModules` |
| Base type | None | `IModule`, `BaseModule` |
| Events | Has no event bus. Does not send events. | `BaseModule` gives it its own `Broadcaster`. |
| Dependencies | Can use another service. Does not use a module. | Can use services and the modules that were made before it. |
| Disposed by | Nobody | `ModuleScope`, in reverse order |

Use these rules to choose:

- If the code only answers calls, make a service.
- If the code keeps state that changes while the game runs, or sends events, make a module.
- Do not make a module that only passes calls to a service. Give the service to the modules that need it.

## Rules

- A module does its side effects in `Start`. It reverses them in `Dispose`.
- A module does not look up another module by type.
- A service finishes its initialization before the first module is built.

## Related responsibilities

- [Modules and views](modules-and-views.md): the structure of a module.
- [Asset management](asset-management.md) and [Records](records.md): the services that `Client` makes.
- [Commands](commands.md): a game can run a command from `StartGame`.
