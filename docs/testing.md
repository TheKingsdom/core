# Testing

Run the project EditMode tests with Unity Test Runner. Select the project test assemblies. The integration tests enter Play Mode inside their fixture. The fixture temporarily enables its own scene in the build scene list and restores your previous list on exit.

| Assembly | Coverage |
|----------|----------|
| `Core.Input.Tests` | Input routing, action gates, and text input |
| `Core.UI.Tests` | Window navigation and pointer behavior |
| `Core.Networking.Tests` | Operation gate, runner retirement, scene registration, shutdown, spawning, and roster |
| `Core.Integration.Tests` | Local room entry, ready game view, loading cancellation, failure and retry, asset release, window reopening, input reference release, two-peer reconnect, and lobby recovery |

The `PhotonOnline` cases first check `CORE_PHOTON_APP_ID`. If that value is empty, it checks the local `Core.PhotonAppId` preference and then `PhotonAppSettings`. The tests skip when none is configured. They use a unique Shared room for avatar reconnect and also check recovery after a lobby runner stops. The fixtures use reconnect state in memory.

The test scene and prefab are in `Assets/Tests/Integration/Fixtures`. The tests temporarily register the avatar prefab in Fusion and restore the global configuration on exit.
