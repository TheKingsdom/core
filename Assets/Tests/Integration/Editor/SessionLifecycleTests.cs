using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.UI.Windows;
using Game.Bootstrap;
using Game.Session;
using Core.Infra.Messaging;
using Core.Infra.Module;
using Core.Input;
using Core.Resources;
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
        private ResourceScope _resources;
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
            _resources = new ResourceScope();
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
            _resources.Dispose();

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
                var network = CreatePeer("solo", (_, _) =>
                    UniTask.FromResult<IDisposable>(new ActionLease(() => released++)));
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
                var peer = CreateModulePeer("cancel", async (_, token) =>
                {
                    entered.TrySetResult();
                    await UniTask.Delay(60000, cancellationToken: token);
                    return new ActionLease(() => { });
                });
                var network = peer.Network;
                var pending = peer.Module.CreateAsync(new CreateSessionRequest("cancel", isSolo: true)).AsTask();
                Assert.That(peer.Windows.CurrentWindow, Is.TypeOf<LoadingWindow>());
                await entered.Task.Timeout(TimeSpan.FromSeconds(40));
                Assert.That(peer.Module.GameView, Is.Null);
                peer.Windows.CurrentWindow.Cancel();
                var result = await pending;
                Assert.That(peer.Windows.CurrentWindow, Is.Null);
                Assert.That(peer.Module.GameView, Is.Null);
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

                    return UniTask.FromResult<IDisposable>(new ActionLease(() => { }));
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
        public IEnumerator CancelAfterInitializationReturnsReleasesLateLease()
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
                    return new ActionLease(() => released++);
                });
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
                var first = CreatePeer("alice");

                var created = await first.CreateSessionAsync(new CreateSessionRequest(Guid.NewGuid().ToString("N"),
                    playerCount: 4, saveSession: false));
                Assert.That(created.IsSuccess, Is.True, created.Error?.Reason);
                var locator = first.Snapshot.Session;
                var peer = CreateModulePeer("bob", startupTarget: locator);
                var second = peer.Network;
                var boot = peer.Broadcaster.BroadcastAsync(new GameStarted()).AsTask();
                Assert.That(peer.Windows.CurrentWindow, Is.TypeOf<LoadingWindow>());
                Assert.That(peer.Module.GameView, Is.Null);
                await boot;
                Assert.That(second.Snapshot.Phase, Is.EqualTo(ConnectionPhase.InRoom));
                Assert.That((bool)peer.Module.GameView.Data.LocalPlayer.IsReady, Is.True);
                var secondContext = second.Runner.GetComponent<PlayerSessionContext>();
                var avatarId = secondContext.Bootstrap.LocalPlayer.Object.Id;
                await peer.Module.LeaveAsync(LeaveMode.KeepReconnect);
                Assert.That(peer.Module.GameView, Is.Null);
                var firstContext = first.Runner.GetComponent<PlayerSessionContext>();
                await UniTask.WaitUntil(() => firstContext.Players.Avatars.Length == 2 &&
                                              Array.Exists(firstContext.Players.Avatars,
                                                  record => record.Avatar.IsDisconnected))
                    .Timeout(TimeSpan.FromSeconds(15));
                var reconnect = peer.Module.ReconnectAsync().AsTask();
                Assert.That(peer.Windows.CurrentWindow, Is.TypeOf<LoadingWindow>());
                Assert.That(peer.Module.GameView, Is.Null);
                var rejoined = await reconnect;
                Assert.That(rejoined.IsSuccess, Is.True, rejoined.Error?.Reason);
                secondContext = second.Runner.GetComponent<PlayerSessionContext>();
                Assert.That(secondContext.Bootstrap.LocalPlayer.Object.Id, Is.EqualTo(avatarId));
                Assert.That(peer.Module.GameView.Data.LocalPlayer.Object.Id, Is.EqualTo(avatarId));
                Assert.That(peer.Module.GameView.Data.Scene.isLoaded, Is.True);
                Assert.That(secondContext.Players.Avatars.Length, Is.EqualTo(2));
                await first.LeaveSessionAsync(LeaveMode.ForgetSession);
                await UniTask.WaitUntil(() => secondContext.Director.Object.HasStateAuthority)
                    .Timeout(TimeSpan.FromSeconds(15));
                Assert.That((bool)secondContext.Bootstrap.LocalPlayer.IsReady, Is.True);
                Assert.That(second.Runner.IsRunning, Is.True);
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
                    Assert.That(peer.Windows.CurrentWindow, Is.TypeOf<LoadingWindow>());
                    Assert.That(peer.Module.GameView, Is.Null);
                    var loading = peer.Windows.CurrentWindow;
                    var overlayRoot = CreateRoot("Overlay");
                    var overlay = peer.Windows.Open<Action>(new WindowOwner(), CreateLoadingWindow(overlayRoot.transform),
                        overlayRoot.transform, () => { }, isDisplacing: false);

                    var result = await join;
                    Assert.That(result.IsSuccess, Is.True, result.Error?.Reason);
                    Assert.That(loading.gameObject.activeSelf, Is.False);
                    Assert.That(peer.Windows.CurrentWindow, Is.SameAs(overlay));
                    peer.Windows.Close();
                    Assert.That(peer.Windows.CurrentWindow, Is.Null);
                    Assert.That((bool)peer.Module.GameView.Data.LocalPlayer.IsReady, Is.True);
                    Assert.That(peer.Module.GameView.Data.Scene.isLoaded, Is.True);
                    Assert.That(peer.Module.GameView.gameObject.activeInHierarchy, Is.True);
                    Assert.That(peer.Windows.Input.GameplayEnabled, Is.True);

                    await PressPauseAsync(keyboard, peer.Windows);
                    var pause = (PauseWindow)peer.Windows.CurrentWindow;
                    Assert.That(peer.Windows.Input.GameplayEnabled, Is.False);
                    pause.Cancel();
                    Assert.That(peer.Windows.CurrentWindow, Is.Null);
                    Assert.That(peer.Windows.Input.GameplayEnabled, Is.True);

                    await PressPauseAsync(keyboard, peer.Windows);
                    Assert.That(peer.Windows.CurrentWindow, Is.SameAs(pause));
                    pause.Leave();
                    await UniTask.WaitUntil(() => peer.Network.Runner == null).Timeout(TimeSpan.FromSeconds(15));
                    Assert.That(peer.Module.GameView, Is.Null);
                    Assert.That(peer.Windows.CurrentWindow, Is.Null);
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
            var peer = CreateModulePeer("window-subscriptions");
            var template = CreateLoadingWindow(CreateRoot("Window templates").transform);
            var first = 0;
            var second = 0;
            peer.Windows.Open(peer.Module, template, null, (Action)(() => first++));
            var window = peer.Windows.CurrentWindow;
            var button = window.GetComponentInChildren<UnityEngine.UI.Button>();
            button.onClick.Invoke();
            Assert.That(first, Is.EqualTo(1));

            peer.Windows.Close();
            button.onClick.Invoke();
            Assert.That(first, Is.EqualTo(1));

            peer.Windows.Open(peer.Module, template, null, (Action)(() => second++));
            Assert.That(peer.Windows.CurrentWindow, Is.SameAs(window));
            button.onClick.Invoke();
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));

            peer.Windows.ReleaseViews(peer.Module);
            button.onClick.Invoke();
            Assert.That(second, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedWindowCleanupKeepsTheRemainingStackAndCurrentWindow()
        {
            var peer = CreateModulePeer("window-cleanup");
            var root = CreateRoot("Window templates");
            var bottomTemplate = CreateLoadingWindow(root.transform);
            var topTemplate = CreateLoadingWindow(root.transform);
            var failingRoot = new GameObject("Failing window", typeof(RectTransform), typeof(Canvas));
            failingRoot.transform.SetParent(root.transform, false);
            failingRoot.SetActive(false);
            var failingTemplate = failingRoot.AddComponent<ThrowingCleanupWindow>();
            var failingOwner = new WindowOwner();
            var failure = new InvalidOperationException("Window cleanup failed");
            Action failOnClose = () => throw failure;

            peer.Windows.Open<Action>(peer.Module, bottomTemplate, root.transform, () => { });
            var bottom = peer.Windows.CurrentWindow;
            peer.Windows.Open(failingOwner, failingTemplate, root.transform, failOnClose, isDisplacing: false);
            var failing = peer.Windows.CurrentWindow;
            peer.Windows.Open<Action>(peer.Module, topTemplate, root.transform, () => { }, isDisplacing: false);
            var top = peer.Windows.CurrentWindow;

            var error = Assert.Throws<AggregateException>(() => peer.Windows.CloseViews(failingOwner));
            Assert.That(error.InnerException, Is.SameAs(failure));
            Assert.That(failing.gameObject.activeSelf, Is.False);
            Assert.That(peer.Windows.CurrentWindow, Is.SameAs(top));
            Assert.That(top.gameObject.activeSelf, Is.True);
            peer.Windows.Close();
            Assert.That(peer.Windows.CurrentWindow, Is.SameAs(bottom));

            peer.Windows.Open(failingOwner, failingTemplate, root.transform, failOnClose, isDisplacing: false);
            Assert.Throws<AggregateException>(() => peer.Windows.CloseViews(failingOwner));
            Assert.That(peer.Windows.CurrentWindow, Is.SameAs(bottom));
            peer.Windows.Close();
            Assert.That(peer.Windows.CurrentWindow, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WindowBecomesInactiveWhenOnCloseThrows()
        {
            var peer = CreateModulePeer("window-on-close");
            var root = CreateRoot("Window templates");
            var template = new GameObject("Failing OnClose", typeof(RectTransform), typeof(Canvas));
            template.transform.SetParent(root.transform, false);
            template.SetActive(false);
            var window = template.AddComponent<ThrowingOnCloseWindow>();
            window.Init(peer.Module, new ResourcesAssetService());
            window.Open(null);

            var error = Assert.Throws<InvalidOperationException>(window.Close);
            Assert.That(error.Message, Is.EqualTo("OnClose failed"));
            Assert.That(window.gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningAfterRemovingMiddleWindowGivesEachCanvasADistinctOrder()
        {
            var peer = CreateModulePeer("window-order");
            var root = CreateRoot("Window templates").transform;
            Action model = () => { };
            var bottom = peer.Windows.Open(peer.Module, CreateLoadingWindow(root), root, model);
            var middle = peer.Windows.Open(peer.Module, CreateLoadingWindow(root), root, model, isDisplacing: false);
            var top = peer.Windows.Open(peer.Module, CreateLoadingWindow(root), root, model, isDisplacing: false);

            peer.Windows.Close(middle);
            var next = peer.Windows.Open(peer.Module, CreateLoadingWindow(root), root, model, isDisplacing: false);

            Assert.That(middle.gameObject.activeSelf, Is.False);
            Assert.That(peer.Windows.CurrentWindow, Is.SameAs(next));
            var orders = new[] { bottom, top, next }.Select(window => window.GetComponent<Canvas>().sortingOrder);
            Assert.That(orders, Is.EqualTo(new[] { 1, 2, 3 }));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleaseAndDisposeDestroyAllWindowsAfterCleanupErrors()
        {
            var peer = CreateModulePeer("release-windows");
            var root = CreateRoot("Window templates");
            var failingRoot = new GameObject("Failing window", typeof(RectTransform), typeof(Canvas));
            failingRoot.transform.SetParent(root.transform, false);
            failingRoot.SetActive(false);
            var failingTemplate = failingRoot.AddComponent<ThrowingCleanupWindow>();
            var normalTemplate = CreateLoadingWindow(root.transform);
            var owner = new WindowOwner();

            foreach (var dispose in new[] { false, true })
            {
                var normal = peer.Windows.Open<Action>(owner, normalTemplate, root.transform, () => { });
                var failing = peer.Windows.Open<Action>(owner, failingTemplate, root.transform,
                    () => throw new InvalidOperationException("Window cleanup failed"), isDisplacing: false);

                var error = Assert.Throws<AggregateException>(() =>
                {
                    if (dispose)
                    {
                        peer.Windows.Dispose();
                    }
                    else
                    {
                        peer.Windows.ReleaseViews(owner);
                    }
                });
                Assert.That(error.Flatten().InnerExceptions.Single().Message, Is.EqualTo("Window cleanup failed"));
                Assert.That(peer.Windows.CurrentWindow, Is.Null);
                yield return null;
                Assert.That(normal == null, Is.True);
                Assert.That(failing == null, Is.True);
            }
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
                Assert.That(peer.Module.GameView, Is.Null);
                Assert.That(peer.Windows.CurrentWindow, Is.Null);
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
                network.Configure(new PlayerSessionCoordinator(network, identity, scene, async (_, token) =>
                {
                    initializing = true;
                    await UniTask.WaitUntil(() => false, cancellationToken: token);
                    return new ActionLease(() => { });
                }, readyTimeout: TimeSpan.FromSeconds(5)));

                var result = await network.CreateSessionAsync(new CreateSessionRequest("timeout", isSolo: true));
                Assert.That(initializing, Is.True);
                Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
                Assert.That(result.Error.Code, Is.EqualTo(NetworkErrorCode.Timeout));
                Assert.That(network.Runner, Is.Null);
            });
        }

        [UnityTest]
        public IEnumerator StoppingPlayModeReleasesClientInputReferences()
        {
            var parent = CreateRoot("Client");
            parent.SetActive(false);
            var ui = parent.AddComponent<InputSystemUIInputModule>();
            var client = parent.AddComponent<GameClient>();
            var settings = new SerializedObject(client);
            settings.FindProperty("_sessionScene").stringValue = WorldPath;
            settings.FindProperty("_viewAnchor").objectReferenceValue = parent.transform;
            settings.FindProperty("_uiInput").objectReferenceValue = ui;
            settings.FindProperty("_loadingWindow").objectReferenceValue = CreateLoadingWindow(parent.transform);
            settings.FindProperty("_gameView").objectReferenceValue = CreateGameView(parent.transform);
            settings.ApplyModifiedPropertiesWithoutUndo();
            LogAssert.Expect(LogType.Log, "Game started");
            parent.SetActive(true);

            InputActionReference[] references = null;
            yield return UniTask.ToCoroutine(async () =>
            {
                await UniTask.WaitUntil(() => client.Session != null).Timeout(TimeSpan.FromSeconds(15));
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

        private static async UniTask PressPauseAsync(Keyboard keyboard, WindowsService windows)
        {
            await UniTask.DelayFrame(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            await UniTask.WaitUntil(() => windows.CurrentWindow is PauseWindow).Timeout(TimeSpan.FromSeconds(5));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await UniTask.DelayFrame(2);
        }

        private (NetworkService Network, WindowsService Windows, SessionModule Module, IBroadcaster Broadcaster)
            CreateModulePeer(string identity,
                Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> initialize = null,
                SessionLocator startupTarget = null, bool failView = false)
        {
            var root = CreateRoot(identity);
            var localIdentity = new LocalPlayerIdentity(identity);
            var network = CreateNetwork(localIdentity);
            var actions = InputActionsFactory.Create();
            var input = _resources.Own(new LocalInputService(actions));
            UnityEngine.Object.Destroy(actions);
            var assets = new ResourcesAssetService();
            var windows = _resources.Own(new WindowsService(assets, input));
            var bus = new Broadcaster();
            var scope = _resources.Own(new ModuleScope(bus));
            var pause = new GameObject("Pause", typeof(RectTransform), typeof(Canvas)).AddComponent<PauseWindow>();
            pause.transform.SetParent(root.transform, false);
            pause.gameObject.SetActive(false);
            var module = scope.Add(new SessionModule(network, localIdentity, input, windows, assets,
                SceneRef.FromIndex(0), root.transform, CreateLoadingWindow(root.transform),
                CreateGameView(root.transform, failView), pause, startupTarget, initializePlayer: initialize));
            scope.BindAsync<GameStarted, IGameStarted>((handler, _) => handler.OnGameStarted());
            scope.Start();
            return (network, windows, module, bus);
        }

        private NetworkService CreatePeer(string identity,
            Func<PlayerAvatar, CancellationToken, UniTask<IDisposable>> initialize = null)
        {
            var localIdentity = new LocalPlayerIdentity(identity);
            var network = CreateNetwork(localIdentity);
            var scene = SceneRef.FromIndex(0);
            network.Configure(new PlayerSessionCoordinator(network, localIdentity, scene,
                initialize ?? ((_, _) => UniTask.FromResult<IDisposable>(new ActionLease(() => { })))));
            network.InitialScene = scene;
            return network;
        }

        private NetworkService CreateNetwork(LocalPlayerIdentity identity)
        {
            var network = new NetworkService(new LobbyDirectory(), new ReconnectStore(TimeSpan.FromMinutes(5)),
                identity);
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

        private sealed class ThrowingOnCloseWindow : WindowBase
        {
            public override Type ModelType => typeof(object);

            protected override void OnOpen(object model)
            {
            }

            protected override void OnClose() => throw new InvalidOperationException("OnClose failed");
        }

        private sealed class ThrowingCleanupWindow : WindowBase<Action>
        {
            protected override void OnOpen()
            {
                Opening.Own(new ActionLease(ActiveModel));
            }
        }
    }
}
