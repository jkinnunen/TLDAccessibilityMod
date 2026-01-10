using System;
using System.Collections.Generic;
using System.Reflection;
using TLDAccessibility;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using UnityEngine;
using UnityEngine.SceneManagement;

// Changelog: Added menu context tracking, debounced highlight narration, menu title narration, and richer diagnostics to improve reliability.
namespace TLDAccessibility.A11y.UI
{
    internal sealed class MenuHighlightTracker
    {
        private const string UIButtonColorTypeName = "UIButtonColor";
        private const string UIButtonTypeName = "UIButton";
        private const string UIToggleTypeName = "UIToggle";
        private const string UISliderTypeName = "UISlider";
        private const string UIPopupListTypeName = "UIPopupList";
        private const string UISelectionListTypeName = "UISelectionList";
        private const string MainMenuSceneName = "MainMenu";
        private const float PollIntervalSeconds = 0.15f;
        private const float HighlightDebounceSeconds = 0.15f;
        private const float HeartbeatIntervalSeconds = 10f;
        private const float MenuReadyDelaySeconds = 0.5f;
        private const float TitleDebounceSeconds = 0.15f;
        private static readonly HotkeyBinding StateDumpHotkey = new HotkeyBinding(KeyCode.F12, true, true, true);

        private static bool buttonColorTypeChecked;
        private static Type buttonColorType;
        private static bool buttonTypeChecked;
        private static Type buttonType;
        private static bool toggleTypeChecked;
        private static Type toggleType;
        private static bool sliderTypeChecked;
        private static Type sliderType;
        private static bool popupListTypeChecked;
        private static Type popupListType;
        private static bool selectionListTypeChecked;
        private static Type selectionListType;
        private static readonly HashSet<string> reflectionFailureLogged = new HashSet<string>();

        private readonly A11ySpeechService speechService;
        private float nextPollTime;
        private float nextHeartbeatTime;
        private int lastSelectedInstanceId = -1;
        private string lastSelectedState;
        private string lastSelectedLabel;
        private string lastSelectedPath;
        private Component lastSelectedComponent;
        private string lastSpokenLabel;
        private string lastSpokenPath;
        private string lastSpokenPanelId;
        private float lastSpokenTime;
        private readonly HashSet<int> lastHoverInstanceIds = new HashSet<int>();
        private bool wasInMainMenu;

        private string currentPanelId = "(unknown)";
        private string lastPanelId = "(unknown)";
        private float panelEnterTime;
        private bool menuReady;
        private bool menuTitleSpoken;

        private PendingHighlight pendingHighlight;
        private float pendingHighlightReadyTime;
        private string pendingTitleText;
        private float pendingTitleReadyTime;

        private int suppressedDuplicateCount;
        private int suppressedThrottleCount;
        private int suppressedNotReadyCount;
        private int suppressedEngineFailCount;

        private HighlightProbeSnapshot lastSnapshot = new HighlightProbeSnapshot("(null)", "(none)", "(none)");

