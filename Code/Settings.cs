using System.Collections.Generic;
using ModSettings;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.UI;

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

        private static readonly Dictionary<string, string> DefaultHotkeys = new Dictionary<string, string>
        {
            { nameof(SpeakFocus), "F6" },
            { nameof(ReadScreen), "F7" },
            { nameof(ExitReview), "Escape" },
            { nameof(NextItem), "F8" },
            { nameof(PreviousItem), "F9" },
            { nameof(ReadAll), "F10" },
            { nameof(Activate), "Return" },
            { nameof(RepeatLast), "F11" },
            { nameof(StopSpeaking), "F12" },
            { nameof(ToggleFocusAutoSpeak), "F5" }
        };

        private static readonly HashSet<string> LoggedInvalidHotkeys = new HashSet<string>();
        private readonly Dictionary<string, HotkeyCacheEntry> hotkeyCache = new Dictionary<string, HotkeyCacheEntry>();

        [Name("Hotkey: Speak focus")]
        [Description("Speak the currently focused UI element.")]
        public string SpeakFocus = "F6";

        [Name("Hotkey: Read screen")]
        [Description("Begin screen review for visible UI elements.")]
        public string ReadScreen = "F7";

        [Name("Hotkey: Exit screen review")]
        [Description("Exit screen review and return to normal focus mode.")]
        public string ExitReview = "Escape";

        [Name("Hotkey: Next item")]
        [Description("Move to the next screen review item.")]
        public string NextItem = "F8";

        [Name("Hotkey: Previous item")]
        [Description("Move to the previous screen review item.")]
        public string PreviousItem = "F9";

        [Name("Hotkey: Read all")]
        [Description("Read all items in screen review order.")]
        public string ReadAll = "F10";

        [Name("Hotkey: Activate")]
        [Description("Activate the currently focused UI element.")]
        public string Activate = "Return";

        [Name("Hotkey: Repeat last")]
        [Description("Repeat the last spoken line.")]
        public string RepeatLast = "F11";

        [Name("Hotkey: Stop speaking")]
        [Description("Stop current speech output.")]
        public string StopSpeaking = "F12";

        [Name("Hotkey: Toggle focus auto-speak")]
        [Description("Toggle automatic speaking when focus changes.")]
        public string ToggleFocusAutoSpeak = "F5";

        internal HotkeyBinding SpeakFocusBinding => GetHotkeyBinding(nameof(SpeakFocus), SpeakFocus);
        internal HotkeyBinding ReadScreenBinding => GetHotkeyBinding(nameof(ReadScreen), ReadScreen);
        internal HotkeyBinding ExitReviewBinding => GetHotkeyBinding(nameof(ExitReview), ExitReview);
        internal HotkeyBinding NextItemBinding => GetHotkeyBinding(nameof(NextItem), NextItem);
        internal HotkeyBinding PreviousItemBinding => GetHotkeyBinding(nameof(PreviousItem), PreviousItem);
        internal HotkeyBinding ReadAllBinding => GetHotkeyBinding(nameof(ReadAll), ReadAll);
        internal HotkeyBinding ActivateBinding => GetHotkeyBinding(nameof(Activate), Activate);
        internal HotkeyBinding RepeatLastBinding => GetHotkeyBinding(nameof(RepeatLast), RepeatLast);
        internal HotkeyBinding StopSpeakingBinding => GetHotkeyBinding(nameof(StopSpeaking), StopSpeaking);
        internal HotkeyBinding ToggleFocusAutoSpeakBinding => GetHotkeyBinding(nameof(ToggleFocusAutoSpeak), ToggleFocusAutoSpeak);

        private HotkeyBinding GetHotkeyBinding(string settingName, string value)
        {
            if (hotkeyCache.TryGetValue(settingName, out HotkeyCacheEntry cache) && cache.Value == value)
            {
                return cache.Binding;
            }

            if (!HotkeyUtil.TryParse(value, out HotkeyBinding binding))
            {
                string fallback = DefaultHotkeys[settingName];
                if (!HotkeyUtil.TryParse(fallback, out binding))
                {
                    binding = HotkeyBinding.None;
                }

                if (LoggedInvalidHotkeys.Add(settingName))
                {
                    string displayValue = string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
                    A11yLogger.Warning($"Hotkey setting '{settingName}' is invalid ('{displayValue}'). Falling back to '{fallback}'.");
                }
            }

            hotkeyCache[settingName] = new HotkeyCacheEntry(value, binding);
            return binding;
        }

        private sealed class HotkeyCacheEntry
        {
            public HotkeyCacheEntry(string value, HotkeyBinding binding)
            {
                Value = value;
                Binding = binding;
            }

            public string Value { get; }

            public HotkeyBinding Binding { get; }
        }
    }
}
