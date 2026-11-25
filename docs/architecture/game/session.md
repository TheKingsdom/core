# Game/Session

Source folder `Assets/Scripts/Game/Session`. Files include `SessionModule.cs`, `GameSessionView.cs`, `LoadingWindow.cs`, and `PauseWindow.cs`.

The [session module](../coop-sessions.md) uses the game-wide services to create and leave rooms. It owns the loading and pause windows and gives the game view its initial data when the runner is ready.

`SessionModule.Start` installs runner composition and subscribes to the service and window broadcasters. When `GameClient` broadcasts `GameStarted`, `OnGameStarted` awaits the configured startup join or resume. Later room commands use the same module methods. The hook does not replay when a module enters the scope later.
