using HarmonyLib;
using MelonLoader;
using TLDAccessibility.A11y.Capture;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using TLDAccessibility.A11y.UI;
using UnityEngine;

namespace TLDAccessibility
{
    internal class Implementation : MelonMod
    {
        private A11ySpeechService speechService;
        private FocusTracker focusTracker;
        private ScreenReviewController screenReview;
        private HarmonyLib.Harmony harmony;
        private bool loggedSettingsUnavailable;

        public override void OnInitializeMelon()
        {
            Settings.instance.AddToModSettings(BuildInfo.Name);
            A11yLogger.Initialize(LoggerInstance);
            LoggerInstance.Msg($"Version {BuildInfo.Version}");

            speechService = new A11ySpeechService();
            focusTracker = new FocusTracker(speechService);
            screenReview = new ScreenReviewController(speechService);
            TextChangeHandler.SpeechService = speechService;

            harmony = new HarmonyLib.Harmony("TLDAccessibility.A11y");
            FocusTracker.ApplyHarmonyPatches(harmony, focusTracker);
            TextChangePatches.Apply(harmony);

            A11yLogger.Info("Accessibility layer initialized.");
        }

        public override void OnUpdate()
        {
            speechService?.Update();
            focusTracker?.Update();
            TmpTextPolling.Update();

            HandleHotkeys();
        }

        private void HandleHotkeys()
        {
            Settings settings = Settings.instance;
            if (settings == null)
            {
                if (!loggedSettingsUnavailable)
                {
                    loggedSettingsUnavailable = true;
                    A11yLogger.Warning("Hotkeys disabled because settings failed to initialize.");
                }

                return;
            }

            if (HotkeyUtil.IsPressed(settings.SpeakFocusBinding))
            {
                SpeakFocusNow();
            }

            if (HotkeyUtil.IsPressed(settings.ReadScreenBinding))
            {
                screenReview.EnterOrRefresh();
            }

            if (HotkeyUtil.IsPressed(settings.ExitReviewBinding))
            {
                screenReview.Exit();
            }

            if (HotkeyUtil.IsPressed(settings.NextItemBinding))
            {
                screenReview.Next();
            }

            if (HotkeyUtil.IsPressed(settings.PreviousItemBinding))
            {
                screenReview.Previous();
            }

            if (HotkeyUtil.IsPressed(settings.ReadAllBinding))
            {
                screenReview.ReadAll();
            }

            if (HotkeyUtil.IsPressed(settings.ActivateBinding))
            {
                ActivateCurrent();
            }

            if (HotkeyUtil.IsPressed(settings.RepeatLastBinding))
            {
                speechService?.RepeatLast();
            }

            if (HotkeyUtil.IsPressed(settings.StopSpeakingBinding))
            {
                speechService?.Stop();
            }

            if (HotkeyUtil.IsPressed(settings.ToggleFocusAutoSpeakBinding))
            {
                settings.AutoSpeakFocusChanges = !settings.AutoSpeakFocusChanges;
                string state = settings.AutoSpeakFocusChanges ? "on" : "off";
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
