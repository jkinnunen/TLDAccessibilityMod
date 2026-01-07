using System;
using ModSettings;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class HotkeyUtil
    {
        public static bool IsPressed(Keybind keybind)
        {
            if (keybind == null)
            {
                return false;
            }

            try
            {
                KeyCode key = ReadKey(keybind);
                bool ctrl = ReadModifier(keybind, "Ctrl") || ReadModifier(keybind, "Control");
                bool alt = ReadModifier(keybind, "Alt");
                bool shift = ReadModifier(keybind, "Shift");
                if (ctrl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                {
                    return false;
                }

                if (alt && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                {
                    return false;
                }

                if (shift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                {
                    return false;
                }

                return Input.GetKeyDown(key);
            }
            catch (Exception)
            {
                return Input.GetKeyDown(ReadKey(keybind));
            }
        }

        private static KeyCode ReadKey(Keybind keybind)
        {
            var field = keybind.GetType().GetField("Key");
            if (field != null && field.GetValue(keybind) is KeyCode keyCode)
            {
                return keyCode;
            }

            var property = keybind.GetType().GetProperty("Key");
            if (property != null && property.GetValue(keybind) is KeyCode keyCodeProp)
            {
                return keyCodeProp;
            }

            return KeyCode.None;
        }

        private static bool ReadModifier(Keybind keybind, string name)
        {
            var field = keybind.GetType().GetField(name);
            if (field != null && field.GetValue(keybind) is bool fieldValue)
            {
                return fieldValue;
            }

            var property = keybind.GetType().GetProperty(name);
            if (property != null && property.GetValue(keybind) is bool propertyValue)
            {
                return propertyValue;
            }

            return false;
        }
    }
}
