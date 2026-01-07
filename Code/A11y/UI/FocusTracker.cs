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

        public FocusTracker(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public void Update()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject current = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (current != lastFocused)
            {
                HandleFocusChanged(current);
            }
        }

        public void HandleFocusChanged(GameObject current)
        {
            lastFocused = current;
            if (!Settings.instance.AutoSpeakFocusChanges)
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
