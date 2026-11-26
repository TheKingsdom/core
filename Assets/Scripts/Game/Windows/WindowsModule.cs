using System;
using System.Collections.Generic;
using System.Threading;
using Core.Infra.Messaging;
using Core.Infra.Module;
using Core.Input;
using Core.Input.TextInput;
using Core.Services.Asset;
using Core.UI;
using Core.UI.Windows;
using Cysharp.Threading.Tasks;
using Game.Bootstrap;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;

namespace Game.Windows
{
    public sealed class WindowsModule : BaseModule, IWindowsModule, IWindowsModuleInternal, IGameStarted
    {
        private readonly IAssetService _assets;
        private readonly LocalInputService _input;
        private readonly IBroadcaster _clientBroadcaster;
        private readonly WindowsRecord _record;
        private readonly TextInputService _textInput;
        private readonly WindowStack<IWindowsModuleInternal> _windows;
        private readonly CancellationTokenSource _lifetime = new();
        private GameObject _root;
        private UiInputBinding _inputBinding;
        private IInstance<LoadingWindow> _loading;
        private IInstance<PauseWindow> _pause;
        private Action _cancelLoading;
        private Action _leaveRoom;
        private Action _disconnect;
        private int _pauseVersion;
        private bool _disposed;

        public bool HasOpenWindow => _windows.CurrentWindow;

        public WindowsModule(IAssetService assets, LocalInputService input, WindowsRecord record,
            IBroadcaster clientBroadcaster)
        {
            _assets = assets;
            _input = input;
            _record = record;
            _clientBroadcaster = clientBroadcaster;
            _textInput = new TextInputService(Broadcaster);
            _windows = new WindowStack<IWindowsModuleInternal>(PublishWindowChanged);
        }

        public override void Start()
        {
            _root = new GameObject("Windows");
            Object.DontDestroyOnLoad(_root);

            var eventSystem = EventSystem.current;
            if (!eventSystem)
            {
                var eventRoot = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventRoot.transform.SetParent(_root.transform, false);
                eventSystem = eventRoot.GetComponent<EventSystem>();
            }

            var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule)
            {
                _inputBinding = new UiInputBinding(inputModule, _input);
            }

            _textInput.Register(new UnityTouchKeyboardProvider(), -100);
            Broadcaster.Subscribe<TextInputSessionChanged>(OnTextInputChanged);
            Broadcaster.Subscribe<TextInputProviderFailed>(OnTextInputProviderFailed);
            HandleCancelAsync(_lifetime.Token).SuppressCancellationThrow().Forget(Debug.LogException);
        }

        async UniTask IGameStarted.OnGameStarted()
        {
            await GetLoadingWindowAsync();
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Broadcaster.Unsubscribe<TextInputSessionChanged>(OnTextInputChanged);
            Broadcaster.Unsubscribe<TextInputProviderFailed>(OnTextInputProviderFailed);
            _cancelLoading = null;
            _leaveRoom = null;
            _disconnect = null;

            var loading = _loading;
            var pause = _pause;
            _loading = null;
            _pause = null;

            List<Exception> errors = null;
            RunCleanup(_lifetime.Cancel);
            RunCleanup(_windows.CloseAll);
            RunCleanup(() => DestroyView(pause));
            RunCleanup(() => DestroyView(loading));
            RunCleanup(() => _inputBinding?.Dispose());
            RunCleanup(_textInput.Dispose);
            RunCleanup(_lifetime.Dispose);
            if (_root)
            {
                Object.Destroy(_root);
            }

            if (errors != null)
            {
                throw new AggregateException(errors).Flatten();
            }

            void RunCleanup(Action cleanup)
            {
                try
                {
                    cleanup();
                }
                catch (Exception error)
                {
                    (errors ??= new()).Add(error);
                }
            }
        }

