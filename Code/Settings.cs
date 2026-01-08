using System;
using System.Collections.Generic;
using MelonLoader;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.UI;

namespace TLDAccessibility
{
    internal sealed class Settings
    {
        private const string CategoryName = "TLDAccessibility";

        internal static Settings Instance => EnsureInitialized();

        private static Settings instance;
        private static Settings fallback;
        private static bool initialized;
        private static bool loggedInitFailure;
        private static bool loggedSaveFailure;

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

        private MelonPreferences_Category category;

        private MelonPreferences_Entry<bool> autoSpeakFocusChangesEntry;
        private MelonPreferences_Entry<bool> autoSpeakTextChangesEntry;
        private MelonPreferences_Entry<bool> suppressNumericAutoSpeechEntry;
        private MelonPreferences_Entry<float> cooldownSecondsEntry;
        private MelonPreferences_Entry<float> minIntervalSecondsEntry;
        private MelonPreferences_Entry<int> maxAutoPerSecondEntry;
        private MelonPreferences_Entry<int> verbosityEntry;
        private MelonPreferences_Entry<bool> allowUnknownNguiInSnapshotEntry;
        private MelonPreferences_Entry<string> speakFocusEntry;
        private MelonPreferences_Entry<string> readScreenEntry;
        private MelonPreferences_Entry<string> exitReviewEntry;
        private MelonPreferences_Entry<string> nextItemEntry;
        private MelonPreferences_Entry<string> previousItemEntry;
        private MelonPreferences_Entry<string> readAllEntry;
        private MelonPreferences_Entry<string> activateEntry;
        private MelonPreferences_Entry<string> repeatLastEntry;
        private MelonPreferences_Entry<string> stopSpeakingEntry;
        private MelonPreferences_Entry<string> toggleFocusAutoSpeakEntry;

        private bool autoSpeakFocusChanges = true;
        private bool autoSpeakTextChanges;
        private bool suppressNumericAutoSpeech = true;
        private float cooldownSeconds = 1.0f;
        private float minIntervalSeconds = 0.2f;
        private int maxAutoPerSecond = 3;
        private int verbosity = (int)VerbosityLevel.Normal;
        private bool allowUnknownNguiInSnapshot = true;
        private string speakFocus = "F6";
        private string readScreen = "F7";
        private string exitReview = "Escape";
        private string nextItem = "F8";
        private string previousItem = "F9";
        private string readAll = "F10";
        private string activate = "Return";
        private string repeatLast = "F11";
        private string stopSpeaking = "F12";
        private string toggleFocusAutoSpeak = "F5";

        internal static Settings Initialize()
        {
            if (initialized)
            {
                return instance ?? GetFallback();
            }

            initialized = true;

            try
            {
                instance = new Settings(usePreferences: true);
            }
            catch (Exception ex)
            {
                if (!loggedInitFailure)
                {
                    loggedInitFailure = true;
                    A11yLogger.Error($"Settings failed to initialize. Using defaults. {ex}");
                }

                instance = GetFallback();
            }

            return instance ?? GetFallback();
        }

        public void Save()
        {
            if (category == null)
            {
                return;
            }

            try
            {
                MelonPreferences.Save();
            }
            catch (Exception ex)
            {
                if (!loggedSaveFailure)
                {
                    loggedSaveFailure = true;
                    A11yLogger.Warning($"Failed to save settings. {ex}");
                }
            }
        }

        public bool AutoSpeakFocusChanges
        {
            get => autoSpeakFocusChangesEntry?.Value ?? autoSpeakFocusChanges;
            set
            {
                autoSpeakFocusChanges = value;
                if (autoSpeakFocusChangesEntry != null)
                {
                    autoSpeakFocusChangesEntry.Value = value;
                }
            }
        }

        public bool AutoSpeakTextChanges
        {
            get => autoSpeakTextChangesEntry?.Value ?? autoSpeakTextChanges;
            set
            {
                autoSpeakTextChanges = value;
                if (autoSpeakTextChangesEntry != null)
                {
                    autoSpeakTextChangesEntry.Value = value;
                }
            }
        }

        public bool SuppressNumericAutoSpeech
        {
            get => suppressNumericAutoSpeechEntry?.Value ?? suppressNumericAutoSpeech;
            set
            {
                suppressNumericAutoSpeech = value;
                if (suppressNumericAutoSpeechEntry != null)
                {
                    suppressNumericAutoSpeechEntry.Value = value;
                }
            }
        }

