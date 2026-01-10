using System;
using System.Collections.Generic;
using System.Reflection;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TLDAccessibility.A11y.UI
{
    internal sealed class MenuHighlightTracker
    {
        private const string UIButtonColorTypeName = "UIButtonColor";
        private const string UIButtonTypeName = "UIButton";
        private const string MainMenuSceneName = "MainMenu";
        private const float PollIntervalSeconds = 0.15f;

        private static bool buttonColorTypeChecked;
        private static Type buttonColorType;
        private static bool buttonTypeChecked;
        private static Type buttonType;

        private readonly A11ySpeechService speechService;
        private float nextPollTime;
        private int lastSelectedInstanceId = -1;
        private string lastSelectedState;
        private string lastSelectedLabel;
        private int lastSpokenInstanceId = -1;
        private string lastSpokenLabel;
        private readonly HashSet<int> lastHoverInstanceIds = new HashSet<int>();

        private HighlightProbeSnapshot lastSnapshot = new HighlightProbeSnapshot("(null)", "(none)", "(none)");

        public MenuHighlightTracker(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public void Update()
        {
            if (Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            if (!IsMainMenuScene())
            {
                ResetSelection();
                return;
            }

            HighlightCandidate selected = SelectHighlightedCandidate(out HashSet<int> hoverIds);
            UpdateHoverCache(hoverIds);
            if (selected == null)
            {
                lastSnapshot = new HighlightProbeSnapshot("(null)", "(none)", "(none)");
                return;
            }

            string label = ResolveCandidateLabel(selected.Component);
            string normalizedLabel = VisibilityUtil.NormalizeText(label);
            string path = MenuProbe.BuildHierarchyPath(selected.Component.transform);
            lastSnapshot = new HighlightProbeSnapshot(path, selected.StateName, string.IsNullOrWhiteSpace(normalizedLabel) ? "(none)" : normalizedLabel);

            if (selected.InstanceId == lastSelectedInstanceId
                && string.Equals(selected.StateName, lastSelectedState, StringComparison.Ordinal))
            {
                return;
            }

            lastSelectedInstanceId = selected.InstanceId;
            lastSelectedState = selected.StateName;
            lastSelectedLabel = normalizedLabel;
            A11yLogger.Info($"Menu highlight: selectedPath={path}, state={selected.StateName}, label=\"{normalizedLabel}\"");

            if (!string.IsNullOrWhiteSpace(normalizedLabel)
                && (selected.InstanceId != lastSpokenInstanceId || !string.Equals(normalizedLabel, lastSpokenLabel, StringComparison.Ordinal)))
            {
                speechService?.Speak(normalizedLabel, A11ySpeechPriority.Normal, "menu_highlight", true);
                lastSpokenInstanceId = selected.InstanceId;
                lastSpokenLabel = normalizedLabel;
            }
        }

        public void LogDiagnostics()
        {
            HighlightProbeSnapshot snapshot = CaptureProbeSnapshot();
            A11yLogger.Info($"MenuProbe NGUI highlight: selectedPath={snapshot.SelectedPath}, state={snapshot.State}, label=\"{snapshot.LabelText}\"");
        }

        public HighlightProbeSnapshot CaptureProbeSnapshot()
        {
            return lastSnapshot;
        }

        private static bool IsMainMenuScene()
        {
            string sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            return sceneName.IndexOf(MainMenuSceneName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ResetSelection()
        {
            lastSelectedInstanceId = -1;
            lastSelectedState = null;
            lastSelectedLabel = null;
            lastSpokenInstanceId = -1;
            lastSpokenLabel = null;
            lastHoverInstanceIds.Clear();
            lastSnapshot = new HighlightProbeSnapshot("(null)", "(none)", "(none)");
        }

        private HighlightCandidate SelectHighlightedCandidate(out HashSet<int> hoverIds)
        {
            hoverIds = new HashSet<int>();
            Type candidateType = GetHighlightType();
            if (candidateType == null)
            {
                return null;
            }

            UnityEngine.Object[] components = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(candidateType));
            if (components == null || components.Length == 0)
            {
                return null;
            }

            List<HighlightCandidate> pressed = new List<HighlightCandidate>();
            List<HighlightCandidate> hovered = new List<HighlightCandidate>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i] as Component;
                if (component == null || component.gameObject == null || !component.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!IsHighlightComponent(component))
                {
                    continue;
                }

                if (!TryGetHighlightState(component, out string stateName))
                {
                    continue;
                }

                string normalizedState = stateName ?? string.Empty;
                bool isPressed = string.Equals(normalizedState, "Pressed", StringComparison.OrdinalIgnoreCase);
                bool isHover = string.Equals(normalizedState, "Hover", StringComparison.OrdinalIgnoreCase);
                if (!isPressed && !isHover)
                {
                    continue;
                }

                HighlightCandidate candidate = new HighlightCandidate(component, normalizedState, isPressed, isHover, CalculateDepth(component.transform));
                if (isPressed)
                {
                    pressed.Add(candidate);
                }
                else
                {
                    hovered.Add(candidate);
                    hoverIds.Add(candidate.InstanceId);
                }
            }

            HighlightCandidate best = ChooseBestCandidate(pressed, hovered);
            return best;
        }

        private HighlightCandidate ChooseBestCandidate(List<HighlightCandidate> pressed, List<HighlightCandidate> hovered)
        {
            if (pressed.Count > 0)
            {
                pressed.Sort(CompareByDepth);
                return pressed[0];
            }

            if (hovered.Count == 0)
            {
                return null;
            }

            HighlightCandidate bestHover = null;
            for (int i = 0; i < hovered.Count; i++)
            {
                HighlightCandidate candidate = hovered[i];
                if (!lastHoverInstanceIds.Contains(candidate.InstanceId))
                {
                    if (bestHover == null || CompareByDepth(candidate, bestHover) < 0)
                    {
                        bestHover = candidate;
                    }
                }
            }

            if (bestHover != null)
            {
                return bestHover;
            }

            hovered.Sort(CompareByDepth);
            return hovered[0];
        }

        private static int CompareByDepth(HighlightCandidate left, HighlightCandidate right)
        {
            if (left == null && right == null)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int depthCompare = right.Depth.CompareTo(left.Depth);
            if (depthCompare != 0)
            {
                return depthCompare;
            }

            return right.InstanceId.CompareTo(left.InstanceId);
        }

        private void UpdateHoverCache(HashSet<int> hoverIds)
        {
            lastHoverInstanceIds.Clear();
            if (hoverIds == null)
            {
                return;
            }

            foreach (int id in hoverIds)
            {
                lastHoverInstanceIds.Add(id);
            }
        }

        private static bool TryGetHighlightState(Component component, out string stateName)
        {
            stateName = null;
            if (component == null)
            {
                return false;
            }

            Type runtimeType = component.GetType();
            if (runtimeType == null)
            {
                return false;
            }

            PropertyInfo stateProperty = runtimeType.GetProperty("state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateProperty != null)
            {
                object value = stateProperty.GetValue(component);
                stateName = value != null ? value.ToString() : null;
                return !string.IsNullOrWhiteSpace(stateName);
            }

            FieldInfo stateField = runtimeType.GetField("mState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateField != null)
            {
                object value = stateField.GetValue(component);
                stateName = value != null ? value.ToString() : null;
                return !string.IsNullOrWhiteSpace(stateName);
            }

            MethodInfo method = runtimeType.GetMethod("GetState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                method = runtimeType.GetMethod("get_state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (method != null && method.GetParameters().Length == 0)
            {
                object value = method.Invoke(component, null);
                stateName = value != null ? value.ToString() : null;
                return !string.IsNullOrWhiteSpace(stateName);
            }

            return false;
        }

        private static bool IsHighlightComponent(Component component)
        {
            string name = GetComponentTypeName(component);
            if (string.Equals(name, UIButtonColorTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(name, UIButtonTypeName, StringComparison.Ordinal);
        }

        private static Type GetHighlightType()
        {
            Type colorType = GetButtonColorType();
            if (colorType != null)
            {
                return colorType;
            }

            return GetButtonType();
        }

        private static Type GetButtonColorType()
        {
            if (buttonColorTypeChecked)
            {
                return buttonColorType;
            }

            buttonColorTypeChecked = true;
            buttonColorType = FindTypeByName($"Il2Cpp.{UIButtonColorTypeName}") ?? FindTypeByName(UIButtonColorTypeName);
            return buttonColorType;
        }

        private static Type GetButtonType()
        {
            if (buttonTypeChecked)
            {
                return buttonType;
            }

            buttonTypeChecked = true;
            buttonType = FindTypeByName($"Il2Cpp.{UIButtonTypeName}") ?? FindTypeByName(UIButtonTypeName);
            return buttonType;
        }

        private static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                {
                    continue;
                }

                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string ResolveCandidateLabel(Component component)
        {
            if (component == null)
            {
                return null;
            }

            string localizeTerm = null;
            string bestProcessed = null;
            string bestRaw = null;
            Component[] components = component.gameObject.GetComponentsInChildren<Component>(false);
            for (int i = 0; i < components.Length; i++)
            {
                Component candidate = components[i];
                if (candidate == null || !NguiReflection.IsLabel(candidate))
                {
                    continue;
                }

                if (NguiReflection.TryGetUILabelTextDetails(candidate, out string rawText, out string processedText, out string labelLocalizeTerm))
                {
                    if (!string.IsNullOrWhiteSpace(labelLocalizeTerm) && string.IsNullOrWhiteSpace(localizeTerm))
                    {
                        localizeTerm = labelLocalizeTerm;
                    }

                    string candidateProcessed = VisibilityUtil.NormalizeText(processedText);
                    string candidateRaw = VisibilityUtil.NormalizeText(rawText);
                    if (!string.IsNullOrWhiteSpace(candidateProcessed) && !IsPlaceholderText(candidateProcessed))
                    {
                        bestProcessed = candidateProcessed;
                        break;
                    }

                    if (bestProcessed == null && !string.IsNullOrWhiteSpace(candidateProcessed))
                    {
                        bestProcessed = candidateProcessed;
                    }

                    if (bestRaw == null && !string.IsNullOrWhiteSpace(candidateRaw) && !IsPlaceholderText(candidateRaw))
                    {
                        bestRaw = candidateRaw;
                    }

                    if (bestRaw == null && !string.IsNullOrWhiteSpace(candidateRaw))
                    {
                        bestRaw = candidateRaw;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(bestProcessed))
            {
                return bestProcessed;
            }

            if (!string.IsNullOrWhiteSpace(bestRaw))
            {
                return bestRaw;
            }

            return localizeTerm;
        }

        private static bool IsPlaceholderText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string trimmed = text.Trim();
            string upper = trimmed.ToUpperInvariant();
            if (upper == "TEXT")
            {
                return true;
            }

            if (upper.StartsWith("TEXT", StringComparison.Ordinal))
            {
                bool digitsOnly = true;
                for (int i = 4; i < upper.Length; i++)
                {
                    if (!char.IsDigit(upper[i]))
                    {
                        digitsOnly = false;
                        break;
                    }
                }

                if (digitsOnly)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CalculateDepth(Transform transform)
        {
            int depth = 0;
            Transform current = transform;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private static string GetComponentTypeName(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            Il2CppSystem.Type il2CppType = component.GetIl2CppType();
            if (il2CppType != null && !string.IsNullOrWhiteSpace(il2CppType.Name))
            {
                return il2CppType.Name;
            }

            return component.GetType().Name ?? string.Empty;
        }

        internal sealed class HighlightProbeSnapshot
        {
            public HighlightProbeSnapshot(string selectedPath, string state, string labelText)
            {
                SelectedPath = selectedPath;
                State = state;
                LabelText = labelText;
            }

            public string SelectedPath { get; }
            public string State { get; }
            public string LabelText { get; }
        }

        private sealed class HighlightCandidate
        {
            public HighlightCandidate(Component component, string stateName, bool isPressed, bool isHover, int depth)
            {
                Component = component;
                StateName = stateName;
                IsPressed = isPressed;
                IsHover = isHover;
                Depth = depth;
                InstanceId = component != null ? component.GetInstanceID() : 0;
            }

            public Component Component { get; }
            public string StateName { get; }
            public bool IsPressed { get; }
            public bool IsHover { get; }
            public int Depth { get; }
            public int InstanceId { get; }
        }
    }
}
