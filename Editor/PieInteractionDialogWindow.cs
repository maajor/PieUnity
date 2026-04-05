using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Pie
{
    internal sealed class PieInteractionDialogWindow : EditorWindow
    {
        private enum Mode
        {
            SelectOne,
            TextInput,
            MultilineInput,
        }

        private Mode _mode;
        private string _prompt = "";
        private string[] _options = new string[0];
        private string _value = "";
        private int _selectedIndex = -1;
        private bool _submitted;
        private bool _cancelled;

        [System.Serializable]
        private class AutomationResponse
        {
            public string type;
            public string selection;
            public string value;
            public bool cancel;
        }

        [System.Serializable]
        private class InteractionStatePayload
        {
            public bool isOpen;
            public string type;
            public string prompt;
            public string[] options;
            public string value;
        }

        private static readonly Queue<AutomationResponse> QueuedResponses = new Queue<AutomationResponse>();
        private static PieInteractionDialogWindow _currentWindow;
        private static InteractionStatePayload _state = new InteractionStatePayload
        {
            isOpen = false,
            type = "",
            prompt = "",
            options = new string[0],
            value = "",
        };

        public static string GetStateJson()
        {
            return JsonUtility.ToJson(_state ?? new InteractionStatePayload());
        }

        public static string EnqueueAutomationResponse(string argsJson)
        {
            var response = JsonUtility.FromJson<AutomationResponse>(argsJson ?? "{}") ?? new AutomationResponse();
            QueuedResponses.Enqueue(response);
            if (_currentWindow != null)
                _currentWindow.Repaint();
            return GetStateJson();
        }

        public static string ClearAutomationResponses()
        {
            QueuedResponses.Clear();
            return GetStateJson();
        }

        public static string ShowSelectOne(string prompt, string[] options)
        {
            var window = CreateInstance<PieInteractionDialogWindow>();
            window.titleContent = new GUIContent("Pie Interaction");
            window.minSize = new Vector2(380, 220);
            window._mode = Mode.SelectOne;
            window._prompt = prompt ?? "Choose an option";
            window._options = options ?? new string[0];
            SetState(window._mode, window._prompt, window._options, "");
            if (window.TryApplyQueuedResponse(closeWindow: false))
            {
                ClearState();
                return window._cancelled || window._selectedIndex < 0 || window._selectedIndex >= window._options.Length
                    ? null
                    : window._options[window._selectedIndex];
            }
            _currentWindow = window;
            window.ShowModal();
            ClearState();
            _currentWindow = null;
            return window._submitted && window._selectedIndex >= 0 && window._selectedIndex < window._options.Length
                ? window._options[window._selectedIndex]
                : null;
        }

        public static string ShowTextInput(string prompt, string placeholder, bool multiline)
        {
            var window = CreateInstance<PieInteractionDialogWindow>();
            window.titleContent = new GUIContent("Pie Interaction");
            window.minSize = new Vector2(420, multiline ? 280 : 180);
            window._mode = multiline ? Mode.MultilineInput : Mode.TextInput;
            window._prompt = prompt ?? "Input";
            window._value = placeholder ?? "";
            SetState(window._mode, window._prompt, new string[0], window._value);
            if (window.TryApplyQueuedResponse(closeWindow: false))
            {
                ClearState();
                return window._submitted ? window._value : null;
            }
            _currentWindow = window;
            window.ShowModal();
            ClearState();
            _currentWindow = null;
            return window._submitted ? window._value : null;
        }

        private void OnGUI()
        {
            if (TryApplyQueuedResponse())
            {
                GUIUtility.ExitGUI();
                return;
            }

            EditorGUILayout.LabelField(_prompt, EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);

            switch (_mode)
            {
                case Mode.SelectOne:
                    DrawSelectOne();
                    break;
                case Mode.TextInput:
                    _value = EditorGUILayout.TextField(_value);
                    _state.value = _value;
                    break;
                case Mode.MultilineInput:
                    _value = EditorGUILayout.TextArea(_value, GUILayout.MinHeight(120));
                    _state.value = _value;
                    break;
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    _submitted = false;
                    _cancelled = true;
                    Close();
                }

                GUI.enabled = _mode != Mode.SelectOne || _selectedIndex >= 0;
                if (GUILayout.Button("OK"))
                {
                    _submitted = true;
                    _cancelled = false;
                    Close();
                }
                GUI.enabled = true;
            }
        }

        private void DrawSelectOne()
        {
            for (var i = 0; i < _options.Length; i++)
            {
                if (GUILayout.Toggle(_selectedIndex == i, _options[i], "Button"))
                {
                    _selectedIndex = i;
                }
            }
        }

        private static void SetState(Mode mode, string prompt, string[] options, string value)
        {
            _state = new InteractionStatePayload
            {
                isOpen = true,
                type = mode == Mode.SelectOne ? "select_one" : mode == Mode.MultilineInput ? "multiline_input" : "text_input",
                prompt = prompt ?? "",
                options = options ?? new string[0],
                value = value ?? "",
            };
        }

        private static void ClearState()
        {
            _state = new InteractionStatePayload
            {
                isOpen = false,
                type = "",
                prompt = "",
                options = new string[0],
                value = "",
            };
        }

        private bool TryApplyQueuedResponse(bool closeWindow = true)
        {
            if (QueuedResponses.Count == 0)
                return false;

            var response = QueuedResponses.Peek();
            var currentType = _mode == Mode.SelectOne ? "select_one" : _mode == Mode.MultilineInput ? "multiline_input" : "text_input";
            if (!string.IsNullOrEmpty(response.type) && !string.Equals(response.type, currentType, System.StringComparison.Ordinal))
                return false;

            QueuedResponses.Dequeue();
            if (response.cancel)
            {
                _submitted = false;
                _cancelled = true;
                if (closeWindow)
                    Close();
                return true;
            }

            if (_mode == Mode.SelectOne)
            {
                if (_options != null)
                    _selectedIndex = System.Array.IndexOf(_options, response.selection);
                _submitted = _selectedIndex >= 0;
                _cancelled = !_submitted;
            }
            else
            {
                _value = response.value ?? "";
                _state.value = _value;
                _submitted = true;
                _cancelled = false;
            }

            if (closeWindow)
                Close();
            return true;
        }
    }
}
