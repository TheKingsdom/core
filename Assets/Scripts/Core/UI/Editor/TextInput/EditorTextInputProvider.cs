using System;
using Core.Input.TextInput;
using UnityEditor;
using UnityEngine;

namespace Core.UI.Editor.TextInput
{
    public sealed class EditorTextInputProvider : ITextInputProvider
    {
        public bool IsAvailable(TextInputRequest request) => EditorApplication.isPlaying && !EditorApplication.isCompiling;

        public IDisposable Open(TextInputRequest request, Action<TextInputResult> completed)
        {
            var window = ScriptableObject.CreateInstance<EditorTextInputWindow>();
            window.Begin(request, completed);
            return window;
        }
    }

    [InitializeOnLoad]
    internal static class EditorTextInputInstaller
    {
        static EditorTextInputInstaller()
        {
            TextInputService.EditorProviderFactory = () => new EditorTextInputProvider();
        }
    }
}