        public MenuHighlightTracker(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            HandleStateDumpHotkey(now);
            UpdateMenuReady(now);
            ProcessPendingSpeech(now);

            if (now < nextPollTime)
            {
                return;
            }

            nextPollTime = now + PollIntervalSeconds;
            try
            {
                LogHeartbeatIfNeeded();

                bool isMainMenu = IsMainMenuScene();
                if (!isMainMenu)
                {
                    ResetSelection();
                    return;
                }

                if (!wasInMainMenu)
                {
                    wasInMainMenu = true;
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

                UpdateMenuContext(path, now);

                if (selected.InstanceId == lastSelectedInstanceId
                    && string.Equals(selected.StateName, lastSelectedState, StringComparison.Ordinal))
                {
                    return;
                }

                lastSelectedInstanceId = selected.InstanceId;
                lastSelectedState = selected.StateName;
                lastSelectedLabel = normalizedLabel;
                lastSelectedPath = path;
                lastSelectedComponent = selected.Component;

                A11yLogger.Info($"Menu highlight: selectedPath={path}, state={selected.StateName}, label=\"{normalizedLabel}\"");

                ScheduleHighlight(normalizedLabel, path, selected.StateName, selected.Component, now);
            }
            catch (Exception ex)
            {
                A11yLogger.Warning($"Menu highlight poll failed: {ex}");
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
            lastSelectedPath = null;
            lastSelectedComponent = null;
            lastSpokenLabel = null;
            lastSpokenPath = null;
            lastSpokenPanelId = null;
            lastSpokenTime = 0f;
            lastHoverInstanceIds.Clear();
            lastSnapshot = new HighlightProbeSnapshot("(null)", "(none)", "(none)");
            wasInMainMenu = false;
            currentPanelId = "(unknown)";
            lastPanelId = "(unknown)";
            panelEnterTime = 0f;
            menuReady = false;
            menuTitleSpoken = false;
            pendingHighlight = null;
            pendingHighlightReadyTime = 0f;
            pendingTitleText = null;
            pendingTitleReadyTime = 0f;
            ResetSuppressionCounters();
        }

        private void ResetSuppressionCounters()
        {
            suppressedDuplicateCount = 0;
            suppressedThrottleCount = 0;
            suppressedNotReadyCount = 0;
            suppressedEngineFailCount = 0;
        }

        private void LogHeartbeatIfNeeded()
        {
            if (Settings.Instance.Verbosity != VerbosityLevel.Detailed)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < nextHeartbeatTime)
            {
                return;
            }

            nextHeartbeatTime = now + HeartbeatIntervalSeconds;
            string sceneName = SceneManager.GetActiveScene().name ?? string.Empty;
            A11yLogger.Info($"Menu highlight heartbeat: scene={sceneName}, time={now:0.###}");
        }

        private void UpdateMenuContext(string selectedPath, float now)
        {
            string panelId = ExtractPanelId(selectedPath);
            if (string.Equals(panelId, currentPanelId, StringComparison.Ordinal))
            {
                return;
            }

            lastPanelId = currentPanelId;
            currentPanelId = panelId;
            panelEnterTime = now;
            menuReady = false;
            menuTitleSpoken = false;
            lastSpokenLabel = null;
            lastSpokenPath = null;
            lastSpokenPanelId = null;
            lastSpokenTime = 0f;
            pendingHighlight = null;
            pendingHighlightReadyTime = 0f;
            pendingTitleText = null;
            pendingTitleReadyTime = 0f;
            ResetSuppressionCounters();
            speechService?.ClearQueue("menu_context_change");
            A11yLogger.Info($"MENU CONTEXT ENTER panel={currentPanelId} from={lastPanelId}");
        }

        private void UpdateMenuReady(float now)
        {
            if (menuReady)
            {
                return;
            }

            if (panelEnterTime <= 0f)
            {
                return;
            }

            if (IsMenuReadyInputPressed() || now - panelEnterTime >= MenuReadyDelaySeconds)
            {
                menuReady = true;
                A11yLogger.Info($"Menu ready: panel={currentPanelId}, elapsed={now - panelEnterTime:0.###}s");
                ScheduleMenuTitle(now);
                if (!string.IsNullOrWhiteSpace(lastSelectedLabel) && !string.IsNullOrWhiteSpace(lastSelectedPath))
                {
                    ScheduleHighlight(lastSelectedLabel, lastSelectedPath, lastSelectedState, lastSelectedComponent, now);
                }
            }
        }

        private static bool IsMenuReadyInputPressed()
        {
            return Input.GetKeyDown(KeyCode.UpArrow)
                || Input.GetKeyDown(KeyCode.DownArrow)
                || Input.GetKeyDown(KeyCode.LeftArrow)
                || Input.GetKeyDown(KeyCode.RightArrow)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Escape);
        }

        private void ScheduleHighlight(string label, string path, string state, Component component, float now)
        {
            pendingHighlight = new PendingHighlight(label, path, state, currentPanelId, component, now);
            pendingHighlightReadyTime = now + HighlightDebounceSeconds;
        }

        private void ScheduleMenuTitle(float now)
        {
            if (menuTitleSpoken)
            {
                return;
            }

            string title = GetMenuTitleForPanel(currentPanelId, lastSelectedPath, lastSelectedLabel);
            if (string.IsNullOrWhiteSpace(title))
            {
                menuTitleSpoken = true;
                return;
            }

            pendingTitleText = title;
            pendingTitleReadyTime = now + TitleDebounceSeconds;
        }

