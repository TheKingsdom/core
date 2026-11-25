# Windows

Source folder `Assets/Scripts/Core/UI/Windows`.

`WindowsService` has application scope. Call `Open` with the owning module, prefab, anchor, and typed model. The service initializes the instance through `ViewBase` and reuses it for that module and prefab. Each interactive canvas needs a `GraphicRaycaster`. Use `UiInputBinding` to connect an EventSystem input module to the local input service.

`Open` displaces the stack by default. With `isDisplacing: false`, it pushes the window over the active window. `Close` removes the top window. `CloseViews` closes windows owned by a module and `ReleaseViews` also disposes their cached instances.

`CurrentWindow` gives the active window. The service broadcasts `CurrentWindowChanged` and `WindowClosed`. On a change it clears transient input. The session module uses this state to block gameplay.

Add subscriptions for a single open to `WindowBase.Opening`. `Close` releases that scope and clears the model. A new open gets a new scope. Use `ManagedDispose` for final cleanup and call the base implementation.

The service owns the text input service. `HandleCancel` cancels active text input before it calls the current window's `Cancel` method.
