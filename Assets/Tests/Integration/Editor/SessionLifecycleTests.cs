using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.UI.Windows;
using Game.Bootstrap;
using Game.Session;
using Game.Windows;
using Core.Infra.Messaging;
using Core.Infra.Module;
using Core.Input;
using Core.Services.Asset;
using Core.Services.Data;
using Core.Services.Asset.Resource;
using System.Threading;
using Core.Networking.Core;
using Core.Networking.Fusion;
using Core.Networking.Players;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Core.Tests.Integration
{
    public sealed class SessionLifecycleTests
    {
        private readonly List<GameObject> _roots = new();
        private const string AvatarPath = "Assets/Tests/Integration/Fixtures/SessionAvatar.prefab";
        private NetworkObject _avatarPrefab;
        private NetworkObjectFlags _avatarFlags;
        private NetworkObjectTypeId _avatarType;
        private const string WorldPath = "Assets/Tests/Integration/Fixtures/SessionWorld.unity";
        private readonly List<NetworkService> _networks = new();
        private readonly List<LocalInputService> _inputs = new();
        private readonly List<ModuleScope> _scopes = new();
        private readonly List<FixtureAssets> _assetServices = new();
        private EditorBuildSettingsScene[] _buildScenes;
        private SceneSetup[] _scenes;
        private string _originalAppId;
        private InputSettings.EditorInputBehaviorInPlayMode _oldEditorInput;
        private InputSettings.BackgroundBehavior _oldBackground;
        private bool _oldPlayOptionsEnabled;
        private EnterPlayModeOptions _oldPlayOptions;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _buildScenes = EditorBuildSettings.scenes;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(WorldPath, true) };
            _scenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _oldPlayOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldPlayOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            yield return new EnterPlayMode(false);
            _originalAppId = PhotonAppSettings.Global.AppSettings.AppIdFusion;
            _avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath).GetComponent<NetworkObject>();
            _avatarFlags = _avatarPrefab.Flags;
            _avatarType = _avatarPrefab.NetworkTypeId;
            _avatarPrefab.Flags &= ~NetworkObjectFlags.Ignore;
            var guid = NetworkObjectGuid.Parse(AssetDatabase.AssetPathToGUID(AvatarPath));
            var prefabs = NetworkProjectConfig.Global.PrefabTable;
            prefabs.AddSource(new NetworkPrefabSourceStatic
            {
                AssetGuid = guid,
                AssetPath = AvatarPath,
                Object = _avatarPrefab
            });
            _avatarPrefab.NetworkTypeId = prefabs.GetId(guid);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var network in _networks)
            {
                if (EditorApplication.isPlaying)
                {
                    var leave = network.LeaveSessionAsync(LeaveMode.ForgetSession).AsTask();
                    while (!leave.IsCompleted)
                    {
                        yield return null;
                    }
                }

                network.Dispose();
            }

            _networks.Clear();
            foreach (var scope in _scopes)
            {
                scope.Dispose();
            }
            _scopes.Clear();

            foreach (var input in _inputs)
            {
                input.Dispose();
            }
            _inputs.Clear();

            foreach (var assets in _assetServices)
            {
                assets.DestroyRecordAssets();
            }
            _assetServices.Clear();

            foreach (var root in _roots)
            {
                if (root)
                {
                    UnityEngine.Object.Destroy(root);
                }
            }

            _roots.Clear();
            PhotonAppSettings.Global.AppSettings.AppIdFusion = _originalAppId;
            yield return null;
            if (EditorApplication.isPlaying)
            {
                yield return new ExitPlayMode();
            }

            if (_avatarPrefab)
            {
                _avatarPrefab.Flags = _avatarFlags;
                _avatarPrefab.NetworkTypeId = _avatarType;
            }

            NetworkProjectConfig.UnloadGlobal();
            EditorBuildSettings.scenes = _buildScenes;
            InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorInput;
            InputSystem.settings.backgroundBehavior = _oldBackground;
            EditorSettings.enterPlayModeOptionsEnabled = _oldPlayOptionsEnabled;
            EditorSettings.enterPlayModeOptions = _oldPlayOptions;
            if (_scenes.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_scenes);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [UnityTest]
        public IEnumerator SoloSessionCanLeaveAndStartWithFreshRunner()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var released = 0;
                var network = CreatePeer("solo", release: _ => released++);
                var first = await network.CreateSessionAsync(new CreateSessionRequest("solo", isSolo: true));
                Assert.That(first.IsSuccess, Is.True, first.Error?.Reason);
                var runner = network.Runner;
                var generation = network.Generation;
                var context = runner.GetComponent<PlayerSessionContext>();
                Assert.That((bool)context.Bootstrap.LocalPlayer.IsReady, Is.True);
                Assert.That(context.Players.Avatars.Length, Is.EqualTo(1));
                var leave = await network.LeaveSessionAsync(LeaveMode.ForgetSession);
                Assert.That(leave.IsSuccess, Is.True, leave.Error?.Reason);
                Assert.That(context.Players.IsDisposed, Is.True);
                Assert.That(released, Is.EqualTo(1));
                Assert.That(network.Runner, Is.Null);
                var second = await network.CreateSessionAsync(new CreateSessionRequest("again", isSolo: true));
                Assert.That(second.IsSuccess, Is.True, second.Error?.Reason);
                Assert.That(network.Generation.Value, Is.GreaterThan(generation.Value));
                Assert.That(network.Runner, Is.Not.SameAs(runner));
                Assert.That(released, Is.EqualTo(1));
                await network.LeaveSessionAsync(LeaveMode.ForgetSession);
                Assert.That(released, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator CancelDuringLocalInitializationReleasesSession()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var entered = new UniTaskCompletionSource();
                var network = CreatePeer("cancel", async (_, token) =>
                {
                    entered.TrySetResult();
                    await UniTask.Delay(60000, cancellationToken: token);
                });
                using var cancellation = new CancellationTokenSource();
                var pending = network.CreateSessionAsync(new CreateSessionRequest("cancel", isSolo: true),
                    cancellation.Token).AsTask();
                await entered.Task.Timeout(TimeSpan.FromSeconds(15));
                cancellation.Cancel();
                var result = await pending;
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Cancelled));
                Assert.That(network.Runner, Is.Null);
                Assert.That(network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
            });
        }

        [UnityTest]
        public IEnumerator CancelDuringSpawnReturnsCancelledAndReleasesRunner()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var network = CreateNetwork(new LocalPlayerIdentity("cancel-spawn"));
                var scene = SceneRef.FromIndex(0);
                var playerEvents = new Broadcaster();
                using var cancellation = new CancellationTokenSource();
                playerEvents.Subscribe<PlayerAvatarChanged>(message =>
                {
                    if (message.Kind == AvatarChangeKind.Registered)
                    {
                        cancellation.Cancel();
                    }
                });
                network.InitialScene = scene;
                network.Configure(new PlayerSessionCoordinator(network, scene, broadcaster: playerEvents));

                var result = await network.CreateSessionAsync(new CreateSessionRequest("cancel-spawn", isSolo: true),
                    cancellation.Token);

                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Cancelled));
                Assert.That(network.Runner, Is.Null);
                Assert.That(network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
            });
        }

        [UnityTest]
        public IEnumerator LocalInitializationFailureCanRetryWithFreshRunner()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var fail = true;
                PlayerSessionContext failedContext = null;
                var network = CreatePeer("retry", (avatar, _) =>
                {
                    if (fail)
                    {
                        failedContext = avatar.Runner.GetComponent<PlayerSessionContext>();
                        throw new InvalidOperationException("Local initialization failed");
                    }

                    return UniTask.CompletedTask;
                });
                LogAssert.Expect(LogType.Exception,
                    new System.Text.RegularExpressions.Regex("InvalidOperationException: Local initialization failed"));
                var first = await network.CreateSessionAsync(new CreateSessionRequest("failure", isSolo: true));
                Assert.That(first.Status, Is.EqualTo(OperationStatus.Failed));
                Assert.That(network.Runner, Is.Null);
                Assert.That(failedContext.Players.IsDisposed, Is.True);
                Assert.That(network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
                var generation = network.Generation;
                fail = false;
                var retry = await network.CreateSessionAsync(new CreateSessionRequest("retry", isSolo: true));
                Assert.That(retry.IsSuccess, Is.True, retry.Error?.Reason);
                Assert.That(network.Generation.Value, Is.GreaterThan(generation.Value));
                Assert.That((bool)network.Runner.GetComponent<PlayerSessionContext>().Bootstrap.LocalPlayer.IsReady,
                    Is.True);
            });
        }

        [UnityTest]
        public IEnumerator CancelAfterInitializationReturnsRunsPlayerCleanupOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var entered = new UniTaskCompletionSource();
                var release = new UniTaskCompletionSource();
                var released = 0;
                var network = CreatePeer("late", async (_, _) =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }, _ => released++);
                using var cancellation = new CancellationTokenSource();
                var pending = network
                    .CreateSessionAsync(new CreateSessionRequest("late", isSolo: true), cancellation.Token).AsTask();
                try
                {
                    await entered.Task.Timeout(TimeSpan.FromSeconds(40));
                    cancellation.Cancel();
                }
                finally
                {
                    release.TrySetResult();
                }

                var result = await pending;
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Cancelled));
                Assert.That(released, Is.EqualTo(1));
                Assert.That(network.Runner, Is.Null);
                await network.LeaveSessionAsync(LeaveMode.ForgetSession);
                Assert.That(released, Is.EqualTo(1));
            });
        }

        [UnityTest, Category("PhotonOnline")]
        public IEnumerator TwoPeersReconnectToExistingAvatarAndSurviveOwnerLeaving()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ConfigurePhotonOrIgnore();
                var first = CreatePeer("alice");

                var created = await first.CreateSessionAsync(new CreateSessionRequest(Guid.NewGuid().ToString("N"),
                    playerCount: 4, saveSession: false));
                Assert.That(created.IsSuccess, Is.True, created.Error?.Reason);
                var locator = first.Snapshot.Session;
                var peer = CreateModulePeer("bob", startupTarget: locator);
                var second = peer.Network;
                var boot = peer.Broadcaster.BroadcastAsync(new GameStarted()).AsTask();
                Assert.That(peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId).isActiveAndEnabled, Is.True);
                Assert.That(peer.Assets.GameView, Is.Null);
                await boot;
                Assert.That(second.Snapshot.Phase, Is.EqualTo(ConnectionPhase.InRoom));
                Assert.That((bool)peer.Assets.GameView.Data.LocalPlayer.IsReady, Is.True);
                var secondContext = second.Runner.GetComponent<PlayerSessionContext>();
                var avatarId = secondContext.Bootstrap.LocalPlayer.Object.Id;
                await WaitForConfirmedPlayersAsync(first.Runner, 2);
                await peer.Module.LeaveAsync(LeaveMode.KeepReconnect);
                Assert.That(peer.Assets.GameView, Is.Null);
                var firstContext = first.Runner.GetComponent<PlayerSessionContext>();
                await UniTask.WaitUntil(() => firstContext.Players.Avatars.Length == 2 &&
                                              Array.Exists(firstContext.Players.Avatars,
                                                  record => record.Avatar.IsDisconnected))
                    .Timeout(TimeSpan.FromSeconds(15));
                await WaitForConfirmedPlayersAsync(first.Runner, 1);
                var reconnect = peer.Module.ReconnectAsync().AsTask();
                Assert.That(peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId).isActiveAndEnabled, Is.True);
                Assert.That(peer.Assets.GameView, Is.Null);
                var rejoined = await reconnect;
                Assert.That(rejoined.IsSuccess, Is.True, rejoined.Error?.Reason);
                secondContext = second.Runner.GetComponent<PlayerSessionContext>();
                Assert.That(secondContext.Bootstrap.LocalPlayer.Object.Id, Is.EqualTo(avatarId));
                Assert.That(peer.Assets.GameView.Data.LocalPlayer.Object.Id, Is.EqualTo(avatarId));
                Assert.That(peer.Assets.GameView.Data.Scene.isLoaded, Is.True);
                Assert.That(secondContext.Players.Avatars.Length, Is.EqualTo(2));
                await WaitForConfirmedPlayersAsync(first.Runner, 2);
                await first.LeaveSessionAsync(LeaveMode.ForgetSession);
                await UniTask.WaitUntil(() => secondContext.Director.Object.HasStateAuthority)
                    .Timeout(TimeSpan.FromSeconds(15));
                Assert.That((bool)secondContext.Bootstrap.LocalPlayer.IsReady, Is.True);
                Assert.That(second.Runner.IsRunning, Is.True);
            });
        }

        [UnityTest, Category("PhotonOnline")]
        public IEnumerator LobbyCanReconnectAfterItsRunnerStops()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ConfigurePhotonOrIgnore();
                var network = CreatePeer("lobby");
                var result = await network.ConnectToLobbyAsync();
                Assert.That(result.IsSuccess, Is.True, result.Error?.Reason);
                var lostRunner = network.Runner;

                await lostRunner.Shutdown(destroyGameObject: false).AsUniTask();
                await UniTask.WaitUntil(() => network.Runner == null).Timeout(TimeSpan.FromSeconds(15));
                Assert.That(network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
                Assert.That(network.Directory.Sessions, Is.Empty);

                result = await network.ConnectToLobbyAsync();
                Assert.That(result.IsSuccess, Is.True, result.Error?.Reason);
                Assert.That(network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Lobby));
                Assert.That(network.Runner, Is.Not.SameAs(lostRunner));
            });
        }

        [UnityTest]
        public IEnumerator SoloSessionShowsReadyViewAndReopensPauseWindow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                var peer = CreateModulePeer("windows");
                try
                {
                    var join = peer.Module.CreateAsync(new CreateSessionRequest("solo", isSolo: true)).AsTask();
                    Assert.That(peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId).isActiveAndEnabled, Is.True);
                    Assert.That(peer.Assets.GameView, Is.Null);
                    var loading = peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId);
                    await peer.Windows.OpenPauseAsync(() => { }, () => { }, CancellationToken.None);
                    var overlay = peer.Assets.View<PauseWindow>(FixtureAssets.PauseId);

                    var result = await join;
                    Assert.That(result.IsSuccess, Is.True, result.Error?.Reason);
                    Assert.That(loading.gameObject.activeSelf, Is.False);
                    Assert.That(overlay.isActiveAndEnabled, Is.True);
                    Assert.That(peer.Windows.HasOpenWindow, Is.True);
                    overlay.Cancel();
                    Assert.That(peer.Windows.HasOpenWindow, Is.False);
                    Assert.That((bool)peer.Assets.GameView.Data.LocalPlayer.IsReady, Is.True);
                    Assert.That(peer.Assets.GameView.Data.Scene.isLoaded, Is.True);
                    Assert.That(peer.Assets.GameView.gameObject.activeInHierarchy, Is.True);
                    Assert.That(peer.Input.GameplayEnabled, Is.True);

                    await PressPauseAsync(keyboard, peer.Windows);
                    var pause = peer.Assets.View<PauseWindow>(FixtureAssets.PauseId);
                    Assert.That(peer.Input.GameplayEnabled, Is.False);
                    pause.Cancel();
                    Assert.That(peer.Windows.HasOpenWindow, Is.False);
                    Assert.That(peer.Input.GameplayEnabled, Is.True);

                    await PressPauseAsync(keyboard, peer.Windows);
                    Assert.That(peer.Assets.View<PauseWindow>(FixtureAssets.PauseId), Is.SameAs(pause));
                    Assert.That(pause.isActiveAndEnabled, Is.True);
                    pause.Leave();
                    await UniTask.WaitUntil(() => peer.Network.Runner == null).Timeout(TimeSpan.FromSeconds(15));
                    Assert.That(peer.Assets.GameView, Is.Null);
                    Assert.That(peer.Windows.HasOpenWindow, Is.False);
                }
                finally
                {
                    InputSystem.RemoveDevice(keyboard);
                }
            });
        }

        [UnityTest]
        public IEnumerator ReopeningLoadingWindowReplacesItsButtonSubscription()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var peer = CreateModulePeer("window-subscriptions");
                var first = 0;
                var second = 0;
                await peer.Windows.OpenLoadingAsync(() => first++, CancellationToken.None);
                var window = peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId);
                var button = window.GetComponentInChildren<UnityEngine.UI.Button>();
                button.onClick.Invoke();
                Assert.That(first, Is.EqualTo(1));

                peer.Windows.CloseLoading();
                button.onClick.Invoke();
                Assert.That(first, Is.EqualTo(1));

                await peer.Windows.OpenLoadingAsync(() => second++, CancellationToken.None);
                Assert.That(peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId), Is.SameAs(window));
                button.onClick.Invoke();
                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(1));

                peer.Windows.Dispose();
                button.onClick.Invoke();
                Assert.That(second, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator LoadingWindowCancelsEntryWhileGameViewAssetIsPending()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var peer = CreateModulePeer("cancel-loaded-view");
                var requested = new UniTaskCompletionSource();
                var complete = new UniTaskCompletionSource();
                peer.Assets.BeforeInstantiate = async id =>
                {
                    if (id == FixtureAssets.GameViewId)
                    {
                        requested.TrySetResult();
                        await complete.Task;
                    }
                };

                var join = peer.Module.CreateAsync(new CreateSessionRequest("cancel", isSolo: true)).AsTask();
                try
                {
                    await requested.Task.Timeout(TimeSpan.FromSeconds(15));
                    Assert.That(peer.Assets.GameView, Is.Null);
                    var loading = peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId);
                    Assert.That(loading.isActiveAndEnabled, Is.True);
                    loading.GetComponentInChildren<UnityEngine.UI.Button>().onClick.Invoke();

                    var result = await join.AsUniTask().Timeout(TimeSpan.FromSeconds(15));
                    Assert.That(result.Status, Is.EqualTo(OperationStatus.Cancelled));
                    Assert.That(peer.Windows.HasOpenWindow, Is.False);
                    Assert.That(peer.Assets.GameView, Is.Null);
                    Assert.That(peer.Network.Runner, Is.Null);
                    CollectionAssert.DoesNotContain(peer.Assets.Destroyed, FixtureAssets.GameViewId);
                }
                finally
                {
                    complete.TrySetResult();
                }

                await UniTask.WaitUntil(() => peer.Assets.Destroyed.Contains(FixtureAssets.GameViewId))
                    .Timeout(TimeSpan.FromSeconds(5));
                Assert.That(peer.Assets.GameView, Is.Null);
            });
        }

        [UnityTest]
        public IEnumerator FailedWindowCleanupKeepsTheRemainingStackAndCurrentWindow()
        {
            var stack = new WindowStack<WindowOwner>(null);
            var root = CreateRoot("Window stack").transform;
            var bottom = CreateTestWindow(root);
            var failing = CreateTestWindow(root);
            var top = CreateTestWindow(root);
            var failure = new InvalidOperationException("Window cleanup failed");
            failing.CloseAction = () => throw failure;
            stack.Open(bottom, isDisplacing: true);
            stack.Open(failing, isDisplacing: false);
            stack.Open(top, isDisplacing: false);

            Assert.That(Assert.Throws<InvalidOperationException>(() => stack.Close(failing)), Is.SameAs(failure));
            Assert.That(failing.gameObject.activeSelf, Is.False);
            Assert.That(stack.CurrentWindow, Is.SameAs(top));
            Assert.That(top.gameObject.activeSelf, Is.True);
            stack.Close(top);
            Assert.That(stack.CurrentWindow, Is.SameAs(bottom));

            stack.Open(failing, isDisplacing: false);
            Assert.Throws<InvalidOperationException>(() => stack.Close(failing));
            Assert.That(stack.CurrentWindow, Is.SameAs(bottom));
            stack.Close(bottom);
            Assert.That(stack.CurrentWindow, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WindowBecomesInactiveWhenOnCloseThrows()
        {
            var window = CreateTestWindow(CreateRoot("Window templates").transform);
            window.CloseAction = () => throw new InvalidOperationException("OnClose failed");
            window.Open();

            var error = Assert.Throws<InvalidOperationException>(window.Close);
            Assert.That(error.Message, Is.EqualTo("OnClose failed"));
            Assert.That(window.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WindowDisposalDoesNotInvokeTheOpeningCloseHandler()
        {
            var window = CreateTestWindow(CreateRoot("Window lifecycle").transform);
            var closed = 0;
            window.CloseAction = () => closed++;
            window.Open();

            window.Dispose();
            Assert.That(closed, Is.Zero);
            window.Close();
            Assert.That(closed, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningAfterRemovingMiddleWindowGivesEachCanvasADistinctOrder()
        {
            var stack = new WindowStack<WindowOwner>(null);
            var root = CreateRoot("Window stack").transform;
            var bottom = CreateTestWindow(root);
            var middle = CreateTestWindow(root);
            var top = CreateTestWindow(root);
            var next = CreateTestWindow(root);
            stack.Open(bottom, isDisplacing: true);
            stack.Open(middle, isDisplacing: false);
            stack.Open(top, isDisplacing: false);

            stack.Close(middle);
            stack.Open(next, isDisplacing: false);

            Assert.That(middle.gameObject.activeSelf, Is.False);
            Assert.That(stack.CurrentWindow, Is.SameAs(next));
            var orders = new[] { bottom, top, next }.Select(window => window.GetComponent<Canvas>().sortingOrder);
            Assert.That(orders, Is.EqualTo(new[] { 1, 2, 3 }));
            stack.CloseAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CloseAllContinuesAfterOneWindowFailsToClose()
        {
            var stack = new WindowStack<WindowOwner>(null);
            var root = CreateRoot("Window stack").transform;
            var bottom = CreateTestWindow(root);
            var failing = CreateTestWindow(root);
            failing.CloseAction = () => throw new InvalidOperationException("OnClose failed");
            stack.Open(bottom, isDisplacing: true);
            stack.Open(failing, isDisplacing: false);

            var error = Assert.Throws<AggregateException>(stack.CloseAll);
            Assert.That(error.InnerExceptions.Single().Message, Is.EqualTo("OnClose failed"));
            Assert.That(stack.CurrentWindow, Is.Null);
            Assert.That(bottom.gameObject.activeSelf, Is.False);
            Assert.That(failing.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WindowsModuleDisposesEveryAssetEvenWhenOneReleaseFails()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var peer = CreateModulePeer("release-windows");
                await peer.Windows.OpenLoadingAsync(() => { }, CancellationToken.None);
                await peer.Windows.OpenPauseAsync(() => { }, () => { }, CancellationToken.None);
                var loading = peer.Assets.View<LoadingWindow>(FixtureAssets.LoadingId);
                var pause = peer.Assets.View<PauseWindow>(FixtureAssets.PauseId);
                peer.Assets.FailOnDestroy = FixtureAssets.PauseId;

                var error = Assert.Throws<AggregateException>(peer.Windows.Dispose);
                Assert.That(error.InnerExceptions.Single().Message, Is.EqualTo("Asset release failed"));
                CollectionAssert.AreEquivalent(new[] { FixtureAssets.LoadingId, FixtureAssets.PauseId },
                    peer.Assets.Destroyed);
                Assert.That(peer.Windows.HasOpenWindow, Is.False);
                await UniTask.WaitUntil(() => !loading && !pause).Timeout(TimeSpan.FromSeconds(5));
            });
        }

        [UnityTest]
        public IEnumerator FailedGameViewStartAndDisposalStillLeaveTheRoom()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var peer = CreateModulePeer("failing-game-view", failView: true);
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Game view disposal failed"));
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("Game view start failed"));

                var result = await peer.Module.CreateAsync(new CreateSessionRequest("view", isSolo: true));
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
                Assert.That(result.Error.Reason, Is.EqualTo("Game view start failed"));
                Assert.That(peer.Network.Runner, Is.Null);
                Assert.That(peer.Network.Snapshot.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
                Assert.That(peer.Assets.GameView, Is.Null);
                Assert.That(peer.Windows.HasOpenWindow, Is.False);
            });
        }

        [UnityTest]
        public IEnumerator LocalReadinessDeadlineReturnsTimeoutAndReleasesRunner()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var identity = new LocalPlayerIdentity("readiness-timeout");
                var network = CreateNetwork(identity);
                var scene = SceneRef.FromIndex(0);
                var initializing = false;
                network.InitialScene = scene;
                network.Configure(new PlayerSessionCoordinator(network, scene, async (_, token) =>
                {
                    initializing = true;
                    await UniTask.WaitUntil(() => false, cancellationToken: token);
                }, readyTimeout: TimeSpan.FromSeconds(5)));

                var result = await network.CreateSessionAsync(new CreateSessionRequest("timeout", isSolo: true));
                Assert.That(initializing, Is.True);
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
                Assert.That(result.Error.Code, Is.EqualTo(NetworkErrorCode.Timeout));
                Assert.That(network.Runner, Is.Null);
            });
        }

        [UnityTest]
        public IEnumerator InvalidSessionSettingsDoNotStartWindows()
        {
            var parent = CreateRoot("Client with invalid settings");
            parent.SetActive(false);
            var client = parent.AddComponent<FixtureClient>();
            client.FixtureAssets = CreateFixtureAssets(CreateRoot("Client assets").transform);
            JsonUtility.FromJsonOverwrite("{\"_gameViewId\":\"\"}", client.FixtureAssets.SessionRecord);
            var activeEventSystem = EventSystem.current;

            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("Set a session build scene and a game view asset ID in SessionRecord"));
            parent.SetActive(true);
            yield return null;

            Assert.That(client.Session, Is.Null);
            Assert.That(EventSystem.current, Is.SameAs(activeEventSystem));
            Assert.That(GameObject.Find("Windows"), Is.Null);
        }

        [UnityTest]
        public IEnumerator ClientBroadcastsNetworkSnapshotHookOnEntryAndExit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var parent = CreateRoot("Client for network hooks");
                parent.SetActive(false);
                var client = parent.AddComponent<FixtureClient>();
                client.FixtureAssets = CreateFixtureAssets(CreateRoot("Client assets").transform);
                LogAssert.Expect(LogType.Log, "Game started");
                parent.SetActive(true);

                await UniTask.WaitUntil(() => client.Session != null).Timeout(TimeSpan.FromSeconds(15));
                var entered = await client.Session.CreateAsync(new CreateSessionRequest("hooks", isSolo: true));
                Assert.That(entered.IsSuccess, Is.True);
                Assert.That(client.Snapshots.Last?.Phase, Is.EqualTo(ConnectionPhase.InRoom));

                var left = await client.Session.LeaveAsync();
                Assert.That(left.IsSuccess, Is.True);
                Assert.That(client.Snapshots.Last?.Phase, Is.EqualTo(ConnectionPhase.Disconnected));
            });
        }

        [UnityTest]
        public IEnumerator StoppingPlayModeReleasesClientInputReferences()
        {
            var parent = CreateRoot("Client");
            parent.SetActive(false);
            var client = parent.AddComponent<FixtureClient>();
            client.FixtureAssets = CreateFixtureAssets(CreateRoot("Client assets").transform);
            LogAssert.Expect(LogType.Log, "Game started");
            parent.SetActive(true);

            InputActionReference[] references = null;
            yield return UniTask.ToCoroutine(async () =>
            {
                await UniTask.WaitUntil(() => client.Session != null).Timeout(TimeSpan.FromSeconds(15));
                var ui = EventSystem.current.GetComponent<InputSystemUIInputModule>();
                references = new[] { ui.point, ui.leftClick, ui.move, ui.submit, ui.cancel };
                Assert.That(
                    references.All(reference => reference && reference.action?.actionMap?.asset == ui.actionsAsset),
                    Is.True);
            });
            yield return new ExitPlayMode();
            var deadline = EditorApplication.timeSinceStartup + 10;
            while (references.Any(reference => reference) && EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(references.All(reference => !reference), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        private void ConfigurePhotonOrIgnore()
        {
            var appId = Environment.GetEnvironmentVariable("CORE_PHOTON_APP_ID");
            if (string.IsNullOrWhiteSpace(appId))
            {
                appId = PlayerPrefs.GetString("Core.PhotonAppId");
            }

            if (string.IsNullOrWhiteSpace(appId))
            {
                appId = _originalAppId;
            }

            if (string.IsNullOrWhiteSpace(appId))
            {
                Assert.Ignore("Configure a Photon Fusion App ID to run the two-peer test.");
            }

            PhotonAppSettings.Global.AppSettings.AppIdFusion = appId;
        }

        private static UniTask WaitForConfirmedPlayersAsync(NetworkRunner runner, int expected)
        {
            return UniTask.WaitUntil(() => runner.SessionInfo.Properties.TryGetValue(
                    LobbyDirectory.ConfirmedPlayersPropertyKey, out var count) && count.IsInt && (int)count == expected)
                .Timeout(TimeSpan.FromSeconds(15));
        }

        private static async UniTask PressPauseAsync(Keyboard keyboard, IWindowsModule windows)
        {
            await UniTask.DelayFrame(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            await UniTask.WaitUntil(() => windows.HasOpenWindow).Timeout(TimeSpan.FromSeconds(5));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await UniTask.DelayFrame(2);
        }

        private (NetworkService Network, WindowsModule Windows, SessionModule Module, IBroadcaster Broadcaster,
            LocalInputService Input, FixtureAssets Assets) CreateModulePeer(string identity,
            SessionLocator startupTarget = null, bool failView = false)
        {
            var root = CreateRoot(identity);
            var localIdentity = new LocalPlayerIdentity(identity);
            var bus = new Broadcaster();
            var network = CreateNetwork(localIdentity, bus);
            var actions = InputActionsFactory.Create();
            var input = new LocalInputService(actions);
            _inputs.Add(input);
            UnityEngine.Object.Destroy(actions);

            var assets = CreateFixtureAssets(root.transform, startupTarget, failView);
            var scope = new ModuleScope(bus);
            _scopes.Add(scope);
            var windows = scope.Add(new WindowsModule(assets, input, assets.WindowsRecord, bus));
            var module = scope.Add(new SessionModule(network, input, assets, windows, assets.SessionRecord));
            scope.BindAsync<GameStarted, IGameStarted>((handler, _) => handler.OnGameStarted());
            scope.Bind<WindowChanged, IWindowChanged>((handler, change) => handler.OnWindowChanged(change.HasOpenWindow));
            scope.Bind<NetworkSnapshot, INetworkSnapshotChanged>((handler, change) => handler.OnNetworkSnapshotChanged(change));
            scope.Start();
            return (network, windows, module, bus, input, assets);
        }

        private FixtureAssets CreateFixtureAssets(Transform parent, SessionLocator startupTarget = null, bool failView = false)
        {
            var pause = new GameObject("Pause", typeof(RectTransform), typeof(Canvas)).AddComponent<PauseWindow>();
            pause.transform.SetParent(parent, false);
            pause.gameObject.SetActive(false);
            var assets = new FixtureAssets(CreateLoadingWindow(parent), pause, CreateGameView(parent, failView), startupTarget);
            _assetServices.Add(assets);
            return assets;
        }

        private NetworkService CreatePeer(string identity, Func<PlayerAvatar, CancellationToken, UniTask> initialize = null,
            Action<PlayerAvatar> release = null)
        {
            var network = CreateNetwork(new LocalPlayerIdentity(identity));
            var scene = SceneRef.FromIndex(0);
            network.InitialScene = scene;
            network.Configure(new PlayerSessionCoordinator(network, scene, initialize, release));
            return network;
        }

        private NetworkService CreateNetwork(LocalPlayerIdentity identity, IBroadcaster bus = null)
        {
            var network = new NetworkService(new LobbyDirectory(), new ReconnectStore(TimeSpan.FromMinutes(5),
                persistAcrossLaunches: false), identity, bus);
            _networks.Add(network);
            return network;
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            _roots.Add(root);
            UnityEngine.Object.DontDestroyOnLoad(root);
            return root;
        }

        private static LoadingWindow CreateLoadingWindow(Transform parent)
        {
            var root = new GameObject("Loading", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(parent, false);
            root.SetActive(false);
            var window = root.AddComponent<LoadingWindow>();
            var button = new GameObject("Cancel", typeof(RectTransform), typeof(UnityEngine.UI.Button));
            button.transform.SetParent(root.transform, false);
            var settings = new SerializedObject(window);
            settings.FindProperty("_cancel").objectReferenceValue = button.GetComponent<UnityEngine.UI.Button>();
            settings.ApplyModifiedPropertiesWithoutUndo();
            return window;
        }

        private static GameSessionView CreateGameView(Transform parent, bool fail = false)
        {
            var root = new GameObject("GameView");
            root.transform.SetParent(parent, false);
            root.SetActive(false);
            return fail ? root.AddComponent<FailingGameView>() : root.AddComponent<GameSessionView>();
        }

        private sealed class FailingGameView : GameSessionView
        {
            protected override void ManagedStart() => throw new InvalidOperationException("Game view start failed");

            protected override void ManagedDispose() => throw new InvalidOperationException("Game view disposal failed");
        }

        private sealed class WindowOwner : IInternalModule
        {
            public IBroadcaster Broadcaster { get; } = new Broadcaster();
        }

        private static TestWindow CreateTestWindow(Transform parent)
        {
            var root = new GameObject("Test window", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(parent, false);
            root.SetActive(false);
            var window = root.AddComponent<TestWindow>();
            window.Init(new WindowOwner(), new ResourcesAssetService());
            return window;
        }

        private sealed class TestWindow : WindowBase<WindowOwner>
        {
            public Action CloseAction { get; set; }

            protected override void OnClose() => CloseAction?.Invoke();
        }

        private sealed class FixtureClient : GameClient
        {
            public FixtureAssets FixtureAssets { get; set; }
            public SnapshotObserver Snapshots { get; private set; }

            protected override void CreateModules()
            {
                base.CreateModules();
                Snapshots = AddModule(new SnapshotObserver());
            }

            protected override IAssetService CreateAssetService() => FixtureAssets;

            protected override ReconnectStore CreateReconnectStore() =>
                new(TimeSpan.FromMinutes(5), persistAcrossLaunches: false);
        }

        private sealed class SnapshotObserver : BaseModule, INetworkSnapshotChanged
        {
            public NetworkSnapshot Last { get; private set; }

            public override void Start()
            {
            }

            public override void Dispose()
            {
            }

            void INetworkSnapshotChanged.OnNetworkSnapshotChanged(NetworkSnapshot snapshot) => Last = snapshot;
        }

        private sealed class FixtureAssets : IAssetService
        {
            public const string LoadingId = "Fixture/Loading";
            public const string PauseId = "Fixture/Pause";
            public const string GameViewId = "Fixture/GameView";

            private readonly Dictionary<string, GameObject> _templates;
            private readonly Dictionary<string, GameObject> _instances = new();
            private readonly RecordStorage _storage;

            public List<string> Destroyed { get; } = new();
            public string FailOnDestroy { get; set; }
            public Func<string, UniTask> BeforeInstantiate { get; set; }
            public SessionRecord SessionRecord { get; }
            public WindowsRecord WindowsRecord { get; }

            public FixtureAssets(LoadingWindow loading, PauseWindow pause, GameSessionView gameView,
                SessionLocator startupTarget)
            {
                _templates = new Dictionary<string, GameObject>
                {
                    [LoadingId] = loading.gameObject,
                    [PauseId] = pause.gameObject,
                    [GameViewId] = gameView.gameObject
                };
                var session = ScriptableObject.CreateInstance<SessionRecordAsset>();
                SessionRecord = (SessionRecord)session.Record;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new SessionRecordData
                {
                    _scenePath = WorldPath,
                    _gameViewId = GameViewId,
                    _startupRoom = startupTarget?.Name,
                    _startupRegion = startupTarget?.Region
                }), SessionRecord);

                var windows = ScriptableObject.CreateInstance<WindowsRecordAsset>();
                WindowsRecord = (WindowsRecord)windows.Record;
                JsonUtility.FromJsonOverwrite(
                    "{\"_loadingWindowId\":\"Fixture/Loading\",\"_pauseWindowId\":\"Fixture/Pause\"}", WindowsRecord);
                _storage = ScriptableObject.CreateInstance<RecordStorage>();
                _storage.Records = new RecordSO[] { session, windows };
            }

            public GameSessionView GameView => View<GameSessionView>(GameViewId);

            public T View<T>(string id) where T : MonoBehaviour =>
                _instances.TryGetValue(id, out var instance) ? instance.GetComponent<T>() : null;

            public async UniTask<GameObject> Instantiate(string id)
            {
                if (BeforeInstantiate != null)
                {
                    await BeforeInstantiate(id);
                }

                var instance = UnityEngine.Object.Instantiate(_templates[id]);
                _instances[id] = instance;
                return instance;
            }

            public GameObject Instantiate<T>(IInstance<T> instance) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public async UniTask<T> Instantiate<T>(string id) where T : MonoBehaviour =>
                (await Instantiate(id)).GetComponent<T>();

            public UniTask<T> Load<T>(string id) where T : UnityEngine.Object =>
                UniTask.FromResult(id == "RecordsStorage" ? _storage as T : throw new ArgumentException(id));

            public void Destroy<T>(IInstance<T> instance) where T : UnityEngine.Object
            {
                Destroyed.Add(instance.Id);
                _instances.Remove(instance.Id);
                var value = instance.Value;
                if (value)
                {
                    DestroyObject(value is Component component ? component.gameObject : value);
                }

                if (instance.Id == FailOnDestroy)
                {
                    throw new InvalidOperationException("Asset release failed");
                }
            }

            public void Unload<T>(IInstance<T> instance) where T : UnityEngine.Object =>
                throw new NotSupportedException();

            public void DestroyRecordAssets()
            {
                foreach (var record in _storage.Records)
                {
                    DestroyObject(record);
                }
                DestroyObject(_storage);
            }

            private static void DestroyObject(UnityEngine.Object value)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(value);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }

            [Serializable]
            private sealed class SessionRecordData
            {
                public string _scenePath;
                public string _gameViewId;
                public string _startupRoom;
                public string _startupRegion;
            }
        }
    }
}