        public float CooldownSeconds
        {
            get => cooldownSecondsEntry?.Value ?? cooldownSeconds;
            set
            {
                cooldownSeconds = value;
                if (cooldownSecondsEntry != null)
                {
                    cooldownSecondsEntry.Value = value;
                }
            }
        }

        public float MinIntervalSeconds
        {
            get => minIntervalSecondsEntry?.Value ?? minIntervalSeconds;
            set
            {
                minIntervalSeconds = value;
                if (minIntervalSecondsEntry != null)
                {
                    minIntervalSecondsEntry.Value = value;
                }
            }
        }

        public int MaxAutoPerSecond
        {
            get => maxAutoPerSecondEntry?.Value ?? maxAutoPerSecond;
            set
            {
                maxAutoPerSecond = value;
                if (maxAutoPerSecondEntry != null)
                {
                    maxAutoPerSecondEntry.Value = value;
                }
            }
        }

        public VerbosityLevel Verbosity
        {
            get
            {
                int value = verbosityEntry?.Value ?? verbosity;
                return Enum.IsDefined(typeof(VerbosityLevel), value) ? (VerbosityLevel)value : VerbosityLevel.Normal;
            }
            set
            {
                int intValue = (int)value;
                verbosity = intValue;
                if (verbosityEntry != null)
                {
                    verbosityEntry.Value = intValue;
                }
            }
        }

        public bool AllowUnknownNguiInSnapshot
        {
            get => allowUnknownNguiInSnapshotEntry?.Value ?? allowUnknownNguiInSnapshot;
            set
            {
                allowUnknownNguiInSnapshot = value;
                if (allowUnknownNguiInSnapshotEntry != null)
                {
                    allowUnknownNguiInSnapshotEntry.Value = value;
                }
            }
        }

        public string SpeakFocus
        {
            get => speakFocusEntry?.Value ?? speakFocus;
            set
            {
                speakFocus = value;
                if (speakFocusEntry != null)
                {
                    speakFocusEntry.Value = value;
                }
            }
        }

        public string ReadScreen
        {
            get => readScreenEntry?.Value ?? readScreen;
            set
            {
                readScreen = value;
                if (readScreenEntry != null)
                {
                    readScreenEntry.Value = value;
                }
            }
        }

        public string ExitReview
        {
            get => exitReviewEntry?.Value ?? exitReview;
            set
            {
                exitReview = value;
                if (exitReviewEntry != null)
                {
                    exitReviewEntry.Value = value;
                }
            }
        }

        public string NextItem
        {
            get => nextItemEntry?.Value ?? nextItem;
            set
            {
                nextItem = value;
                if (nextItemEntry != null)
                {
                    nextItemEntry.Value = value;
                }
            }
        }

        public string PreviousItem
        {
            get => previousItemEntry?.Value ?? previousItem;
            set
            {
                previousItem = value;
                if (previousItemEntry != null)
                {
                    previousItemEntry.Value = value;
                }
            }
        }

        public string ReadAll
        {
            get => readAllEntry?.Value ?? readAll;
            set
            {
                readAll = value;
                if (readAllEntry != null)
                {
                    readAllEntry.Value = value;
                }
            }
        }

        public string Activate
        {
            get => activateEntry?.Value ?? activate;
            set
            {
                activate = value;
                if (activateEntry != null)
                {
                    activateEntry.Value = value;
                }
            }
        }

        public string RepeatLast
        {
            get => repeatLastEntry?.Value ?? repeatLast;
            set
            {
                repeatLast = value;
                if (repeatLastEntry != null)
                {
                    repeatLastEntry.Value = value;
                }
            }
        }

        public string StopSpeaking
        {
            get => stopSpeakingEntry?.Value ?? stopSpeaking;
            set
            {
                stopSpeaking = value;
                if (stopSpeakingEntry != null)
                {
                    stopSpeakingEntry.Value = value;
                }
            }
        }

        public string ToggleFocusAutoSpeak
        {
            get => toggleFocusAutoSpeakEntry?.Value ?? toggleFocusAutoSpeak;
            set
            {
                toggleFocusAutoSpeak = value;
                if (toggleFocusAutoSpeakEntry != null)
                {
                    toggleFocusAutoSpeakEntry.Value = value;
                }
            }
        }

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

