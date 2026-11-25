# Infra/Module

Source folder: `Assets/Scripts/Core/Infra/Module`. Files: `IModule.cs`, `BaseModule.cs`, `IInternalModule.cs`, `ModuleScope.cs`, `IHookBinding.cs`, `HookBinding.cs`, `AsyncHookBinding.cs`.

Responsibilities: [Bootstrap and lifecycle](../bootstrap-and-lifecycle.md), [Modules and views](../modules-and-views.md), [Messaging](../messaging.md).

## Purpose

A module holds the behavior of one feature. A view holds the presentation. Core defines the module types. A game project uses them for each feature. A feature has this shape:

```
IFooModule           public interface for other modules             : IModule
IFooModuleInternal   interface for the views of FooModule           : IInternalModule
FooModule            implementation                                 : BaseModule, IFooModule, IFooModuleInternal
View/FooView         MonoBehaviour for the presentation             : ViewBase<IFooModuleInternal>
```

Refer to [view](view.md) for `ViewBase<T>`.

## Types

### IModule

```csharp
public interface IModule : IDisposable
{
    void Start();
}
```

- A module gets its dependencies through the constructor.
- `Start` does side effects only. For example, it subscribes to events, makes entities or finds scene objects.
- `Dispose` reverses what `Start` did.

### IInternalModule

`IInternalModule` has one member: `IBroadcaster Broadcaster`. `BaseModule` implements it, so a module does not need to implement it again. A view is typed against an `Internal` interface. The view can call only the methods that the module chooses to give it.

### BaseModule

`BaseModule` implements `IInternalModule`. It gives each module its own `ViewBroadcaster`. This bus is a private channel between the module and its views. Use it only for this communication. It is not the client bus.

- `ViewBroadcaster` is a protected property. Only the module and its subclasses can use it. A module sends events to its views on this bus.
- Code that holds the module as its public interface cannot get this bus. The views get it through the explicit member `IInternalModule.Broadcaster`.
- A module does not use this bus to talk to another module. A module uses the client bus for this.
- A [hook](#hooks) is an event on the client bus. It never uses the bus of a module.

Refer to [messaging](messaging.md).

A module derives from `BaseModule`. It implements its public interface and its internal interface. Other modules receive the module as the public interface. If a module must give an object to a view, it uses the `Internal` interface.

### ModuleScope

`ModuleScope` controls the life of a group of modules. The group can be the whole client or one scene. `ModuleScope` is not a registry. The constructor takes the `IBroadcaster` that carries the hooks of the group.

- `Add<T>(module)` stores the module and returns it. The order of the `Add` calls is the start order. If the scope has already started, `Add` starts the module at once and then attaches its hooks.
- `Start()` starts the modules in the order of addition. Then it attaches the hooks of all modules. A second call does nothing.
- `Bind<TEvent, THook>(call)` and `BindAsync<TEvent, THook>(call)` register a hook. Refer to [Hooks](#hooks).
- `Dispose()` disposes the modules in reverse order. Dependents are disposed before their dependencies. It detaches the hooks of a module before it disposes the module. Then it clears the lists.
- If a module throws an exception in `Dispose`, `ModuleScope` writes the exception to the log. It then continues with the other modules.

The order of construction is the order of dependency. A module can only receive a dependency that was made and added before the module.

### Hooks

A hook lets a module react to a moment in the life of the game, for example the start of the game. A hook has two parts:

- An event class that derives from `Event` or `AsyncEvent`.
- An interface that a module implements to receive the hook.

Core defines no hooks. A game project defines its own. Refer to [Game/Hooks](../game/hooks.md).

`Bind` connects an event to an interface. `call` receives the module and the event. `BindAsync` does the same for an `AsyncEvent`. `call` returns a `UniTask`.

```csharp
Modules.BindAsync<GameStarted, IGameStarted>((module, _) => module.OnGameStarted());
```

When `ModuleScope` attaches a module, it subscribes one handler to the `IBroadcaster` for each binding that the module matches. A module matches a binding when it implements the interface of the binding. To fire a hook, any code broadcasts the event on the same bus.

- The order of the hooks is the order of addition, because the handlers are subscribed in that order.
- `Broadcaster` runs the handlers. It logs a handler that throws and continues. `BroadcastAsync` waits for each handler in turn.
- A module receives a hook only after the scope has started. An event that is broadcast before this point reaches no module.
- A handler runs only while its module is attached. If `ModuleScope` detaches a module during a broadcast, the handler of this module does not run, even if the broadcast started earlier.
- A module that is added after the start receives only the events that are broadcast after it is attached. `ModuleScope` does not replay old events.
- `Bind` after the start attaches the modules that already exist.

The classes `HookBinding<TEvent, THook>` and `AsyncHookBinding<TEvent, THook>` implement the internal interface `IHookBinding`. Each class holds the handlers of its attached modules so that it can unsubscribe them. They are internal to Core.

`Client` holds one `ModuleScope` for the app-lifetime modules. Refer to [bootstrap](bootstrap.md). A game project can make more `ModuleScope` objects. For example, it can make one for each scene.
