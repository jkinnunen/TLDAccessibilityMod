using ModSettings;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class HotkeyUtil
    {
        // ModSettings keybinds store a primary KeyCode plus Ctrl/Alt/Shift modifiers.
        // Match exact modifier state to avoid conflicts with other bindings.
        public static bool IsPressed(Keybind keybind, bool allowRepeat = false)
        {
            if (keybind == null)
            {
                return false;
            }

            if (keybind.Key == KeyCode.None)
            {
                return false;
            }

            bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (keybind.Ctrl != ctrlDown || keybind.Alt != altDown || keybind.Shift != shiftDown)
            {
                return false;
            }

            return allowRepeat ? Input.GetKey(keybind.Key) : Input.GetKeyDown(keybind.Key);
        }
    }
}