        private void ProcessPendingSpeech(float now)
        {
            if (!menuReady)
            {
                if (pendingTitleText != null && now >= pendingTitleReadyTime)
                {
                    suppressedNotReadyCount++;
                    LogMenuSpeakDecision(false, "SUPPRESSED_MENU_NOT_READY", pendingTitleText, currentPanelId, "(title)", "title", true);
                    pendingTitleText = null;
                }

                if (pendingHighlight != null && now >= pendingHighlightReadyTime)
                {
                    suppressedNotReadyCount++;
                    LogMenuSpeakDecision(false, "SUPPRESSED_MENU_NOT_READY", pendingHighlight.Label, pendingHighlight.PanelId, pendingHighlight.Path, pendingHighlight.State, true);
                    pendingHighlight = null;
                }

                return;
            }

            if (pendingTitleText != null && now >= pendingTitleReadyTime)
            {
                TrySpeakMenuTitle(pendingTitleText, now);
                pendingTitleText = null;
            }

            if (pendingHighlight != null && now >= pendingHighlightReadyTime)
            {
                TrySpeakPendingHighlight(pendingHighlight, now);
                pendingHighlight = null;
            }
        }

        private void TrySpeakMenuTitle(string title, float now)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                LogMenuSpeakDecision(false, "EmptyTitle", title, currentPanelId, "(title)", "title", false);
                return;
            }

            if (speechService == null || !speechService.IsAvailable)
            {
                LogMenuSpeakDecision(false, "SpeechDisabled", title, currentPanelId, "(title)", "title", false);
                return;
            }

            bool accepted = speechService.TrySpeak(
                title,
                A11ySpeechPriority.Normal,
                "menu_title",
                true,
                out A11ySpeechService.SpeechSuppressionReason suppressionReason,
                new A11ySpeechService.SpeechRequestOptions
                {
                    BypassCooldown = true,
                    BypassAutoRateLimit = true
                });

            if (!accepted)
            {
                if (suppressionReason == A11ySpeechService.SpeechSuppressionReason.Exception)
                {
                    suppressedEngineFailCount++;
                }

                LogMenuSpeakDecision(false, suppressionReason.ToString(), title, currentPanelId, "(title)", "title", false);
                return;
            }