        private static Settings EnsureInitialized()
        {
            if (instance != null)
            {
                return instance;
            }

            return Initialize();
        }

        private static Settings GetFallback()
        {
            fallback ??= new Settings(usePreferences: false);
            return fallback;
        }

        private Settings(bool usePreferences)
        {
            if (usePreferences)
            {
                InitializePreferences();
            }
        }

        private void InitializePreferences()
        {
            category = MelonPreferences.CreateCategory(CategoryName);

            autoSpeakFocusChangesEntry = category.CreateEntry(nameof(AutoSpeakFocusChanges), autoSpeakFocusChanges);
            autoSpeakTextChangesEntry = category.CreateEntry(nameof(AutoSpeakTextChanges), autoSpeakTextChanges);
            suppressNumericAutoSpeechEntry = category.CreateEntry(nameof(SuppressNumericAutoSpeech), suppressNumericAutoSpeech);
            cooldownSecondsEntry = category.CreateEntry(nameof(CooldownSeconds), cooldownSeconds);
            minIntervalSecondsEntry = category.CreateEntry(nameof(MinIntervalSeconds), minIntervalSeconds);
            maxAutoPerSecondEntry = category.CreateEntry(nameof(MaxAutoPerSecond), maxAutoPerSecond);
            verbosityEntry = category.CreateEntry(nameof(Verbosity), verbosity);
            allowUnknownNguiInSnapshotEntry = category.CreateEntry(nameof(AllowUnknownNguiInSnapshot), allowUnknownNguiInSnapshot);

            speakFocusEntry = category.CreateEntry(nameof(SpeakFocus), speakFocus);
            readScreenEntry = category.CreateEntry(nameof(ReadScreen), readScreen);
            exitReviewEntry = category.CreateEntry(nameof(ExitReview), exitReview);
            nextItemEntry = category.CreateEntry(nameof(NextItem), nextItem);
            previousItemEntry = category.CreateEntry(nameof(PreviousItem), previousItem);
            readAllEntry = category.CreateEntry(nameof(ReadAll), readAll);
            activateEntry = category.CreateEntry(nameof(Activate), activate);
            repeatLastEntry = category.CreateEntry(nameof(RepeatLast), repeatLast);
            stopSpeakingEntry = category.CreateEntry(nameof(StopSpeaking), stopSpeaking);
            toggleFocusAutoSpeakEntry = category.CreateEntry(nameof(ToggleFocusAutoSpeak), toggleFocusAutoSpeak);

            autoSpeakFocusChanges = autoSpeakFocusChangesEntry.Value;
            autoSpeakTextChanges = autoSpeakTextChangesEntry.Value;
            suppressNumericAutoSpeech = suppressNumericAutoSpeechEntry.Value;
            cooldownSeconds = cooldownSecondsEntry.Value;
            minIntervalSeconds = minIntervalSecondsEntry.Value;
            maxAutoPerSecond = maxAutoPerSecondEntry.Value;
            verbosity = verbosityEntry.Value;
            allowUnknownNguiInSnapshot = allowUnknownNguiInSnapshotEntry.Value;

            speakFocus = speakFocusEntry.Value;
            readScreen = readScreenEntry.Value;
            exitReview = exitReviewEntry.Value;
            nextItem = nextItemEntry.Value;
            previousItem = previousItemEntry.Value;
            readAll = readAllEntry.Value;
            activate = activateEntry.Value;
            repeatLast = repeatLastEntry.Value;
            stopSpeaking = stopSpeakingEntry.Value;
            toggleFocusAutoSpeak = toggleFocusAutoSpeakEntry.Value;
        }

        private HotkeyBinding GetHotkeyBinding(string settingName, string value)
        {
            if (hotkeyCache.TryGetValue(settingName, out HotkeyCacheEntry cache) && cache.Value == value)
            {
                return cache.Binding;
            }

            if (!HotkeyUtil.TryParse(value, out HotkeyBinding binding))
            {
                string fallbackValue = DefaultHotkeys[settingName];
                if (!HotkeyUtil.TryParse(fallbackValue, out binding))
                {
                    binding = HotkeyBinding.None;
                }

                if (LoggedInvalidHotkeys.Add(settingName))
                {
                    string displayValue = string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
                    A11yLogger.Warning($"Hotkey setting '{settingName}' is invalid ('{displayValue}'). Falling back to '{fallbackValue}'.");
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
