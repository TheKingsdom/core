# Testing

Run the project EditMode tests with Unity Test Runner. Select the project test assemblies. The integration tests enter Play Mode inside their fixture. The fixture temporarily enables its own scene in the build scene list and restores your previous list on exit.

| Assembly | Coverage |
|----------|----------|
| `Core.Input.Tests` | Input routing, action gates, and text input |
| `Core.UI.Tests` | Window navigation and pointer behavior |
| `Core.Networking.Tests` | Operation gate, runner retirement, scene registration, shutdown, spawning, roster, and leases |
| `Core.Integration.Tests` | Local room entry, ready game view, loading cancellation, failure and retry, window reopening, input reference release, and two-peer reconnect |

The `PhotonOnline` case first checks `CORE_PHOTON_APP_ID`. If that value is empty, it checks the local `Core.PhotonAppId` preference and then `PhotonAppSettings`. The test skips when none is configured. It uses a unique Shared room and checks that the returning player keeps the same avatar after reconnect.

The test scene and prefab are in `Assets/Tests/Integration/Fixtures`. The tests temporarily register the avatar prefab in Fusion and restore the global configuration on exit.
