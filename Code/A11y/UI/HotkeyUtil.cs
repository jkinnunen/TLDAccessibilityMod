using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal readonly struct HotkeyBinding
    {
        public HotkeyBinding(KeyCode key, bool ctrl, bool alt, bool shift)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        public KeyCode Key { get; }

        public bool Ctrl { get; }

        public bool Alt { get; }

        public bool Shift { get; }

        public static HotkeyBinding None => new HotkeyBinding(KeyCode.None, false, false, false);
    }

    internal static class HotkeyUtil
    {
        // Match exact modifier state to avoid conflicts with other bindings.
        public static bool IsPressed(HotkeyBinding hotkey, bool allowRepeat = false)
        {
            if (hotkey.Key == KeyCode.None)
            {
                return false;
            }

            bool ctrlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shiftDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (hotkey.Ctrl != ctrlDown || hotkey.Alt != altDown || hotkey.Shift != shiftDown)
            {
                return false;
            }

            return allowRepeat ? Input.GetKey(hotkey.Key) : Input.GetKeyDown(hotkey.Key);
        }

        public static bool TryParse(string value, out HotkeyBinding binding)
        {
            binding = HotkeyBinding.None;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            bool ctrl = false;
            bool alt = false;
            bool shift = false;
            KeyCode key = KeyCode.None;

            string[] parts = value.Split('+');
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (part.Equals("Ctrl", System.StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", System.StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                    continue;
                }

                if (part.Equals("Alt", System.StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                    continue;
                }

                if (part.Equals("Shift", System.StringComparison.OrdinalIgnoreCase))
                {
                    shift = true;
                    continue;
                }

                if (key != KeyCode.None)
                {
                    return false;
                }

                if (!System.Enum.TryParse(part, true, out key))
                {
                    return false;
                }
            }

            binding = new HotkeyBinding(key, ctrl, alt, shift);
            return true;
        }
    }
}
