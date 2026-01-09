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
        private GameObject lastNguiHovered;
        private float lastNguiSpeakTime;
        private const float NguiSpeakCooldownSeconds = 0.2f;
        private static bool nguiHoverExceptionLogged;
        private static bool nguiSelectExceptionLogged;

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
            GameObject hovered = NguiReflection.GetHoveredObject();
            if (hovered == null)
            {
                lastNguiSelected = null;
                lastNguiHovered = null;
                return;
            }

            if (hovered == lastNguiSelected || hovered == lastNguiHovered)
            {
                return;
            }

            lastNguiHovered = hovered;
            string label = NguiReflection.ResolveLabelText(hovered);
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastNguiSpeakTime < NguiSpeakCooldownSeconds)
            {
                return;
            }

            lastNguiSelected = hovered;
            speechService.Speak($"Selected: {label}", A11ySpeechPriority.Normal, "ngui_focus", true);
            lastNguiSpeakTime = now;
        }

        private void HandleNguiFocus(GameObject target, string source)
        {
            if (target == null)
            {
                return;
            }

            if (target == lastNguiSelected)
            {
                return;
            }

            string label = NguiReflection.ResolveLabelText(target);
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastNguiSpeakTime < NguiSpeakCooldownSeconds)
            {
                return;
            }

            string targetName = target.name ?? "(null)";
            A11yLogger.Info($"NGUI focus ({source}): {targetName} -> \"{label}\"");
            speechService.Speak($"Selected: {label}", A11ySpeechPriority.Normal, "ngui_focus", true);
            lastNguiSelected = target;
            lastNguiHovered = target;
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
            NguiFocusPatches.Tracker = tracker;
            harmony.PatchAll(typeof(NguiFocusPatches));
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

        [HarmonyPatch]
        private static class NguiFocusPatches
        {
            public static FocusTracker Tracker { get; set; }

            [HarmonyPatch]
            private static class UIButtonOnHoverPatch
            {
                private static System.Reflection.MethodBase TargetMethod()
                {
                    Type type = NguiReflection.GetUIButtonType();
                    if (type == null)
                    {
                        return null;
                    }

                    return AccessTools.Method(type, "OnHover", new[] { typeof(bool) });
                }

                [HarmonyPostfix]
                private static void Postfix(Component __instance, bool isOver)
                {
                    if (!isOver || __instance == null)
                    {
                        return;
                    }

                    try
                    {
                        Tracker?.HandleNguiFocus(__instance.gameObject, "UIButton.OnHover");
                    }
                    catch (Exception ex)
                    {
                        LogNguiPatchException(ref nguiHoverExceptionLogged, "NGUI UIButton.OnHover", ex);
                    }
                }
            }

            [HarmonyPatch]
            private static class UIButtonOnSelectPatch
            {
                private static System.Reflection.MethodBase TargetMethod()
                {
                    Type type = NguiReflection.GetUIButtonType();
                    if (type == null)
                    {
                        return null;
                    }

                    return AccessTools.Method(type, "OnSelect", new[] { typeof(bool) });
                }

                [HarmonyPostfix]
                private static void Postfix(Component __instance, bool isSelected)
                {
                    if (!isSelected || __instance == null)
                    {
                        return;
                    }

                    try
                    {
                        Tracker?.HandleNguiFocus(__instance.gameObject, "UIButton.OnSelect");
                    }
                    catch (Exception ex)
                    {
                        LogNguiPatchException(ref nguiSelectExceptionLogged, "NGUI UIButton.OnSelect", ex);
                    }
                }
            }
        }

        private static void LogNguiPatchException(ref bool guard, string label, Exception ex)
        {
            if (guard)
            {
                return;
            }

            guard = true;
            A11yLogger.Warning($"{label} patch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
