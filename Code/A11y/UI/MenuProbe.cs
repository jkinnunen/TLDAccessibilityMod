using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TLDAccessibility.A11y.Logging;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

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
            public SnapshotResult(IReadOnlyList<CandidateSnapshot> candidates, UiToolkitSnapshot uiToolkitSnapshot)
            {
                Candidates = candidates ?? Array.Empty<CandidateSnapshot>();
                ById = Candidates.ToDictionary(candidate => candidate.InstanceId, candidate => candidate);
                UiToolkitSnapshot = uiToolkitSnapshot;
            }

            public IReadOnlyList<CandidateSnapshot> Candidates { get; }
            public Dictionary<int, CandidateSnapshot> ById { get; }
            public UiToolkitSnapshot UiToolkitSnapshot { get; }
        }

        internal sealed class UiToolkitSnapshot
        {
            public UiToolkitSnapshot(int documentCount, IReadOnlyList<string> textCandidates)
            {
                DocumentCount = documentCount;
                TextCandidates = textCandidates ?? Array.Empty<string>();
            }

            public int DocumentCount { get; }
            public IReadOnlyList<string> TextCandidates { get; }
            public int TextCount => TextCandidates.Count;
            public string FirstText => TextCandidates.Count > 0 ? TextCandidates[0] : string.Empty;
        }

        private sealed class FilterCounters
        {
            public int Total { get; set; }
            public int AfterText { get; set; }
            public int AfterActiveInHierarchy { get; set; }
            public int AfterIsActiveAndEnabled { get; set; }
            public int AfterVisibility { get; set; }
            public int AfterMask { get; set; }

            public string Format(string label)
            {
                return $"MenuProbe census ({label}): total={Total}, afterText={AfterText}, afterActiveInHierarchy={AfterActiveInHierarchy}, afterIsActiveAndEnabled={AfterIsActiveAndEnabled}, afterVisibility={AfterVisibility}, afterMask={AfterMask}";
            }
        }

        public SnapshotResult Capture(bool logDiagnostics = false)
        {
            List<CandidateSnapshot> candidates = new List<CandidateSnapshot>();
            List<Component> tmpComponents = TmpReflection.FindAllTmpTextComponents(false).ToList();
            List<Text> uiTextComponents = UnityEngine.Object.FindObjectsOfType<Text>(false).ToList();
            bool useUiTextFallback = tmpComponents.Count == 0 && uiTextComponents.Count > 0;

            IEnumerable<Component> sourceComponents = tmpComponents;
            if (useUiTextFallback)
            {
                sourceComponents = uiTextComponents.Cast<Component>();
            }
            else
            {
                sourceComponents = tmpComponents.Concat(uiTextComponents.Cast<Component>());
            }

            foreach (Component component in sourceComponents)
            {
                if (!TryBuildCandidate(component, GetTextValue, out CandidateSnapshot candidate))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            if (logDiagnostics)
            {
                UiToolkitSnapshot uiToolkitSnapshot = LogDiagnostics(tmpComponents, uiTextComponents, candidates);
                return new SnapshotResult(candidates, uiToolkitSnapshot);
            }

            return new SnapshotResult(candidates, null);
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

        private static bool TryBuildCandidate(Component component, Func<Component, string> textResolver, out CandidateSnapshot candidate)
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

            string text = VisibilityUtil.NormalizeText(textResolver?.Invoke(component));
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

        private static string GetTextValue(Component component)
        {
            if (component == null)
            {
                return null;
            }

            if (component is Text uiText)
            {
                return uiText.text;
            }

            if (TmpReflection.IsTmpText(component))
            {
                return TmpReflection.GetTmpTextValue(component);
            }

            return null;
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

        private static UiToolkitSnapshot LogDiagnostics(List<Component> tmpComponents, List<Text> uiTextComponents, List<CandidateSnapshot> candidates)
        {
            int tmpFindObjectsCount = tmpComponents?.Count ?? 0;
            int uiTextFindObjectsCount = uiTextComponents?.Count ?? 0;

            List<Component> tmpAll = FindAllTmpTextComponents();
            List<Text> uiTextAll = UnityEngine.Resources.FindObjectsOfTypeAll<Text>()
                .Where(text => text != null)
                .ToList();

            int tmpFindObjectsAllCount = tmpAll.Count;
            int uiTextFindObjectsAllCount = uiTextAll.Count;
            int selectableCount = UnityEngine.Resources.FindObjectsOfTypeAll<Selectable>().Length;
            int canvasCount = UnityEngine.Resources.FindObjectsOfTypeAll<Canvas>().Length;

            A11yLogger.Info($"MenuProbe census: TMP total (FindObjectsOfType)={tmpFindObjectsCount}, TMP total (FindObjectsOfTypeAll)={tmpFindObjectsAllCount}, UI.Text total (FindObjectsOfType)={uiTextFindObjectsCount}, UI.Text total (FindObjectsOfTypeAll)={uiTextFindObjectsAllCount}, Selectable total (FindObjectsOfTypeAll)={selectableCount}, Canvas total (FindObjectsOfTypeAll)={canvasCount}");

            FilterCounters tmpCounters = new FilterCounters();
            foreach (Component component in tmpAll)
            {
                AccumulateCounters(component, TmpReflection.GetTmpTextValue, tmpCounters);
            }

            FilterCounters uiTextCounters = new FilterCounters();
            foreach (Text text in uiTextAll)
            {
                AccumulateCounters(text, component => (component as Text)?.text, uiTextCounters);
            }

            A11yLogger.Info(tmpCounters.Format("TMP"));
            A11yLogger.Info(uiTextCounters.Format("UI.Text"));

            if (candidates.Count == 0)
            {
                List<Component> rawTextComponents = tmpAll.Concat(uiTextAll.Cast<Component>())
                    .Where(component => !string.IsNullOrWhiteSpace(VisibilityUtil.NormalizeText(GetTextValue(component))))
                    .ToList();
                if (rawTextComponents.Count == 0)
                {
                    A11yLogger.Info("MenuProbe census: no raw text components found (FindObjectsOfTypeAll).");
                    return CaptureUiToolkitSnapshot();
                }

                int limit = Mathf.Min(rawTextComponents.Count, 20);
                A11yLogger.Info($"MenuProbe census: logging {limit} raw text components (before visibility filtering).");
                for (int i = 0; i < limit; i++)
                {
                    Component component = rawTextComponents[i];
                    string typeLabel = TmpReflection.IsTmpText(component) ? "TMP_Text" : component is Text ? "UI.Text" : component.GetType().Name;
                    string text = TrimText(VisibilityUtil.NormalizeText(GetTextValue(component)), 120);
                    Behaviour behaviour = component as Behaviour;
                    bool enabled = behaviour?.enabled ?? true;
                    bool activeInHierarchy = component.gameObject.activeInHierarchy;
                    Color color = GetComponentColor(component);
                    string hierarchyPath = BuildHierarchyPath(component.transform);
                    A11yLogger.Info($"MenuProbe census raw[{i + 1}]: type={typeLabel}, text=\"{text}\", activeInHierarchy={activeInHierarchy}, enabled={enabled}, color.a={color.a:0.###}, path={hierarchyPath}");
                }
            }

            return CaptureUiToolkitSnapshot();
        }

        private static UiToolkitSnapshot CaptureUiToolkitSnapshot()
        {
            UIDocument[] documents = UnityEngine.Resources.FindObjectsOfTypeAll<UIDocument>();
            int documentCount = documents?.Length ?? 0;
            A11yLogger.Info($"MenuProbe UI Toolkit census: UIDocument total={documentCount}");

            List<string> textCandidates = new List<string>();
            HashSet<string> uniqueTexts = new HashSet<string>();

            if (documents == null || documents.Length == 0)
            {
                return new UiToolkitSnapshot(documentCount, textCandidates);
            }

            foreach (UIDocument document in documents)
            {
                if (document == null)
                {
                    continue;
                }

                string documentName = string.IsNullOrWhiteSpace(document.name) ? "(unnamed)" : document.name;
                string gameObjectName = document.gameObject != null ? document.gameObject.name : "(null)";
                VisualElement root = document.rootVisualElement;
                if (root == null)
                {
                    A11yLogger.Info($"MenuProbe UI Toolkit snapshot: UIDocument name={documentName}, gameObject={gameObjectName}, rootVisualElement=null");
                    continue;
                }

                int docTextCount = 0;
                CollectUiToolkitText(root, textCandidates, uniqueTexts, ref docTextCount);
                A11yLogger.Info($"MenuProbe UI Toolkit snapshot: UIDocument name={documentName}, gameObject={gameObjectName}, textCount={docTextCount}");
            }

            if (textCandidates.Count > 0)
            {
                int limit = Mathf.Min(10, textCandidates.Count);
                A11yLogger.Info($"MenuProbe UI Toolkit snapshot: logging {limit} unique text samples.");
                for (int i = 0; i < limit; i++)
                {
                    string text = TrimText(textCandidates[i], 120);
                    A11yLogger.Info($"MenuProbe UI Toolkit snapshot text[{i + 1}]: \"{text}\"");
                }
            }

            return new UiToolkitSnapshot(documentCount, textCandidates);
        }

        private static void CollectUiToolkitText(
            VisualElement root,
            List<string> textCandidates,
            HashSet<string> uniqueTexts,
            ref int docTextCount)
        {
            if (root == null)
            {
                return;
            }

            Stack<VisualElement> stack = new Stack<VisualElement>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                VisualElement element = stack.Pop();
                if (element == null)
                {
                    continue;
                }

                bool displayNone = element.resolvedStyle.display == DisplayStyle.None;
                bool hidden = element.resolvedStyle.visibility == Visibility.Hidden;
                if (!displayNone && !hidden)
                {
                    var hierarchy = element.hierarchy;
                    for (int i = 0; i < hierarchy.childCount; i++)
                    {
                        VisualElement child = hierarchy.ElementAt(i);
                        stack.Push(child);
                    }
                }

                if (displayNone || hidden)
                {
                    continue;
                }

                Rect worldBound = element.worldBound;
                if (worldBound.width <= 0f || worldBound.height <= 0f)
                {
                    continue;
                }

                string text = GetUiToolkitTextValue(element);
                string normalized = VisibilityUtil.NormalizeText(text);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                docTextCount++;
                if (uniqueTexts.Add(normalized))
                {
                    textCandidates.Add(normalized);
                }
            }
        }

        private static string GetUiToolkitTextValue(VisualElement element)
        {
            if (element == null)
            {
                return null;
            }

            if (element is Label label)
            {
                return label.text;
            }

            if (element is TextElement textElement)
            {
                return textElement.text;
            }

            PropertyInfo property = element.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (property?.PropertyType == typeof(string))
            {
                return property.GetValue(element) as string;
            }

            return null;
        }

        private static void AccumulateCounters(Component component, Func<Component, string> textResolver, FilterCounters counters)
        {
            if (component == null || counters == null)
            {
                return;
            }

            counters.Total++;
            string text = VisibilityUtil.NormalizeText(textResolver?.Invoke(component));
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            counters.AfterText++;

            if (!component.gameObject.activeInHierarchy)
            {
                return;
            }

            counters.AfterActiveInHierarchy++;

            Behaviour behaviour = component as Behaviour;
            if (behaviour != null && !behaviour.isActiveAndEnabled)
            {
                return;
            }

            counters.AfterIsActiveAndEnabled++;

            bool passesVisibility = false;
            bool passesMask = false;
            if (component is Graphic graphic)
            {
                VisibilityUtil.TryGetGraphicVisibility(graphic, out passesVisibility, out passesMask);
            }
            else
            {
                passesVisibility = VisibilityUtil.IsElementVisible(component, false);
                passesMask = passesVisibility;
            }

            if (!passesVisibility)
            {
                return;
            }

            counters.AfterVisibility++;

            if (!passesMask)
            {
                return;
            }

            counters.AfterMask++;
        }

        private static List<Component> FindAllTmpTextComponents()
        {
            if (!TmpReflection.HasTmpText)
            {
                return new List<Component>();
            }

            Type tmpType = TmpReflection.TmpTextType;
            if (tmpType == null)
            {
                return new List<Component>();
            }

            Il2CppSystem.Type il2cppType = Il2CppInterop.Runtime.Il2CppType.From(tmpType);
            UnityEngine.Object[] found = UnityEngine.Resources.FindObjectsOfTypeAll(il2cppType);
            List<Component> results = new List<Component>(found.Length);
            foreach (UnityEngine.Object obj in found)
            {
                if (obj is Component component)
                {
                    results.Add(component);
                }
            }

            return results;
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
