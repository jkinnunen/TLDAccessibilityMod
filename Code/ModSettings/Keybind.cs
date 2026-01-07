using System;
using UnityEngine;

namespace ModSettings
{
    [Serializable]
    public sealed class Keybind
    {
        public KeyCode Key;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;

        public Keybind()
        {
            Key = KeyCode.None;
        }

        public Keybind(KeyCode key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }
    }
}
