# Windows

Source folder `Assets/Scripts/Core/UI/Windows`. Files `WindowBase.cs` and `WindowStack.cs`.

`WindowBase<TModule>` derives from `ViewBase<TModule>`. Use the owning module's internal interface for `TModule`. The owning module loads the instance by asset ID, initializes it, and adds it to a `WindowStack<TModule>`.

The stack can displace its current windows or place another window above them. It closes a specific instance at any depth and updates the order of the remaining canvases. `CurrentWindow` gives the top window. The stack tells its owner when this value changes.

Use `OnOpen` to add opening-specific handlers. Use `OnClose` to remove them. `Close` hides the object even if `OnClose` fails. The owner calls `Close` before disposal. Use `ManagedDispose` to unsubscribe and release view resources.

Add a `GraphicRaycaster` to each interactive canvas. See [Game/Windows](../game/windows.md) for asset ownership and the connection to text input.
