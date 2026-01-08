using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TLDAccessibility.A11y.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
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
                string componentType,
                string gameObjectName,
                string sceneName,
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
                ComponentType = componentType;
                GameObjectName = gameObjectName;
                SceneName = sceneName;
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
            public string ComponentType { get; }
            public string GameObjectName { get; }
            public string SceneName { get; }
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

        private sealed class SceneScanResult
        {
            public int SceneCount { get; set; }
            public List<string> SceneNames { get; } = new List<string>();
            public int RootCount { get; set; }
            public int TransformCount { get; set; }
            public List<Component> TmpComponents { get; } = new List<Component>();
            public List<Text> UiTextComponents { get; } = new List<Text>();
            public List<Selectable> Selectables { get; } = new List<Selectable>();
            public List<Canvas> Canvases { get; } = new List<Canvas>();
            public List<UIDocument> UiDocuments { get; } = new List<UIDocument>();
        }

        public SnapshotResult Capture(bool logDiagnostics = false)
        {
            List<CandidateSnapshot> candidates = new List<CandidateSnapshot>();
            SceneScanResult sceneScan = null;
            List<Component> tmpComponents;
            List<Text> uiTextComponents;

            if (logDiagnostics)
            {
                sceneScan = ScanScenes();
                tmpComponents = sceneScan.TmpComponents;
                uiTextComponents = sceneScan.UiTextComponents;
            }
            else
            {
                tmpComponents = FindAllTmpTextComponents();
                uiTextComponents = FindObjectsOfTypeAllIl2Cpp<Text>();
            }

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
                UiToolkitSnapshot uiToolkitSnapshot = LogDiagnostics(sceneScan, tmpComponents, uiTextComponents, candidates);
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
            string componentType = GetComponentTypeLabel(component);
            string gameObjectName = component.gameObject != null ? component.gameObject.name : "(null)";
            string sceneName = component.gameObject != null && component.gameObject.scene.IsValid()
                ? component.gameObject.scene.name
                : "(unknown)";

            candidate = new CandidateSnapshot(
                component.GetInstanceID(),
                hierarchyPath,
                componentType,
                gameObjectName,
                sceneName,
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

        private static UiToolkitSnapshot LogDiagnostics(
            SceneScanResult sceneScan,
            List<Component> tmpComponents,
            List<Text> uiTextComponents,
            List<CandidateSnapshot> candidates)
        {
            List<Component> tmpAll = tmpComponents ?? FindAllTmpTextComponents();
            List<Text> uiTextAll = uiTextComponents ?? FindObjectsOfTypeAllIl2Cpp<Text>();

            int tmpFindObjectsCount = CountActiveInHierarchy(tmpAll);
            int uiTextFindObjectsCount = CountActiveInHierarchy(uiTextAll);
            int tmpFindObjectsAllCount = tmpAll.Count;
            int uiTextFindObjectsAllCount = uiTextAll.Count;
            int selectableCount = sceneScan?.Selectables.Count ?? FindObjectsOfTypeAllIl2Cpp<Selectable>().Count;
            int canvasCount = sceneScan?.Canvases.Count ?? FindObjectsOfTypeAllIl2Cpp<Canvas>().Count;
            int transformCount = sceneScan?.TransformCount ?? FindObjectsOfTypeAllIl2Cpp<Transform>().Count;

            if (sceneScan != null)
            {
                string sceneNames = sceneScan.SceneNames.Count > 0 ? string.Join(", ", sceneScan.SceneNames) : "(none)";
                A11yLogger.Info($"MenuProbe scenes: count={sceneScan.SceneCount}, names=[{sceneNames}]");
                A11yLogger.Info($"MenuProbe scenes: root objects total={sceneScan.RootCount}");
            }

            A11yLogger.Info($"MenuProbe census: TMP total (active)={tmpFindObjectsCount}, TMP total (all)={tmpFindObjectsAllCount}, UI.Text total (active)={uiTextFindObjectsCount}, UI.Text total (all)={uiTextFindObjectsAllCount}, Selectable total (all)={selectableCount}, Canvas total (all)={canvasCount}");
            A11yLogger.Info($"MenuProbe sanity: Transform total={transformCount}");

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
                    A11yLogger.Info("MenuProbe census: no raw text components found (IL2CPP enumeration).");
                    return CaptureUiToolkitSnapshot(sceneScan?.UiDocuments);
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

            return CaptureUiToolkitSnapshot(sceneScan?.UiDocuments);
        }

        private static UiToolkitSnapshot CaptureUiToolkitSnapshot(List<UIDocument> documents = null)
        {
            List<UIDocument> documentList = documents ?? FindObjectsOfTypeAllIl2Cpp<UIDocument>();
            int documentCount = documentList?.Count ?? 0;
            A11yLogger.Info($"MenuProbe UI Toolkit census: UIDocument total={documentCount}");

            List<string> textCandidates = new List<string>();
            HashSet<string> uniqueTexts = new HashSet<string>();

            if (documentList == null || documentList.Count == 0)
            {
                return new UiToolkitSnapshot(documentCount, textCandidates);
            }

            foreach (UIDocument document in documentList)
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

            return FindObjectsOfTypeAllIl2Cpp<Component>(tmpType);
        }

        private static SceneScanResult ScanScenes()
        {
            SceneScanResult result = new SceneScanResult();
            int sceneCount = SceneManager.sceneCount;
            result.SceneCount = sceneCount;

            Type tmpType = TmpReflection.HasTmpText ? TmpReflection.TmpTextType : null;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                result.SceneNames.Add(scene.name);
                GameObject[] roots = scene.GetRootGameObjects();
                if (roots == null || roots.Length == 0)
                {
                    continue;
                }

                result.RootCount += roots.Length;
                foreach (GameObject root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                    if (transforms != null)
                    {
                        result.TransformCount += transforms.Length;
                    }

                    if (tmpType != null)
                    {
                        Component[] tmpFound = root.GetComponentsInChildren(tmpType, true);
                        if (tmpFound != null && tmpFound.Length > 0)
                        {
                            result.TmpComponents.AddRange(tmpFound);
                        }
                    }

                    Text[] uiTexts = root.GetComponentsInChildren<Text>(true);
                    if (uiTexts != null && uiTexts.Length > 0)
                    {
                        result.UiTextComponents.AddRange(uiTexts);
                    }

                    Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
                    if (selectables != null && selectables.Length > 0)
                    {
                        result.Selectables.AddRange(selectables);
                    }

                    Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
                    if (canvases != null && canvases.Length > 0)
                    {
                        result.Canvases.AddRange(canvases);
                    }

                    UIDocument[] documents = root.GetComponentsInChildren<UIDocument>(true);
                    if (documents != null && documents.Length > 0)
                    {
                        result.UiDocuments.AddRange(documents);
                    }
                }
            }

            return result;
        }

        private static int CountActiveInHierarchy<T>(IEnumerable<T> components) where T : Component
        {
            if (components == null)
            {
                return 0;
            }

            int count = 0;
            foreach (T component in components)
            {
                if (component != null && component.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<T> FindObjectsOfTypeAllIl2Cpp<T>() where T : UnityEngine.Object
        {
            return FindObjectsOfTypeAllIl2Cpp<T>(typeof(T));
        }

        private static List<T> FindObjectsOfTypeAllIl2Cpp<T>(Type type) where T : UnityEngine.Object
        {
            if (type == null)
            {
                return new List<T>();
            }

            Il2CppSystem.Type il2cppType = Il2CppInterop.Runtime.Il2CppType.From(type);
            UnityEngine.Object[] found = UnityEngine.Resources.FindObjectsOfTypeAll(il2cppType);
            List<T> results = new List<T>(found.Length);
            foreach (UnityEngine.Object obj in found)
            {
                if (obj is T casted)
                {
                    results.Add(casted);
                }
            }

            return results;
        }

        private static string GetComponentTypeLabel(Component component)
        {
            if (component == null)
            {
                return "(null)";
            }

            if (TmpReflection.IsTmpText(component))
            {
                return "TMP_Text";
            }

            if (component is Text)
            {
                return "UI.Text";
            }

            return component.GetType().Name;
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
