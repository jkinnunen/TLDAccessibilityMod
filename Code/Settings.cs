using ModSettings;
using TLDAccessibility.A11y.Model;
using UnityEngine;

namespace TLDAccessibility
{
    internal class Settings : JsonModSettings
    {
        internal static Settings instance = new Settings();

        [Name("Auto speak focus changes")]
        [Description("Speak when UI focus changes.")]
        public bool AutoSpeakFocusChanges = true;

        [Name("Auto speak text changes")]
        [Description("Speak visible text changes (may be chatty).")]
        public bool AutoSpeakTextChanges = false;

        [Name("Suppress numeric auto speech")]
        [Description("Suppress auto speaking numeric-only text changes.")]
        public bool SuppressNumericAutoSpeech = true;

        [Name("Cooldown seconds")]
        [Description("Minimum seconds before repeating the same text.")]
        public float CooldownSeconds = 1.0f;

        [Name("Min interval seconds")]
        [Description("Minimum interval between any spoken phrases.")]
        public float MinIntervalSeconds = 0.2f;

        [Name("Max auto speech per second")]
        [Description("Rate limit for auto narration.")]
        public int MaxAutoPerSecond = 3;

        [Name("Verbosity")]
        [Description("Detail level for spoken content.")]
        public VerbosityLevel Verbosity = VerbosityLevel.Normal;

        [Name("Allow unknown NGUI visibility in snapshot")]
        [Description("Include NGUI labels in screen review even when visibility is uncertain.")]
        public bool AllowUnknownNguiInSnapshot = true;

        [Name("Hotkey: Speak focus")]
        public Keybind SpeakFocus = new Keybind(KeyCode.F6);

        [Name("Hotkey: Read screen")]
        public Keybind ReadScreen = new Keybind(KeyCode.F7);

        [Name("Hotkey: Exit screen review")]
        public Keybind ExitReview = new Keybind(KeyCode.Escape);

        [Name("Hotkey: Next item")]
        public Keybind NextItem = new Keybind(KeyCode.F8);

        [Name("Hotkey: Previous item")]
        public Keybind PreviousItem = new Keybind(KeyCode.F9);

        [Name("Hotkey: Read all")]
        public Keybind ReadAll = new Keybind(KeyCode.F10);

        [Name("Hotkey: Activate")]
        public Keybind Activate = new Keybind(KeyCode.Return);

        [Name("Hotkey: Repeat last")]
        public Keybind RepeatLast = new Keybind(KeyCode.F11);

        [Name("Hotkey: Stop speaking")]
        public Keybind StopSpeaking = new Keybind(KeyCode.F12);

        [Name("Hotkey: Toggle focus auto-speak")]
        public Keybind ToggleFocusAutoSpeak = new Keybind(KeyCode.F5);
    }
}
