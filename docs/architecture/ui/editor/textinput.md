# TextInput

Source folder: `Assets/Scripts/Core/UI/Editor/TextInput`. Files: EditorTextInputProvider.cs, EditorTextInputWindow.cs, EditorTextInputWindow.uxml, EditorTextInputWindow.uss.

Responsibility: [Coop sessions](../../coop-sessions.md).

## Purpose

The editor registers a provider factory for `TextInputService`. Each window module owns its text input service during Play Mode. Its utility window supports Unicode text, keyboard confirmation, and cancellation. Provider registration releases when Play Mode ends.
