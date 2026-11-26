# Input

Source folder: `Assets/Scripts/Core/Input`. Files: LocalInputService.cs, GameInputAction.cs, InputActionsFactory.cs, KeyboardShortcuts.cs.

Responsibility: [Coop sessions](../coop-sessions.md).

## Purpose

Input has no network or UI dependency. It owns a copy of the action asset. The owner binds and unbinds the local avatar explicitly. Context changes clear pending presses and require held action buttons to release. Device detection selects keyboard or gamepad hints. [Text input](textinput.md) defines platform providers.

## Action contract

`InputActionsFactory.Create` supplies the default `Gameplay`, `Common`, and `UI` action maps.
`LocalInputService` resolves each `GameInputAction` by map and action name. A custom asset must preserve those names.
The generic actions cover movement, look, jump, sprint, crouch, pause, pointer, click, navigation, submit, and cancel.
The session module gathers movement input and handles pause. The other actions remain available to game code.

## Player and window changes

`BindPlayer` selects the avatar and starts with gameplay blocked.
Call `SetGameplayBlocked` with the same player object to enable or block gameplay.
`UnbindPlayer` clears the binding only if its avatar matches the active binding.

`Move` and `GetLookDelta` respect the gameplay gate.
`ConsumePress` removes a queued gameplay press. `WasPressed` reads the current frame action state through its context gate.
`ClearTransientInput` clears queued presses and requires held gameplay action buttons to release before reuse.
The application and window service use that method when the context changes.

The service keeps the action asset enabled behind the gate so that it can observe physical releases.
Dispose the service to remove global Input System callbacks and release its copied asset.
Use the same copied asset for UI actions. Refer to [Windows](../ui/windows.md) for UI module setup.