            menuTitleSpoken = true;
            LogMenuSpeakDecision(true, "Accepted", title, currentPanelId, "(title)", "title", false);
        }

        private void TrySpeakPendingHighlight(PendingHighlight pending, float now)
        {
            if (pending == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pending.Label))
            {
                LogMenuSpeakDecision(false, "EmptyText", pending.Label, pending.PanelId, pending.Path, pending.State, true);
                return;
            }

            if (ShouldSuppressDuplicate(pending.Label, pending.Path, pending.PanelId, now))
            {
                suppressedDuplicateCount++;
                LogMenuSpeakDecision(false, "Duplicate", pending.Label, pending.PanelId, pending.Path, pending.State, true);
                return;
            }

            if (speechService == null || !speechService.IsAvailable)
            {
                LogMenuSpeakDecision(false, "SpeechDisabled", pending.Label, pending.PanelId, pending.Path, pending.State, true);
                return;
            }

            string narration = ResolveHighlightNarration(pending);
            if (string.IsNullOrWhiteSpace(narration))
            {
                LogMenuSpeakDecision(false, "EmptyNarration", pending.Label, pending.PanelId, pending.Path, pending.State, true);
                return;
            }

            bool accepted = speechService.TrySpeak(
                narration,
                A11ySpeechPriority.Normal,
                "menu_highlight",
                true,
                out A11ySpeechService.SpeechSuppressionReason suppressionReason,
                new A11ySpeechService.SpeechRequestOptions
                {
                    BypassCooldown = true,
                    BypassAutoRateLimit = true
                });

            if (!accepted)
            {
                if (suppressionReason == A11ySpeechService.SpeechSuppressionReason.Exception)
                {
                    suppressedEngineFailCount++;
                }

                LogMenuSpeakDecision(false, suppressionReason.ToString(), narration, pending.PanelId, pending.Path, pending.State, true);
                return;
            }

            lastSpokenLabel = pending.Label;
            lastSpokenPath = pending.Path;
            lastSpokenPanelId = pending.PanelId;
            lastSpokenTime = now;
            LogMenuSpeakDecision(true, "Accepted", narration, pending.PanelId, pending.Path, pending.State, true);
        }

        private string ResolveHighlightNarration(PendingHighlight pending)
        {
            if (pending == null)
            {
                return null;
            }

            Component component = pending.Component;
            if (component == null)
            {
                return pending.Label;
            }

            GameObject target = component.gameObject;
            if (target == null)
            {
                return pending.Label;
            }

            if (TryBuildToggleNarration(target, pending.Label, out string narration))
            {
                return narration;
            }

            if (TryBuildSliderNarration(target, pending.Label, out narration))
            {
                return narration;
            }

            if (TryBuildListNarration(target, pending.Label, out narration))
            {
                return narration;
            }

            return pending.Label;
        }

        private bool TryBuildToggleNarration(GameObject target, string fallbackLabel, out string narration)
        {
            narration = null;
            Component toggle = FindComponentInParents(target, GetToggleType());
            if (toggle == null)
            {
                return false;
            }

            bool? isOn = TryReadBool(toggle, "value", "isChecked", "isOn", "mIsChecked");
            if (!isOn.HasValue)
            {
                return false;
            }

            string name = ResolveControlName(toggle.gameObject, fallbackLabel);
            string state = isOn.Value ? "on" : "off";
            narration = string.IsNullOrWhiteSpace(name) ? state : $"{name}: {state}";
            return true;
        }

        private bool TryBuildSliderNarration(GameObject target, string fallbackLabel, out string narration)
        {
            narration = null;
            Component slider = FindComponentInParents(target, GetSliderType());
            if (slider == null)
            {
                return false;
            }

            float? value = TryReadFloat(slider, "value", "rawValue", "sliderValue");
            if (!value.HasValue)
            {
                return false;
            }

            int percent = NormalizePercent(value.Value);
            string name = ResolveControlName(slider.gameObject, fallbackLabel);
            narration = string.IsNullOrWhiteSpace(name)
                ? $"{percent} percent"
                : $"{name}: {percent} percent";
            return true;
        }

        private bool TryBuildListNarration(GameObject target, string fallbackLabel, out string narration)
        {
            narration = null;
            Component popup = FindComponentInParents(target, GetPopupListType())
                ?? FindComponentInParents(target, GetSelectionListType());
            if (popup == null)
            {
                return false;
            }

            string value = TryReadString(popup, "value", "selection", "selectedValue", "current", "currentSelection");
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = VisibilityUtil.NormalizeText(value);
            string name = ResolveControlName(popup.gameObject, fallbackLabel);
            narration = string.IsNullOrWhiteSpace(name) ? value : $"{name}: {value}";
            return true;
        }

        private static int NormalizePercent(float value)
        {
            if (value < 0f)
            {
                return 0;
            }

            if (value <= 1.0f)
            {
                return Mathf.RoundToInt(value * 100f);
            }

            if (value <= 100f)
            {
                return Mathf.RoundToInt(value);
            }

            return Mathf.RoundToInt(Mathf.Clamp(value, 0f, 100f));
        }

        private string ResolveControlName(GameObject target, string fallbackLabel)
        {
            string normalizedFallback = VisibilityUtil.NormalizeText(fallbackLabel);
            string name = FindLabelCandidate(target, normalizedFallback);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (target.transform.parent != null)
            {
                Transform parent = target.transform.parent;
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (sibling == null || sibling.gameObject == target)
                    {
                        continue;
                    }

                    name = FindLabelCandidate(sibling.gameObject, normalizedFallback);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }

            return string.IsNullOrWhiteSpace(normalizedFallback) ? null : normalizedFallback;
        }

        private string FindLabelCandidate(GameObject root, string excludeLabel)
        {
            if (root == null)
            {
                return null;
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            string best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < components.Length; i++)
            {
                Component candidate = components[i];
                if (candidate == null || !NguiReflection.IsLabel(candidate))
                {
                    continue;
                }

                if (!NguiReflection.TryGetUILabelTextDetails(candidate, out string rawText, out string processedText, out string localizeTerm))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(!string.IsNullOrWhiteSpace(processedText) ? processedText : rawText);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = VisibilityUtil.NormalizeText(localizeTerm);
                }

                if (string.IsNullOrWhiteSpace(text) || IsPlaceholderText(text))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(excludeLabel) && string.Equals(text, excludeLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                string name = candidate.gameObject.name ?? string.Empty;
                int score = 0;
                if (name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 10;
                }

                if (name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 5;
                }

                score -= text.Length;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = text;
                }
            }

            return best;
        }

        private static Component FindComponentInParents(GameObject target, Type type)
        {
            if (target == null || type == null)
            {
                return null;
            }

            Transform current = target.transform;
            int depth = 0;
            while (current != null && depth < 5)
            {
                Component component = current.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(type));
                if (component != null)
                {
                    return component;
                }

                current = current.parent;
                depth++;
            }

            return null;
        }

        private static bool? TryReadBool(Component component, params string[] memberNames)
        {
            if (component == null)
            {
                return null;
            }

            Type type = component.GetType();
            for (int i = 0; i < memberNames.Length; i++)
            {
                string name = memberNames[i];
                try
                {
                    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.PropertyType == typeof(bool))
                    {
                        return (bool)property.GetValue(component, null);
                    }

                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        return (bool)field.GetValue(component);
                    }
                }
                catch (Exception ex)
                {
                    LogReflectionFailure(type, name, ex);
                    return null;
                }
            }

            return null;
        }

        private static float? TryReadFloat(Component component, params string[] memberNames)
        {
            if (component == null)
            {
                return null;
            }

            Type type = component.GetType();
            for (int i = 0; i < memberNames.Length; i++)
            {
                string name = memberNames[i];
                try
                {
                    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.PropertyType == typeof(float))
                    {
                        return (float)property.GetValue(component, null);
                    }

                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(float))
                    {
                        return (float)field.GetValue(component);
                    }
                }
                catch (Exception ex)
                {
                    LogReflectionFailure(type, name, ex);
                    return null;
                }
            }

            return null;
        }

        private static string TryReadString(Component component, params string[] memberNames)
        {
            if (component == null)
            {
                return null;
            }

            Type type = component.GetType();
            for (int i = 0; i < memberNames.Length; i++)
            {
                string name = memberNames[i];
                try
                {
                    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        return property.GetValue(component, null) as string;
                    }

                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        return field.GetValue(component) as string;
                    }
                }
                catch (Exception ex)
                {
                    LogReflectionFailure(type, name, ex);
                    return null;
                }
            }

            return null;
        }

        private static void LogReflectionFailure(Type type, string memberName, Exception ex)
        {
            string key = $"{type?.FullName ?? "(null)"}.{memberName}";
            if (!reflectionFailureLogged.Add(key))
            {
                return;
            }

            A11yLogger.Warning($"Menu highlight reflection failed: {key} ex={ex.GetType().Name}: {ex.Message}");
        }

        private static Type GetToggleType()
        {
            if (toggleTypeChecked)
            {
                return toggleType;
            }

            toggleTypeChecked = true;
            toggleType = FindTypeByName($"Il2Cpp.{UIToggleTypeName}") ?? FindTypeByName(UIToggleTypeName);
            return toggleType;
        }

        private static Type GetSliderType()
        {
            if (sliderTypeChecked)
            {
                return sliderType;
            }

            sliderTypeChecked = true;
            sliderType = FindTypeByName($"Il2Cpp.{UISliderTypeName}") ?? FindTypeByName(UISliderTypeName);
            return sliderType;
        }

        private static Type GetPopupListType()
        {
            if (popupListTypeChecked)
            {
                return popupListType;
            }

            popupListTypeChecked = true;
            popupListType = FindTypeByName($"Il2Cpp.{UIPopupListTypeName}") ?? FindTypeByName(UIPopupListTypeName);
            return popupListType;
        }

        private static Type GetSelectionListType()
        {
            if (selectionListTypeChecked)
            {
                return selectionListType;
            }

            selectionListTypeChecked = true;
            selectionListType = FindTypeByName($"Il2Cpp.{UISelectionListTypeName}") ?? FindTypeByName(UISelectionListTypeName);
            return selectionListType;
        }

        private static string ExtractPanelId(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return "(unknown)";
            }

            string[] segments = selectedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.StartsWith("Panel_", StringComparison.Ordinal))
                {
                    return segment;
                }
            }

            return "(unknown)";
        }

        private string GetMenuTitleForPanel(string panelId, string selectedPath, string selectedLabel)
        {
            _ = selectedPath;
            if (string.IsNullOrWhiteSpace(panelId) || panelId == "(unknown)")
            {
                return null;
            }

            Transform panelRoot = FindPanelRoot(panelId);
            if (panelRoot == null)
            {
                return null;
            }

            Component[] components = panelRoot.GetComponentsInChildren<Component>(true);
            List<TitleCandidate> candidates = new List<TitleCandidate>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !NguiReflection.IsLabel(component))
                {
                    continue;
                }

                if (!NguiReflection.TryGetUILabelTextDetails(component, out string rawText, out string processedText, out string localizeTerm))
                {
                    continue;
                }

                string text = VisibilityUtil.NormalizeText(!string.IsNullOrWhiteSpace(processedText) ? processedText : rawText);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = VisibilityUtil.NormalizeText(localizeTerm);
                }

                if (string.IsNullOrWhiteSpace(text) || IsPlaceholderText(text))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(selectedLabel) && string.Equals(text, selectedLabel, StringComparison.Ordinal))
                {
                    continue;
                }

                string name = component.gameObject.name ?? string.Empty;
                int score = 0;
                if (name.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("MenuHeader", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Label_Title", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("PanelHeader", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 50;
                }

                if (text.Length <= 60)
                {
                    score += 10;
                }

                score -= text.Length;
                candidates.Add(new TitleCandidate(text, score, name));
            }

            if (candidates.Count == 0)
            {
#if DEBUG
                A11yLogger.Info($"TITLE_NOT_FOUND panel={panelId}");
#endif
                return null;
            }

            candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
            string title = candidates[0].Text;
