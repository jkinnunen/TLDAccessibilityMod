using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TLDAccessibility.A11y.Model;
using UnityEngine;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal static class AccessibleNameResolver
    {
        private const float NearbyDistance = 160f;
        private const float RowAlignmentThreshold = 30f;
        private const float HeaderDistance = 200f;

        public static AccessibleLabel Resolve(GameObject target)
        {
            AccessibleLabel label = new AccessibleLabel();
            if (target == null)
            {
                return label;
            }

            label.Name = ResolveOverrideName(target);
            if (string.IsNullOrWhiteSpace(label.Name))
            {
                label.Name = ResolveTextOnObject(target, out Object source);
                label.LabelSource = source;
            }

            if (string.IsNullOrWhiteSpace(label.Name))
            {
                label.Name = ResolveTextOnChildren(target, out Object source);
                label.LabelSource = source;
            }

            if (string.IsNullOrWhiteSpace(label.Name))
            {
                label.Name = ResolveNearbyLabel(target, out Object source, out string header);
                label.LabelSource = source;
                label.GroupHeader = header;
            }

            if (string.IsNullOrWhiteSpace(label.GroupHeader))
            {
                label.GroupHeader = ResolveGroupHeader(target);
            }

            label.Role = ResolveRole(target, out string value);
            label.Value = value;
            return label;
        }

        private static string ResolveOverrideName(GameObject target)
        {
            A11yNameOverride overrideComponent = target.GetComponent<A11yNameOverride>();
            if (overrideComponent != null && !string.IsNullOrWhiteSpace(overrideComponent.OverrideName))
            {
                return overrideComponent.OverrideName.Trim();
            }

            return null;
        }

        private static string ResolveTextOnObject(GameObject target, out Object source)
        {
            source = null;
            TMP_Text tmp = target.GetComponent<TMP_Text>();
            if (tmp != null && VisibilityUtil.IsElementVisible(tmp, true))
            {
                string text = VisibilityUtil.NormalizeText(tmp.text);
                if (!VisibilityUtil.IsGarbageText(text))
                {
                    source = tmp;
                    return text;
                }
            }

            Text uiText = target.GetComponent<Text>();
            if (uiText != null && VisibilityUtil.IsElementVisible(uiText, true))
            {
                string text = VisibilityUtil.NormalizeText(uiText.text);
                if (!VisibilityUtil.IsGarbageText(text))
                {
                    source = uiText;
                    return text;
                }
            }

            Component ngui = GetNguiLabel(target);
            if (ngui != null && VisibilityUtil.IsElementVisible(ngui, false))
            {
                string text = VisibilityUtil.NormalizeText(NGUIReflection.GetUILabelText(ngui));
                if (!VisibilityUtil.IsGarbageText(text))
                {
                    source = ngui;
                    return text;
                }
            }

            return null;
        }

        private static string ResolveTextOnChildren(GameObject target, out Object source)
        {
            source = null;
            TMP_Text[] tmpTexts = target.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text tmp in tmpTexts)
            {
                if (tmp == null || tmp.gameObject == target)
                {
                    continue;
                }

                if (!VisibilityUtil.IsElementVisible(tmp, true))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(tmp.text);
                if (VisibilityUtil.IsGarbageText(text))
                {
                    continue;
                }

                source = tmp;
                return text;
            }

            Text[] uiTexts = target.GetComponentsInChildren<Text>(true);
            foreach (Text uiText in uiTexts)
            {
                if (uiText == null || uiText.gameObject == target)
                {
                    continue;
                }

                if (!VisibilityUtil.IsElementVisible(uiText, true))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(uiText.text);
                if (VisibilityUtil.IsGarbageText(text))
                {
                    continue;
                }

                source = uiText;
                return text;
            }

            foreach (Component child in target.GetComponentsInChildren<Component>(true))
            {
                if (child == null || child.gameObject == target)
                {
                    continue;
                }

                if (!IsNguiLabel(child))
                {
                    continue;
                }

                if (!VisibilityUtil.IsElementVisible(child, false))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(NGUIReflection.GetUILabelText(child));
                if (VisibilityUtil.IsGarbageText(text))
                {
                    continue;
                }

                source = child;
                return text;
            }

            return null;
        }

        private static string ResolveNearbyLabel(GameObject target, out Object source, out string header)
        {
            source = null;
            header = null;
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null || !VisibilityUtil.TryGetScreenRect(rectTransform, out Rect controlRect))
            {
                return null;
            }

            Vector2 controlCenter = controlRect.center;
            List<CandidateLabel> candidates = new List<CandidateLabel>();
            foreach (TMP_Text tmp in GetAllVisibleTmpTexts())
            {
                if (tmp == null || tmp.gameObject == target)
                {
                    continue;
                }

                if (!TryGetCandidate(tmp, controlRect, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            foreach (Text uiText in GetAllVisibleUiTexts())
            {
                if (uiText == null || uiText.gameObject == target)
                {
                    continue;
                }

                if (!TryGetCandidate(uiText, controlRect, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            foreach (Component ngui in GetAllVisibleNguiLabels())
            {
                if (!TryGetCandidate(ngui, controlRect, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            CandidateLabel best = candidates.OrderBy(c => c.Score).FirstOrDefault();
            if (best == null)
            {
                return null;
            }

            if (best.IsHeader)
            {
                header = best.Text;
                return null;
            }

            source = best.Source;
            header = best.HeaderCandidate;
            return best.Text;
        }

        private static string ResolveGroupHeader(GameObject target)
        {
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            if (rectTransform == null || !VisibilityUtil.TryGetScreenRect(rectTransform, out Rect controlRect))
            {
                return null;
            }

            Vector2 controlCenter = controlRect.center;
            CandidateLabel bestHeader = null;
            foreach (TMP_Text tmp in GetAllVisibleTmpTexts())
            {
                if (!TryGetHeaderCandidate(tmp, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                if (bestHeader == null || candidate.Score < bestHeader.Score)
                {
                    bestHeader = candidate;
                }
            }

            foreach (Text uiText in GetAllVisibleUiTexts())
            {
                if (!TryGetHeaderCandidate(uiText, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                if (bestHeader == null || candidate.Score < bestHeader.Score)
                {
                    bestHeader = candidate;
                }
            }

            foreach (Component ngui in GetAllVisibleNguiLabels())
            {
                if (!TryGetHeaderCandidate(ngui, controlCenter, out CandidateLabel candidate))
                {
                    continue;
                }

                if (bestHeader == null || candidate.Score < bestHeader.Score)
                {
                    bestHeader = candidate;
                }
            }

            return bestHeader?.Text;
        }

        private static string ResolveRole(GameObject target, out string value)
        {
            value = null;
            if (target == null)
            {
                return null;
            }

            Toggle toggle = target.GetComponent<Toggle>();
            if (toggle != null)
            {
                value = toggle.isOn ? "on" : "off";
                return "toggle";
            }

            Slider slider = target.GetComponent<Slider>();
            if (slider != null)
            {
                float percent = slider.maxValue > slider.minValue
                    ? Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value) * 100f
                    : slider.value * 100f;
                value = $"{Mathf.RoundToInt(percent)} percent";
                return "slider";
            }

            Dropdown dropdown = target.GetComponent<Dropdown>();
            if (dropdown != null)
            {
                if (dropdown.options != null && dropdown.value >= 0 && dropdown.value < dropdown.options.Count)
                {
                    value = dropdown.options[dropdown.value].text;
                }

                return "dropdown";
            }

            TMP_Dropdown tmpDropdown = target.GetComponent<TMP_Dropdown>();
            if (tmpDropdown != null)
            {
                if (tmpDropdown.options != null && tmpDropdown.value >= 0 && tmpDropdown.value < tmpDropdown.options.Count)
                {
                    value = tmpDropdown.options[tmpDropdown.value].text;
                }

                return "dropdown";
            }

            InputField inputField = target.GetComponent<InputField>();
            if (inputField != null)
            {
                value = ResolveInputValue(inputField.text);
                return "edit";
            }

            TMP_InputField tmpInput = target.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                value = ResolveInputValue(tmpInput.text);
                return "edit";
            }

            Button button = target.GetComponent<Button>();
            if (button != null)
            {
                return "button";
            }

            if (IsListItem(target, out string listValue))
            {
                value = listValue;
                return "list item";
            }

            Selectable selectable = target.GetComponent<Selectable>();
            if (selectable != null)
            {
                return "selectable";
            }

            return null;
        }

        private static string ResolveInputValue(string text)
        {
            switch (Settings.instance.Verbosity)
            {
                case VerbosityLevel.Detailed:
                    return string.IsNullOrWhiteSpace(text) ? "empty" : text;
                case VerbosityLevel.Normal:
                    return string.IsNullOrWhiteSpace(text) ? "empty" : $"{text.Length} characters";
                default:
                    return null;
            }
        }

        private static bool IsListItem(GameObject target, out string value)
        {
            value = null;
            if (target == null)
            {
                return false;
            }

            ScrollRect scrollRect = target.GetComponentInParent<ScrollRect>();
            if (scrollRect == null)
            {
                return false;
            }

            Transform parent = target.transform.parent;
            if (parent == null)
            {
                return false;
            }

            List<Selectable> siblings = parent.GetComponentsInChildren<Selectable>(true)
                .Where(s => s != null && s.transform.parent == parent)
                .ToList();

            int index = siblings.IndexOf(target.GetComponent<Selectable>());
            if (index >= 0 && siblings.Count > 0)
            {
                value = $"Item {index + 1} of {siblings.Count}";
            }

            return true;
        }

        private static bool TryGetCandidate(Component component, Rect controlRect, Vector2 controlCenter, out CandidateLabel candidate)
        {
            candidate = null;
            string text = GetText(component);
            if (VisibilityUtil.IsGarbageText(text))
            {
                return false;
            }

            if (!TryGetComponentRect(component, out Rect rect))
            {
                return false;
            }

            Vector2 center = rect.center;
            float distance = Vector2.Distance(controlCenter, center);
            if (distance > NearbyDistance)
            {
                return false;
            }

            float verticalDelta = Mathf.Abs(controlCenter.y - center.y);
            float rowPenalty = verticalDelta > RowAlignmentThreshold ? 50f : 0f;
            bool isLeftOrRight = center.x < controlRect.xMin || center.x > controlRect.xMax;
            float positionBonus = isLeftOrRight ? -20f : 10f;
            bool isHeader = center.y > controlCenter.y && verticalDelta <= HeaderDistance;
            float score = distance + rowPenalty + positionBonus;
            string header = null;
            if (isHeader && !isLeftOrRight)
            {
                score += 15f;
                header = text;
            }

            candidate = new CandidateLabel
            {
                Source = component,
                Text = text,
                Score = score,
                HeaderCandidate = header,
                IsHeader = false
            };
            return true;
        }

        private static bool TryGetHeaderCandidate(Component component, Vector2 controlCenter, out CandidateLabel candidate)
        {
            candidate = null;
            string text = GetText(component);
            if (VisibilityUtil.IsGarbageText(text))
            {
                return false;
            }

            if (!TryGetComponentRect(component, out Rect rect))
            {
                return false;
            }

            Vector2 center = rect.center;
            float verticalDelta = center.y - controlCenter.y;
            if (verticalDelta < 0f || verticalDelta > HeaderDistance)
            {
                return false;
            }

            float score = verticalDelta;
            candidate = new CandidateLabel
            {
                Source = component,
                Text = text,
                Score = score,
                IsHeader = true
            };
            return true;
        }

        private static bool TryGetComponentRect(Component component, out Rect rect)
        {
            rect = Rect.zero;
            if (component is Graphic graphic)
            {
                return VisibilityUtil.TryGetScreenRect(graphic.rectTransform, out rect);
            }

            if (component is TMP_Text tmp)
            {
                return VisibilityUtil.TryGetScreenRect(tmp.rectTransform, out rect);
            }

            if (IsNguiLabel(component))
            {
                Vector3[] corners = NGUIReflection.GetUILabelWorldCorners(component);
                if (corners != null && corners.Length >= 4)
                {
                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                    Vector2 max = new Vector2(float.MinValue, float.MinValue);
                    foreach (Vector3 corner in corners)
                    {
                        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                        min = Vector2.Min(min, screen);
                        max = Vector2.Max(max, screen);
                    }

                    rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    return true;
                }
            }

            return false;
        }

        private static string GetText(Component component)
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

        private static IEnumerable<TMP_Text> GetAllVisibleTmpTexts()
        {
            return UnityEngine.Object.FindObjectsOfType<TMP_Text>(true)
                .Where(tmp => tmp != null && VisibilityUtil.IsElementVisible(tmp, true));
        }

        private static IEnumerable<Text> GetAllVisibleUiTexts()
        {
            return UnityEngine.Object.FindObjectsOfType<Text>(true)
                .Where(text => text != null && VisibilityUtil.IsElementVisible(text, true));
        }

        private static IEnumerable<Component> GetAllVisibleNguiLabels()
        {
            if (!NGUIReflection.HasUILabel)
            {
                return Enumerable.Empty<Component>();
            }

            return UnityEngine.Object.FindObjectsOfType<Component>(true)
                .Where(component => IsNguiLabel(component) && VisibilityUtil.IsElementVisible(component, false));
        }

        private static Component GetNguiLabel(GameObject target)
        {
            if (target == null || !NGUIReflection.HasUILabel)
            {
                return null;
            }

            Component component = target.GetComponent(NGUIReflection.GetUILabelType());
            if (component != null)
            {
                return component;
            }

            return null;
        }

        private static bool IsNguiLabel(Component component)
        {
            return component != null && NGUIReflection.GetUILabelType() != null && component.GetType() == NGUIReflection.GetUILabelType();
        }

        private sealed class CandidateLabel
        {
            public Component Source { get; set; }
            public string Text { get; set; }
            public float Score { get; set; }
            public string HeaderCandidate { get; set; }
            public bool IsHeader { get; set; }
        }
    }
}
