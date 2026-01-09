using System;
using HarmonyLib;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal sealed class FocusTracker
    {
        private readonly A11ySpeechService speechService;
        private GameObject lastFocused;
        private GameObject lastSelected;
        private GameObject lastNguiSelected;
        private float lastNguiSpeakTime;
        private const float NguiSpeakCooldownSeconds = 0.2f;

        public FocusTracker(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public void Update()
        {
            UpdateUnitySelection();
            UpdateNguiSelection();
        }

        public void HandleFocusChanged(GameObject current)
        {
            lastFocused = current;
            lastSelected = current;
            if (!Settings.Instance.AutoSpeakFocusChanges)
            {
                return;
            }

            string narration = BuildFocusNarration(current);
            if (!string.IsNullOrWhiteSpace(narration))
            {
                speechService.Speak(narration, A11ySpeechPriority.Normal, GetSourceId(current), false);
            }
        }

        public string BuildFocusNarration(GameObject current)
        {
            if (current == null)
            {
                return null;
            }

            AccessibleLabel label = AccessibleNameResolver.Resolve(current);
            return label?.ToSpokenString(true);
        }

        public string BuildUiSelectionNarration(GameObject current)
        {
            if (current == null)
            {
                return null;
            }

            AccessibleLabel label = AccessibleNameResolver.Resolve(current);
            if (label != null && !string.IsNullOrWhiteSpace(label.Name))
            {
                return label.ToSpokenString(true);
            }

            string fallback = ResolveSelectionText(current);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = current.name;
            }

            if (label != null && !string.IsNullOrWhiteSpace(label.Value))
            {
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    return label.Value;
                }

                if (!fallback.Contains(label.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{fallback}, {label.Value}";
                }
            }

            return fallback;
        }

        public GameObject GetCurrentFocus()
        {
            return lastFocused;
        }

        private string GetSourceId(GameObject target)
        {
            if (target == null)
            {
                return "focus";
            }

            return $"focus_{target.GetInstanceID()}";
        }

        private void HandleUiSelectionChanged(GameObject previous, GameObject current)
        {
            lastSelected = current;
            if (current != null)
            {
                lastFocused = current;
            }

            string spokenText = BuildUiSelectionNarration(current);
            string previousName = previous != null ? previous.name : "None";
            string currentName = current != null ? current.name : "None";
            A11yLogger.Info($"UI selection changed: {previousName} -> {currentName}");

            string narration = string.IsNullOrWhiteSpace(spokenText) ? currentName : spokenText;
            speechService.Speak($"Selected: {narration}", A11ySpeechPriority.Normal, "ui_selected", true);
        }

        private void UpdateUnitySelection()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                lastSelected = null;
                return;
            }

            GameObject current = eventSystem.currentSelectedGameObject;
            if (current == null)
            {
                lastSelected = null;
                return;
            }

            if (current != lastSelected)
            {
                HandleUiSelectionChanged(lastSelected, current);
            }
        }

        private void UpdateNguiSelection()
        {
            GameObject selected = NguiReflection.GetSelectedOrHoveredObject();
            if (selected == null)
            {
                lastNguiSelected = null;
                return;
            }

            if (selected == lastNguiSelected)
            {
                return;
            }

            lastNguiSelected = selected;
            string label = NguiReflection.ResolveLabelText(selected);
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastNguiSpeakTime < NguiSpeakCooldownSeconds)
            {
                return;
            }

            speechService.Speak($"Selected: {label}", A11ySpeechPriority.Normal, "ngui_focus", true);
            lastNguiSpeakTime = now;
        }

        private static string ResolveSelectionText(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            Component tmp = TmpReflection.GetTmpTextComponent(target);
            if (tmp != null && VisibilityUtil.IsElementVisible(tmp, true))
            {
                string text = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(tmp));
                if (!VisibilityUtil.IsGarbageText(text))
                {
                    return text;
                }
            }

            Text uiText = target.GetComponent<Text>();
            if (uiText != null && VisibilityUtil.IsElementVisible(uiText, true))
            {
                string text = VisibilityUtil.NormalizeText(uiText.text);
                if (!VisibilityUtil.IsGarbageText(text))
                {
                    return text;
                }
            }

            foreach (Component tmpChild in TmpReflection.GetTmpTextComponentsInChildren(target, true))
            {
                if (tmpChild == null || tmpChild.gameObject == target)
                {
                    continue;
                }

                if (!VisibilityUtil.IsElementVisible(tmpChild, true))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(tmpChild));
                if (VisibilityUtil.IsGarbageText(text))
                {
                    continue;
                }

                return text;
            }

            Text[] uiTexts = target.GetComponentsInChildren<Text>(true);
            foreach (Text childText in uiTexts)
            {
                if (childText == null || childText.gameObject == target)
                {
                    continue;
                }

                if (!VisibilityUtil.IsElementVisible(childText, true))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(childText.text);
                if (VisibilityUtil.IsGarbageText(text))
                {
                    continue;
                }

                return text;
            }

            return null;
        }

        public static void ApplyHarmonyPatches(HarmonyLib.Harmony harmony, FocusTracker tracker)
        {
            if (harmony == null || tracker == null)
            {
                return;
            }

            FocusTrackerPatches.Tracker = tracker;
            harmony.PatchAll(typeof(FocusTrackerPatches));
        }

        [HarmonyPatch]
        private static class FocusTrackerPatches
        {
            public static FocusTracker Tracker { get; set; }

            [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.SetSelectedGameObject), new[] { typeof(GameObject), typeof(BaseEventData) })]
            [HarmonyPostfix]
            private static void EventSystemSetSelected(GameObject selected)
            {
                Tracker?.HandleFocusChanged(selected);
            }

            [HarmonyPatch(typeof(Selectable), nameof(Selectable.OnSelect))]
            [HarmonyPostfix]
            private static void SelectableOnSelect(BaseEventData eventData)
            {
                GameObject selected = eventData?.selectedObject;
                if (selected != null)
                {
                    Tracker?.HandleFocusChanged(selected);
                }
            }
        }
    }
}
