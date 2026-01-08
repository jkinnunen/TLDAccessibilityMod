using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal sealed class MenuProbe
    {
        internal sealed class CandidateSnapshot
        {
            public CandidateSnapshot(
                int instanceId,
                string hierarchyPath,
                string text,
                Color color,
                float fontSize,
                int fontStyle,
                bool enabled,
                bool activeInHierarchy,
                Vector3 worldPosition)
            {
                InstanceId = instanceId;
                HierarchyPath = hierarchyPath;
                Text = text;
                LogText = TrimText(text, 120);
                Color = color;
                FontSize = fontSize;
                FontStyle = fontStyle;
                Enabled = enabled;
                ActiveInHierarchy = activeInHierarchy;
                WorldPosition = worldPosition;
            }

            public int InstanceId { get; }
            public string HierarchyPath { get; }
            public string Text { get; }
            public string LogText { get; }
            public Color Color { get; }
            public float FontSize { get; }
            public int FontStyle { get; }
            public bool Enabled { get; }
            public bool ActiveInHierarchy { get; }
            public Vector3 WorldPosition { get; }
        }

        internal sealed class SnapshotResult
        {
            public SnapshotResult(IReadOnlyList<CandidateSnapshot> candidates)
            {
                Candidates = candidates ?? Array.Empty<CandidateSnapshot>();
                ById = Candidates.ToDictionary(candidate => candidate.InstanceId, candidate => candidate);
            }

            public IReadOnlyList<CandidateSnapshot> Candidates { get; }
            public Dictionary<int, CandidateSnapshot> ById { get; }
        }

        public SnapshotResult Capture()
        {
            List<CandidateSnapshot> candidates = new List<CandidateSnapshot>();
            foreach (Component component in TmpReflection.FindAllTmpTextComponents(false))
            {
                if (!TryBuildCandidate(component, out CandidateSnapshot candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            return new SnapshotResult(candidates);
        }

        public static float CalculateChangeScore(CandidateSnapshot previous, CandidateSnapshot current)
        {
            if (previous == null || current == null)
            {
                return 0f;
            }

            float score = Mathf.Abs(current.Color.r - previous.Color.r)
                + Mathf.Abs(current.Color.g - previous.Color.g)
                + Mathf.Abs(current.Color.b - previous.Color.b)
                + Mathf.Abs(current.Color.a - previous.Color.a) * 3f;

            score += Mathf.Abs(current.FontSize - previous.FontSize) * 0.5f;

            if (current.FontStyle != previous.FontStyle)
            {
                score += 5f;
            }

            if (current.Enabled != previous.Enabled || current.ActiveInHierarchy != previous.ActiveInHierarchy)
            {
                score += 10f;
            }

            return score;
        }

        public static float CalculateProminenceScore(CandidateSnapshot candidate)
        {
            if (candidate == null)
            {
                return 0f;
            }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, candidate.WorldPosition);
            float maxDistance = Vector2.Distance(center, Vector2.zero);
            float distance = Vector2.Distance(center, screenPos);
            float centerWeight = maxDistance > 0f ? Mathf.Clamp01(1f - distance / maxDistance) : 0f;
            return candidate.Color.a * 2f + candidate.FontSize * 0.1f + centerWeight * 2f;
        }

        public static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            Stack<string> segments = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private static bool TryBuildCandidate(Component component, out CandidateSnapshot candidate)
        {
            candidate = null;
            if (component == null)
            {
                return false;
            }

            if (!component.gameObject.activeInHierarchy)
            {
                return false;
            }

            Behaviour behaviour = null;
            if (component is Behaviour candidateBehaviour)
            {
                behaviour = candidateBehaviour;
                if (!behaviour.isActiveAndEnabled)
                {
                    return false;
                }
            }

            string text = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(component));
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!VisibilityUtil.IsElementVisible(component, false))
            {
                return false;
            }

            Color color = GetComponentColor(component);
            if (color.a <= 0.01f)
            {
                return false;
            }

            bool enabled = behaviour?.enabled ?? true;
            bool activeInHierarchy = component.gameObject.activeInHierarchy;
            float fontSize = GetFloatProperty(component, "fontSize");
            int fontStyle = GetIntProperty(component, "fontStyle");
            Vector3 worldPosition = component.transform.position;
            string hierarchyPath = BuildHierarchyPath(component.transform);

            candidate = new CandidateSnapshot(
                component.GetInstanceID(),
                hierarchyPath,
                text,
                color,
                fontSize,
                fontStyle,
                enabled,
                activeInHierarchy,
                worldPosition);
            return true;
        }

        private static Color GetComponentColor(Component component)
        {
            if (component is Graphic graphic)
            {
                return graphic.color;
            }

            PropertyInfo property = component.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(component) is Color color)
            {
                return color;
            }

            return Color.white;
        }

        private static float GetFloatProperty(Component component, string propertyName, float fallback = 0f)
        {
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                return fallback;
            }

            object value = property.GetValue(component);
            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (value is double doubleValue)
            {
                return (float)doubleValue;
            }

            return fallback;
        }

        private static int GetIntProperty(Component component, string propertyName, int fallback = 0)
        {
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                return fallback;
            }

            object value = property.GetValue(component);
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is Enum enumValue)
            {
                return Convert.ToInt32(enumValue);
            }

            return fallback;
        }

        private static string TrimText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            return $"{text.Substring(0, maxLength)}...";
        }
    }
}
