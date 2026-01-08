using HarmonyLib;
using MelonLoader;
using TLDAccessibility.A11y.Capture;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using TLDAccessibility.A11y.UI;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TLDAccessibility
{
    internal class Implementation : MelonMod
    {
        private A11ySpeechService speechService;
        private FocusTracker focusTracker;
        private ScreenReviewController screenReview;
        private HarmonyLib.Harmony harmony;
        private bool loggedSettingsUnavailable;
        private Settings settings;

        public override void OnInitializeMelon()
        {
            A11yLogger.Initialize(LoggerInstance);
            LoggerInstance.Msg($"Version {BuildInfo.Version}");
            settings = Settings.Initialize();

            speechService = new A11ySpeechService();
            focusTracker = new FocusTracker(speechService);
            screenReview = new ScreenReviewController(speechService);
            TextChangeHandler.SpeechService = speechService;

            harmony = new HarmonyLib.Harmony("TLDAccessibility.A11y");
            FocusTracker.ApplyHarmonyPatches(harmony, focusTracker);
            TextChangePatches.Apply(harmony);

            A11yLogger.Info("Accessibility layer initialized.");
            A11yLogger.Info("Startup speech test: requesting output.");
            speechService.Speak("TLDAccessibility loaded. Speech test.", A11ySpeechPriority.Critical, "startup_test", false);
        }

        public override void OnUpdate()
        {
            speechService?.Update();
            focusTracker?.Update();
            TmpTextPolling.Update();

            HandleDebugHotkey();
            HandleHotkeys();
        }

        private void HandleDebugHotkey()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && alt && shift && Input.GetKeyDown(KeyCode.F11))
            {
                HandleSelectionSnapshotHotkey();
                return;
            }

            if (ctrl && alt && shift && Input.GetKeyDown(KeyCode.F12))
            {
                A11yLogger.Info("Debug speech hotkey pressed.");
                speechService?.Speak("TLDAccessibility debug hotkey speech test.", A11ySpeechPriority.Critical, "debug_hotkey", false);
            }
        }

        private void HandleSelectionSnapshotHotkey()
        {
            A11yLogger.Info("Selection snapshot hotkey pressed.");
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                A11yLogger.Info("Selection snapshot: EventSystem is null.");
                speechService?.Speak("EventSystem is null", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
                return;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected == null)
            {
                A11yLogger.Info("Selection snapshot: No selected UI object.");
                speechService?.Speak("No selected UI object", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
                return;
            }

            string narration = focusTracker?.BuildUiSelectionNarration(selected);
            if (string.IsNullOrWhiteSpace(narration))
            {
                narration = selected.name;
            }

            speechService?.Speak($"Selected: {narration}", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
            string details = BuildSelectionSnapshotDetails(selected, narration);
            A11yLogger.Info(details);
        }

        private static string BuildSelectionSnapshotDetails(GameObject selected, string narration)
        {
            StringBuilder builder = new StringBuilder();
            string sceneName = selected.scene.IsValid() ? selected.scene.name : "(unknown)";
            builder.Append("Selection snapshot: ");
            builder.Append($"Name={selected.name}; ");
            builder.Append($"ActiveInHierarchy={selected.activeInHierarchy}; ");
            builder.Append($"Scene={sceneName}; ");

            Component[] components = selected.GetComponents<Component>();
            int limit = Mathf.Min(components.Length, 8);
            builder.Append("Components=[");
            int appended = 0;
            for (int i = 0; i < limit; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (appended > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(component.GetType().Name);
                appended++;
            }

            if (components.Length > limit)
            {
                builder.Append(", ...");
            }

            builder.Append("]; ");

            Component tmpText = TmpReflection.GetTmpTextComponent(selected);
            if (tmpText != null)
            {
                string text = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(tmpText));
                text = TrimSnapshotText(text);
                builder.Append($"TMP_Text=\"{text}\"; ");
            }

            builder.Append($"Narration=\"{narration}\"");
            return builder.ToString();
        }

        private static string TrimSnapshotText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            const int maxLength = 120;
            if (text.Length <= maxLength)
            {
                return text;
            }

            return $"{text.Substring(0, maxLength)}...";
        }

        private void HandleHotkeys()
        {
            Settings activeSettings = settings ?? Settings.Instance;
            if (activeSettings == null)
            {
                if (!loggedSettingsUnavailable)
                {
                    loggedSettingsUnavailable = true;
                    A11yLogger.Warning("Hotkeys disabled because settings failed to initialize.");
                }

                return;
            }

            if (HotkeyUtil.IsPressed(activeSettings.SpeakFocusBinding))
            {
                SpeakFocusNow();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ReadScreenBinding))
            {
                screenReview.EnterOrRefresh();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ExitReviewBinding))
            {
                screenReview.Exit();
            }

            if (HotkeyUtil.IsPressed(activeSettings.NextItemBinding))
            {
                screenReview.Next();
            }

            if (HotkeyUtil.IsPressed(activeSettings.PreviousItemBinding))
            {
                screenReview.Previous();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ReadAllBinding))
            {
                screenReview.ReadAll();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ActivateBinding))
            {
                ActivateCurrent();
            }

            if (HotkeyUtil.IsPressed(activeSettings.RepeatLastBinding))
            {
                speechService?.RepeatLast();
            }

            if (HotkeyUtil.IsPressed(activeSettings.StopSpeakingBinding))
            {
                speechService?.Stop();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ToggleFocusAutoSpeakBinding))
            {
                activeSettings.AutoSpeakFocusChanges = !activeSettings.AutoSpeakFocusChanges;
                activeSettings.Save();
                string state = activeSettings.AutoSpeakFocusChanges ? "on" : "off";
                speechService?.Speak($"Focus auto speak {state}", A11ySpeechPriority.Normal, "toggle_focus", false);
            }
        }

        private void SpeakFocusNow()
        {
            if (focusTracker == null)
            {
                return;
            }

            string narration = focusTracker.BuildFocusNarration(focusTracker.GetCurrentFocus());
            if (!string.IsNullOrWhiteSpace(narration))
            {
                speechService?.Speak(narration, A11ySpeechPriority.Normal, "focus_manual", false);
            }
        }

        private void ActivateCurrent()
        {
            GameObject target = screenReview.InReviewMode ? screenReview.GetCurrentItem()?.Target : focusTracker?.GetCurrentFocus();
            if (target == null)
            {
                speechService?.Speak("Nothing to activate", A11ySpeechPriority.Normal, "activate", false);
                return;
            }

            bool activated = ActivationUtil.Activate(target);
            if (activated)
            {
                string name = AccessibleNameResolver.Resolve(target)?.Name;
                string label = string.IsNullOrWhiteSpace(name) ? "item" : name;
                speechService?.Speak($"Activated {label}", A11ySpeechPriority.Normal, "activate", false);
            }
            else
            {
                speechService?.Speak("Cannot activate", A11ySpeechPriority.Normal, "activate", false);
            }
        }
    }
}
