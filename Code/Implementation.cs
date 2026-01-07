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
        private Harmony harmony;

        public override void OnApplicationStart()
        {
            Settings.instance.AddToModSettings(BuildInfo.Name);
            A11yLogger.Initialize(LoggerInstance);
            LoggerInstance.Msg($"Version {BuildInfo.Version}");

            speechService = new A11ySpeechService();
            focusTracker = new FocusTracker(speechService);
            screenReview = new ScreenReviewController(speechService);
            TextChangeHandler.SpeechService = speechService;

            harmony = new Harmony("TLDAccessibility.A11y");
            FocusTracker.ApplyHarmonyPatches(harmony, focusTracker);
            TextChangePatches.Apply(harmony);

            A11yLogger.Info("Accessibility layer initialized.");
        }

        public override void OnUpdate()
        {
            speechService?.Update();
            focusTracker?.Update();

            HandleHotkeys();
        }

        private void HandleHotkeys()
        {
            if (HotkeyUtil.IsPressed(Settings.instance.SpeakFocus))
            {
                SpeakFocusNow();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.ReadScreen))
            {
                screenReview.EnterOrRefresh();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.ExitReview))
            {
                screenReview.Exit();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.NextItem))
            {
                screenReview.Next();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.PreviousItem))
            {
                screenReview.Previous();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.ReadAll))
            {
                screenReview.ReadAll();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.Activate))
            {
                ActivateCurrent();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.RepeatLast))
            {
                speechService?.RepeatLast();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.StopSpeaking))
            {
                speechService?.Stop();
            }

            if (HotkeyUtil.IsPressed(Settings.instance.ToggleFocusAutoSpeak))
            {
                Settings.instance.AutoSpeakFocusChanges = !Settings.instance.AutoSpeakFocusChanges;
                string state = Settings.instance.AutoSpeakFocusChanges ? "on" : "off";
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