#if DEBUG
            if (string.IsNullOrWhiteSpace(title))
            {
                int limit = Math.Min(5, candidates.Count);
                for (int i = 0; i < limit; i++)
                {
                    TitleCandidate candidate = candidates[i];
                    A11yLogger.Info($"TITLE_CANDIDATE panel={panelId} text=\"{candidate.Text}\" name={candidate.Name} score={candidate.Score}");
                }
            }
#endif
            return title;
        }

        private static Transform FindPanelRoot(string panelId)
        {
            if (string.IsNullOrWhiteSpace(panelId) || panelId == "(unknown)")
            {
                return null;
            }

            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            if (transforms == null || transforms.Length == 0)
            {
                return null;
            }

            Transform best = null;
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null || candidate.name != panelId)
                {
                    continue;
                }

                if (candidate.gameObject != null && candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }

                if (best == null)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private void HandleStateDumpHotkey(float now)
        {
            if (!HotkeyUtil.IsPressed(StateDumpHotkey))
            {
                return;
            }

            int queueCount = speechService?.QueueCount ?? 0;
            float lastOkTime = speechService?.LastOutputOkTime ?? -1f;
            float lastOkAgeMs = lastOkTime > 0f ? (now - lastOkTime) * 1000f : -1f;
            string lastError = speechService?.LastOutputError ?? string.Empty;
            string pendingLabel = pendingHighlight?.Label ?? string.Empty;
            string lastHighlight = lastSelectedLabel ?? string.Empty;
            A11yLogger.Info(
                $"A11Y STATE DUMP panel={currentPanelId} menuReady={menuReady} titleSpoken={menuTitleSpoken} " +
                $"lastHighlight=\"{lastHighlight}\" pending=\"{pendingLabel}\" q={queueCount} " +
                $"lastOutOkAgeMs={lastOkAgeMs:0} lastOutErr=\"{lastError}\" " +
                $"suppressed={{dup:{suppressedDuplicateCount}, thr:{suppressedThrottleCount}, notReady:{suppressedNotReadyCount}, eng:{suppressedEngineFailCount}}}");
        }

        private bool ShouldSuppressDuplicate(string label, string path, string panelId, float now)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return true;
            }

            if (!string.Equals(panelId, lastSpokenPanelId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(label, lastSpokenLabel, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(path, lastSpokenPath, StringComparison.Ordinal))
            {
                return false;
            }

            return now - lastSpokenTime < HighlightDebounceSeconds;
        }

        private void LogMenuSpeakDecision(bool accepted, string reason, string label, string panelId, string path, string state, bool pending)
        {
            string safeLabel = string.IsNullOrWhiteSpace(label) ? "(none)" : label;
            string safePath = string.IsNullOrWhiteSpace(path) ? "(null)" : path;
            string safeState = string.IsNullOrWhiteSpace(state) ? "(null)" : state;
            string safePanel = string.IsNullOrWhiteSpace(panelId) ? "(unknown)" : panelId;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
            int queueCount = speechService?.QueueCount ?? 0;
            A11yLogger.Info(
                $"MenuSpeak DECISION accept={accepted.ToString().ToLowerInvariant()} reason={safeReason} panel={safePanel} label=\"{safeLabel}\" path={safePath} state={safeState} pending={pending.ToString().ToLowerInvariant()} q={queueCount}");
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

        private sealed class PendingHighlight
        {
            public PendingHighlight(string label, string path, string state, string panelId, Component component, float detectedTime)
            {
                Label = label;
                Path = path;
                State = state;
                PanelId = panelId;
                Component = component;
                DetectedTime = detectedTime;
            }

            public string Label { get; }
            public string Path { get; }
            public string State { get; }
            public string PanelId { get; }
            public Component Component { get; }
            public float DetectedTime { get; }
        }

        private readonly struct TitleCandidate
        {
            public TitleCandidate(string text, int score, string name)
            {
                Text = text;
                Score = score;
                Name = name;
            }

            public string Text { get; }
            public int Score { get; }
            public string Name { get; }
        }
    }
}
