using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal static class VisibilityUtil
    {
        private const float AlphaThreshold = 0.01f;
        private static readonly Vector2 MinScreen = Vector2.zero;

        public static bool IsElementVisible(Component component, bool allowUnknownNgui)
        {
            if (component == null)
            {
                return false;
            }

            if (!component.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (component is Graphic graphic)
            {
                return IsGraphicVisible(graphic);
            }

            if (IsNguiLabel(component))
            {
                return IsNguiLabelVisible(component, allowUnknownNgui);
            }

            return false;
        }

        public static bool IsGraphicVisible(Graphic graphic)
        {
            if (graphic == null)
            {
                return false;
            }

            if (!graphic.enabled)
            {
                return false;
            }

            if (graphic.canvasRenderer != null && graphic.canvasRenderer.cull)
            {
                return false;
            }

            float alpha = GetEffectiveAlpha(graphic);
            if (alpha <= AlphaThreshold)
            {
                return false;
            }

            if (!TryGetScreenRect(graphic.rectTransform, out Rect rect))
            {
                return false;
            }

            if (!IntersectsScreen(rect))
            {
                return false;
            }

            if (!IsWithinMasks(graphic.rectTransform, rect))
            {
                return false;
            }

            return rect.width > 1f && rect.height > 1f;
        }

        public static bool TryGetGraphicVisibility(Graphic graphic, out bool passesVisibility, out bool passesMask)
        {
            passesVisibility = false;
            passesMask = false;
            if (graphic == null)
            {
                return false;
            }

            if (!graphic.enabled)
            {
                return false;
            }

            if (graphic.canvasRenderer != null && graphic.canvasRenderer.cull)
            {
                return false;
            }

            float alpha = GetEffectiveAlpha(graphic);
            if (alpha <= AlphaThreshold)
            {
                return false;
            }

            if (!TryGetScreenRect(graphic.rectTransform, out Rect rect))
            {
                return false;
            }

            if (!IntersectsScreen(rect))
            {
                return false;
            }

            if (rect.width <= 1f || rect.height <= 1f)
            {
                return false;
            }

            passesVisibility = true;
            passesMask = IsWithinMasks(graphic.rectTransform, rect);
            return passesMask;
        }

        public static bool TryGetScreenRect(RectTransform rectTransform, out Rect rect)
        {
            rect = Rect.zero;
            if (rectTransform == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                min = Vector2.Min(min, screenPoint);
                max = Vector2.Max(max, screenPoint);
            }

            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return rect.width >= 0f && rect.height >= 0f;
        }

        public static bool IntersectsScreen(Rect rect)
        {
            Vector2 maxScreen = new Vector2(Screen.width, Screen.height);
            Rect screenRect = Rect.MinMaxRect(MinScreen.x, MinScreen.y, maxScreen.x, maxScreen.y);
            return rect.Overlaps(screenRect);
        }

        private static float GetEffectiveAlpha(Graphic graphic)
        {
            float alpha = graphic.color.a;
            Transform current = graphic.transform;
            while (current != null)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    alpha *= group.alpha;
                }

                current = current.parent;
            }

            return alpha;
        }

        public static bool IsRectTransformVisible(RectTransform rectTransform)
        {
            if (rectTransform == null || !rectTransform.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (!TryGetScreenRect(rectTransform, out Rect rect))
            {
                return false;
            }

            if (!IntersectsScreen(rect))
            {
                return false;
            }

            if (!IsWithinMasks(rectTransform, rect))
            {
                return false;
            }

            return rect.width > 1f && rect.height > 1f;
        }

        private static bool IsWithinMasks(RectTransform rectTransform, Rect elementRect)
        {
            if (rectTransform == null)
            {
                return false;
            }

            Transform current = rectTransform.parent;
            while (current != null)
            {
                RectMask2D rectMask = current.GetComponent<RectMask2D>();
                if (rectMask != null && rectMask.isActiveAndEnabled)
                {
                    if (!TryGetScreenRect(rectMask.rectTransform, out Rect maskRect))
                    {
                        return false;
                    }

                    if (!elementRect.Overlaps(maskRect))
                    {
                        return false;
                    }
                }

                Mask mask = current.GetComponent<Mask>();
                if (mask != null && mask.isActiveAndEnabled)
                {
                    if (!TryGetScreenRect(mask.rectTransform, out Rect maskRect))
                    {
                        return false;
                    }

                    if (!elementRect.Overlaps(maskRect))
                    {
                        return false;
                    }
                }

                current = current.parent;
            }

            return true;
        }

        private static bool IsNguiLabel(Component component)
        {
            return component != null && NguiReflection.GetUILabelType() != null && component.GetType() == NguiReflection.GetUILabelType();
        }

        private static bool IsNguiLabelVisible(Component component, bool allowUnknown)
        {
            bool? isVisible = NguiReflection.GetUILabelIsVisible(component);
            if (isVisible.HasValue)
            {
                return isVisible.Value;
            }

            bool? enabled = NguiReflection.GetUILabelEnabled(component);
            if (enabled.HasValue && !enabled.Value)
            {
                return false;
            }

            float? alpha = NguiReflection.GetUILabelAlpha(component);
            if (alpha.HasValue && alpha.Value <= AlphaThreshold)
            {
                return false;
            }

            Vector3[] corners = NguiReflection.GetUILabelWorldCorners(component);
            if (corners != null && corners.Length >= 4)
            {
                Rect rect = GetRectFromCorners(corners);
                return IntersectsScreen(rect) && rect.width > 1f && rect.height > 1f;
            }

            return allowUnknown;
        }

        private static Rect GetRectFromCorners(Vector3[] corners)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
                min = Vector2.Min(min, screenPoint);
                max = Vector2.Max(max, screenPoint);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Replace("\n", " ").Replace("\r", " ").Trim();
        }

        public static bool IsGarbageText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            bool hasLetter = false;
            foreach (char c in trimmed)
            {
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    break;
                }
            }

            if (!hasLetter && trimmed.Length > 4)
            {
                return true;
            }

            return false;
        }
    }
}