        public async UniTask OpenLoadingAsync(Action cancel, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var loading = await GetLoadingWindowAsync().AttachExternalCancellation(token);
            token.ThrowIfCancellationRequested();
            _cancelLoading = cancel;
            try
            {
                _windows.Open(loading, isDisplacing: true);
            }
            catch
            {
                _cancelLoading = null;
                throw;
            }
        }

        public void CloseLoading()
        {
            _cancelLoading = null;
            if (_loading != null)
            {
                _windows.Close(_loading.Value);
            }
        }

        public async UniTask OpenPauseAsync(Action leave, Action disconnect, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(_record.PauseWindowId))
            {
                return;
            }

            var version = ++_pauseVersion;
            if (_pause == null)
            {
                var instance = await CreateViewAsync<PauseWindow>(_record.PauseWindowId);
                if (_pause != null)
                {
                    DestroyView(instance);
                }
                else
                {
                    _pause = instance;
                }
            }

            token.ThrowIfCancellationRequested();
            if (version != _pauseVersion)
            {
                return;
            }

            _leaveRoom = leave;
            _disconnect = disconnect;
            try
            {
                _windows.Open(_pause.Value, isDisplacing: false);
            }
            catch
            {
                _leaveRoom = null;
                _disconnect = null;
                throw;
            }
        }

        public void ClosePause()
        {
            _pauseVersion++;
            _leaveRoom = null;
            _disconnect = null;
            if (_pause != null)
            {
                _windows.Close(_pause.Value);
            }
        }

        void IWindowsModuleInternal.CancelLoading() => _cancelLoading?.Invoke();
        void IWindowsModuleInternal.LeaveRoom() => _leaveRoom?.Invoke();
        void IWindowsModuleInternal.Disconnect() => _disconnect?.Invoke();

        private async UniTask<LoadingWindow> GetLoadingWindowAsync()
        {
            if (_loading == null)
            {
                var instance = await CreateViewAsync<LoadingWindow>(_record.LoadingWindowId);
                if (_loading != null)
                {
                    DestroyView(instance);
                }
                else
                {
                    _loading = instance;
                }
            }

            return _loading.Value;
        }

        private async UniTask<IInstance<TWindow>> CreateViewAsync<TWindow>(string id)
            where TWindow : WindowBase<IWindowsModuleInternal>
        {
            var token = _lifetime.Token;
            var view = await _assets.Instantiate<TWindow>(id);
            var instance = new DefaultInstance<TWindow>(id, view);
            try
            {
                token.ThrowIfCancellationRequested();
                view.gameObject.SetActive(false);
                view.transform.SetParent(_root.transform, false);
                foreach (var field in view.GetComponentsInChildren<GamepadInputField>(true))
                {
                    field.Configure(_textInput, _input, view.gameObject);
                }

                view.Init(this, _assets);
                token.ThrowIfCancellationRequested();
                return instance;
            }
            catch
            {
                DestroyView(instance);
                throw;
            }
        }

        private void DestroyView<TWindow>(IInstance<TWindow> instance) where TWindow : WindowBase<IWindowsModuleInternal>
        {
            if (instance == null)
            {
                return;
            }

            try
            {
                if (instance.Value)
                {
                    instance.Value.Dispose();
                }
            }
            finally
            {
                _assets.Destroy(instance);
            }
        }

        private async UniTask HandleCancelAsync(CancellationToken token)
        {
            while (true)
            {
                await UniTask.NextFrame(cancellationToken: token);
                if (!_input.WasPressed(GameInputAction.Cancel))
                {
                    continue;
                }

                if (_textInput.IsOpen)
                {
                    _textInput.Cancel();
                }
                else
                {
                    _windows.CurrentWindow?.Cancel();
                }
            }
        }

        private void PublishWindowChanged()
        {
            _input.ClearTransientInput();
            _clientBroadcaster.Broadcast(new WindowChanged(HasOpenWindow));
        }

        private void OnTextInputChanged(TextInputSessionChanged _) => _input.ClearTransientInput();

        private static void OnTextInputProviderFailed(TextInputProviderFailed message) =>
            Debug.LogException(message.Error);
    }
}
