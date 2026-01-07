using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using UnityEngine;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal sealed class ScreenReviewController
    {
        private readonly A11ySpeechService speechService;
        private readonly List<SnapshotItem> snapshot = new List<SnapshotItem>();
        private int currentIndex;
        private bool inReviewMode;

        public ScreenReviewController(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public bool InReviewMode => inReviewMode;

        public void EnterOrRefresh()
        {
            BuildSnapshot();
            inReviewMode = snapshot.Count > 0;
            currentIndex = 0;
            if (snapshot.Count == 0)
            {
                speechService.Speak("No items", A11ySpeechPriority.Normal, "screenreview", false);
                return;
            }

            speechService.Speak($"{snapshot.Count} items", A11ySpeechPriority.Normal, "screenreview", false);
            SpeakCurrent();
        }

        public void Exit()
        {
            inReviewMode = false;
        }

        public void Next()
        {
            if (!inReviewMode || snapshot.Count == 0)
            {
                return;
            }

            currentIndex = Mathf.Clamp(currentIndex + 1, 0, snapshot.Count - 1);
            SpeakCurrent();
        }

        public void Previous()
        {
            if (!inReviewMode || snapshot.Count == 0)
            {
                return;
            }

            currentIndex = Mathf.Clamp(currentIndex - 1, 0, snapshot.Count - 1);
            SpeakCurrent();
        }

        public void SpeakCurrent()
        {
            if (!inReviewMode || snapshot.Count == 0)
            {
                return;
            }

            SnapshotItem item = snapshot[currentIndex];
            if (item != null && !string.IsNullOrWhiteSpace(item.SpokenText))
            {
                speechService.Speak(item.SpokenText, A11ySpeechPriority.Normal, $"review_{currentIndex}", false);
            }
        }

        public void ReadAll()
        {
            if (!inReviewMode || snapshot.Count == 0)
            {
                return;
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                SnapshotItem item = snapshot[i];
                if (item != null && !string.IsNullOrWhiteSpace(item.SpokenText))
                {
                    speechService.Speak(item.SpokenText, A11ySpeechPriority.Low, $"review_{i}", false);
                }
            }
        }

        public SnapshotItem GetCurrentItem()
        {
            if (!inReviewMode || snapshot.Count == 0)
            {
                return null;
            }

            return snapshot[currentIndex];
        }

        public void BuildSnapshot()
        {
            snapshot.Clear();
            HashSet<UnityEngine.Object> usedLabels = new HashSet<UnityEngine.Object>();
            List<SnapshotItem> items = new List<SnapshotItem>();

            foreach (Selectable selectable in UnityEngine.Object.FindObjectsOfType<Selectable>(true))
            {
                if (selectable == null || !selectable.gameObject.activeInHierarchy)
                {
                    continue;
                }

                RectTransform rectTransform = selectable.GetComponent<RectTransform>();
                if (rectTransform != null && !VisibilityUtil.IsRectTransformVisible(rectTransform))
                {
                    continue;
                }

                AccessibleLabel label = AccessibleNameResolver.Resolve(selectable.gameObject);
                if (label == null)
                {
                    continue;
                }

                if (label.LabelSource != null)
                {
                    usedLabels.Add(label.LabelSource);
                }

                string spoken = label.ToSpokenString(true);
                if (string.IsNullOrWhiteSpace(spoken))
                {
                    continue;
                }

                SnapshotItem item = new SnapshotItem
                {
                    Target = selectable.gameObject,
                    SpokenText = spoken
                };

                if (rectTransform != null && VisibilityUtil.TryGetScreenRect(rectTransform, out Rect rect))
                {
                    item.ScreenRect = rect;
                    item.HasRect = true;
                }

                items.Add(item);
            }

            foreach (TMP_Text tmp in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
            {
                AddStaticText(tmp, items, usedLabels);
            }

            foreach (Text uiText in UnityEngine.Object.FindObjectsOfType<Text>(true))
            {
                AddStaticText(uiText, items, usedLabels);
            }

            foreach (Component ngui in GetAllNguiLabels())
            {
                AddStaticText(ngui, items, usedLabels);
            }

            snapshot.AddRange(OrderSnapshot(items));
        }

        private void AddStaticText(Component component, List<SnapshotItem> items, HashSet<UnityEngine.Object> usedLabels)
        {
            if (component == null || usedLabels.Contains(component))
            {
                return;
            }

            if (!VisibilityUtil.IsElementVisible(component, Settings.instance.AllowUnknownNguiInSnapshot))
            {
                return;
            }

            string text = GetText(component);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            SnapshotItem item = new SnapshotItem
            {
                Target = component.gameObject,
                SpokenText = text
            };

            if (component is Graphic graphic && VisibilityUtil.TryGetScreenRect(graphic.rectTransform, out Rect rect))
            {
                item.ScreenRect = rect;
                item.HasRect = true;
            }
            else if (component is TMP_Text tmp && VisibilityUtil.TryGetScreenRect(tmp.rectTransform, out Rect tmpRect))
            {
                item.ScreenRect = tmpRect;
                item.HasRect = true;
            }
            else if (component is Component)
            {
                RectTransform rectTransform = component.GetComponent<RectTransform>();
                if (rectTransform != null && VisibilityUtil.TryGetScreenRect(rectTransform, out Rect rectTransformRect))
                {
                    item.ScreenRect = rectTransformRect;
                    item.HasRect = true;
                }
            }

            items.Add(item);
        }

        private IEnumerable<SnapshotItem> OrderSnapshot(List<SnapshotItem> items)
        {
            List<SnapshotItem> sorted = items
                .OrderByDescending(item => item.HasRect ? item.ScreenRect.yMax : float.MinValue)
                .ThenBy(item => item.HasRect ? item.ScreenRect.xMin : float.MaxValue)
                .ToList();

            List<SnapshotItem> deduped = new List<SnapshotItem>();
            HashSet<string> seen = new HashSet<string>();
            foreach (SnapshotItem item in sorted)
            {
                string key = item.SpokenText;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (seen.Contains(key))
                {
                    continue;
                }

                seen.Add(key);
                deduped.Add(item);
            }

            return deduped;
        }

        private string GetText(Component component)
        {
            if (component is TMP_Text tmp)
            {
                return VisibilityUtil.NormalizeText(tmp.text);
            }

            if (component is Text uiText)
            {
                return VisibilityUtil.NormalizeText(uiText.text);
            }

            if (IsNguiLabel(component))
            {
                return VisibilityUtil.NormalizeText(NGUIReflection.GetUILabelText(component));
            }

            return null;
        }

        private bool IsNguiLabel(Component component)
        {
            return component != null && NGUIReflection.GetUILabelType() != null && component.GetType() == NGUIReflection.GetUILabelType();
        }

        private IEnumerable<Component> GetAllNguiLabels()
        {
            if (!NGUIReflection.HasUILabel)
            {
                return Enumerable.Empty<Component>();
            }

            return UnityEngine.Object.FindObjectsOfType<Component>(true)
                .Where(component => IsNguiLabel(component));
        }
    }
}
