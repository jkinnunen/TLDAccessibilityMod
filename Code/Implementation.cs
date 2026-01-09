using HarmonyLib;
using MelonLoader;
using TLDAccessibility.A11y.Capture;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using TLDAccessibility.A11y.UI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace TLDAccessibility
{
    internal class Implementation : MelonMod
    {
        private A11ySpeechService speechService;
        private FocusTracker focusTracker;
        private ScreenReviewController screenReview;
        private HarmonyLib.Harmony harmony;
        private bool loggedSettingsUnavailable;
        private Settings settings;
        private MenuProbe menuProbe;
        private MenuSelectionTracker menuSelectionTracker;
        private const int NguiLabelSnapshotLimit = 20;
        private static bool nguiLabelEntryExceptionLogged;

        public override void OnInitializeMelon()
        {
            A11yLogger.Initialize(LoggerInstance);
            LoggerInstance.Msg($"Version {BuildInfo.Version}");
            settings = Settings.Initialize();

            speechService = new A11ySpeechService();
            focusTracker = new FocusTracker(speechService);
            screenReview = new ScreenReviewController(speechService);
            TextChangeHandler.SpeechService = speechService;
            menuProbe = new MenuProbe();
            menuSelectionTracker = new MenuSelectionTracker(speechService);

            harmony = new HarmonyLib.Harmony("TLDAccessibility.A11y");
            FocusTracker.ApplyHarmonyPatches(harmony, focusTracker);
            TextChangePatches.Apply(harmony);
            MenuSelectionTracker.ApplyHarmonyPatches(harmony, menuSelectionTracker);

            A11yLogger.Info("Accessibility layer initialized.");
            A11yLogger.Info("Startup speech test: requesting output.");
            speechService.Speak("TLDAccessibility loaded. Speech test.", A11ySpeechPriority.Critical, "startup_test", false);
        }

        public override void OnUpdate()
        {
            speechService?.Update();
            focusTracker?.Update();
            TmpTextPolling.Update();

            HandleDebugHotkey();
            HandleHotkeys();
        }

        private void HandleDebugHotkey()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && alt && shift && Input.GetKeyDown(KeyCode.F10))
            {
                HandleMenuProbeSnapshotHotkey();
                return;
            }

            if (ctrl && alt && shift && Input.GetKeyDown(KeyCode.F11))
            {
                HandleSelectionSnapshotHotkey();
                return;
            }

            if (ctrl && alt && shift && Input.GetKeyDown(KeyCode.F12))
            {
                A11yLogger.Info("Debug speech hotkey pressed.");
                speechService?.Speak("TLDAccessibility debug hotkey speech test.", A11ySpeechPriority.Critical, "debug_hotkey", false);
            }
        }

        private void HandleMenuProbeSnapshotHotkey()
        {
            try
            {
                A11yLogger.Info("MenuProbe DEEP CENSUS MARKER v1 (Ctrl+Alt+Shift+F10 handler entered)");
                string sceneName = SceneManager.GetActiveScene().name;
                A11yLogger.Info($"MenuProbe marker: scene={sceneName} time={Time.unscaledTime}");

                if (menuProbe == null)
                {
                    return;
                }

                MenuProbe.SnapshotResult snapshot = menuProbe.Capture(true);
                MenuProbe.UiToolkitSnapshot uiToolkitSnapshot = snapshot.UiToolkitSnapshot;
                if (uiToolkitSnapshot != null && uiToolkitSnapshot.TextCount > 0)
                {
                    string firstText = uiToolkitSnapshot.FirstText;
                    string spokenText = string.IsNullOrWhiteSpace(firstText)
                        ? $"MenuProbe: UI Toolkit found {uiToolkitSnapshot.TextCount} text nodes."
                        : $"MenuProbe: UI Toolkit found {uiToolkitSnapshot.TextCount} text nodes. First: {firstText}";
                    speechService?.Speak(spokenText, A11ySpeechPriority.Critical, "menu_probe_ui_toolkit_snapshot", false);
                }
                LogMenuProbeDiagnostics();
                menuSelectionTracker?.LogDiagnostics();
                LogNguiUILabelSnapshot();
                if (snapshot.Candidates.Count == 0)
                {
                    A11yLogger.Info("MenuProbe snapshot: no visible text candidates.");
                    if (uiToolkitSnapshot == null || uiToolkitSnapshot.TextCount == 0)
                    {
                        A11yLogger.Info("MenuProbe snapshot: no UI Toolkit text candidates either.");
                    }
                    speechService?.Speak("MenuProbe: no visible text candidates", A11ySpeechPriority.Critical, "menu_probe_snapshot", false);
                    return;
                }

                List<(MenuProbe.CandidateSnapshot Candidate, float Score)> ranked = new List<(MenuProbe.CandidateSnapshot, float)>();
                foreach (MenuProbe.CandidateSnapshot candidate in snapshot.Candidates)
                {
                    ranked.Add((candidate, MenuProbe.CalculateProminenceScore(candidate)));
                }

                ranked.Sort((left, right) => right.Score.CompareTo(left.Score));
                MenuProbe.CandidateSnapshot topCandidate = ranked[0].Candidate;
                speechService?.Speak($"MenuProbe: top candidate: {topCandidate.LogText}", A11ySpeechPriority.Critical, "menu_probe_snapshot", false);

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("MenuProbe snapshot: top visible text candidates:");
                int limit = Mathf.Min(10, ranked.Count);
                for (int i = 0; i < limit; i++)
                {
                    MenuProbe.CandidateSnapshot candidate = ranked[i].Candidate;
                    Vector3 position = candidate.WorldPosition;
                    builder.Append($"{i + 1}. type={candidate.ComponentType}; ");
                    builder.Append($"gameObject={candidate.GameObjectName}; ");
                    builder.Append($"scene={candidate.SceneName}; ");
                    builder.Append($"text=\"{candidate.LogText}\"; ");
                    builder.Append($"activeInHierarchy={candidate.ActiveInHierarchy}; ");
                    builder.Append($"Path={candidate.HierarchyPath}; ");
                    builder.Append($"Alpha={candidate.Color.a:0.00}; ");
                    builder.Append($"FontSize={candidate.FontSize:0.0}; ");
                    builder.Append($"FontStyle={candidate.FontStyle}; ");
                    builder.AppendLine($"Position=({position.x:0.00}, {position.y:0.00}, {position.z:0.00})");
                }

                A11yLogger.Info(builder.ToString());
            }
            catch (Exception ex)
            {
                A11yLogger.Info($"MenuProbe marker: exception: {ex}");
            }
        }

        private void HandleSelectionSnapshotHotkey()
        {
            A11yLogger.Info("Selection snapshot hotkey pressed.");
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            GameObject nguiSelected = NguiReflection.GetSelectedOrHoveredObject();
            string nguiLabel = NguiReflection.ResolveLabelText(nguiSelected);

            string eventName = selected != null ? selected.name : "(null)";
            string eventPath = selected != null ? MenuProbe.BuildHierarchyPath(selected.transform) : "(null)";
            string nguiName = nguiSelected != null ? nguiSelected.name : "(null)";
            string nguiPath = nguiSelected != null ? MenuProbe.BuildHierarchyPath(nguiSelected.transform) : "(null)";
            string nguiLabelTrimmed = TrimSnapshotText(nguiLabel);

            A11yLogger.Info($"Selection snapshot: EventSystem selected={eventName}, path={eventPath}");
            A11yLogger.Info($"Selection snapshot: NGUI selected={nguiName}, path={nguiPath}, label=\"{nguiLabelTrimmed}\"");

            if (selected == null && nguiSelected == null)
            {
                A11yLogger.Info("Selection snapshot: No selected UI object.");
                speechService?.Speak("No selected UI object", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
                return;
            }

            if (selected != null)
            {
                string narration = focusTracker?.BuildUiSelectionNarration(selected);
                if (string.IsNullOrWhiteSpace(narration))
                {
                    narration = selected.name;
                }

                speechService?.Speak($"Selected: {narration}", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
                string details = BuildSelectionSnapshotDetails(selected, narration);
                A11yLogger.Info(details);
                return;
            }

            string nguiNarration = string.IsNullOrWhiteSpace(nguiLabel) ? nguiSelected.name : nguiLabel;
            speechService?.Speak($"Selected: {nguiNarration}", A11ySpeechPriority.Critical, "ui_selection_snapshot", false);
            string nguiDetails = BuildSelectionSnapshotDetails(nguiSelected, nguiNarration);
            A11yLogger.Info(nguiDetails);
        }

        private static string BuildSelectionSnapshotDetails(GameObject selected, string narration)
        {
            StringBuilder builder = new StringBuilder();
            string sceneName = selected.scene.IsValid() ? selected.scene.name : "(unknown)";
            builder.Append("Selection snapshot: ");
            builder.Append($"Name={selected.name}; ");
            builder.Append($"ActiveInHierarchy={selected.activeInHierarchy}; ");
            builder.Append($"Scene={sceneName}; ");

            Component[] components = selected.GetComponents<Component>();
            int limit = Mathf.Min(components.Length, 8);
            builder.Append("Components=[");
            int appended = 0;
            for (int i = 0; i < limit; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (appended > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(component.GetType().Name);
                appended++;
            }

            if (components.Length > limit)
            {
                builder.Append(", ...");
            }

            builder.Append("]; ");

            Component tmpText = TmpReflection.GetTmpTextComponent(selected);
            if (tmpText != null)
            {
                string text = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(tmpText));
                text = TrimSnapshotText(text);
                builder.Append($"TMP_Text=\"{text}\"; ");
            }

            builder.Append($"Narration=\"{narration}\"");
            return builder.ToString();
        }

        private static string TrimSnapshotText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            const int maxLength = 120;
            if (text.Length <= maxLength)
            {
                return text;
            }

            return $"{text.Substring(0, maxLength)}...";
        }

        private readonly struct NguiLabelEntry
        {
            public NguiLabelEntry(string text, string path)
            {
                Text = text;
                Path = path;
            }

            public string Text { get; }
            public string Path { get; }
        }

        private readonly struct NguiLabelRawEntry
        {
            public NguiLabelRawEntry(
                string path,
                bool activeInHierarchy,
                string enabledState,
                int rawLength,
                int processedLength,
                string sample,
                string localizeTerm)
            {
                Path = path;
                ActiveInHierarchy = activeInHierarchy;
                EnabledState = enabledState;
                RawLength = rawLength;
                ProcessedLength = processedLength;
                Sample = sample;
                LocalizeTerm = localizeTerm;
            }

            public string Path { get; }
            public bool ActiveInHierarchy { get; }
            public string EnabledState { get; }
            public int RawLength { get; }
            public int ProcessedLength { get; }
            public string Sample { get; }
            public string LocalizeTerm { get; }
        }

        private static void LogMenuProbeDiagnostics()
        {
            LogEventSystemDiagnostics();
            LogNguiSelectionDiagnostics();
            LogNguiUILabelRawDump();
            LogCameraCensus();
            List<Transform> transforms = FindAllTransforms();
            LogSceneDistribution(transforms);
            LogComponentHistogramAndKeywords(transforms);
        }

        private static void LogNguiSelectionDiagnostics()
        {
            NguiReflection.UiCameraStatus status = NguiReflection.GetUiCameraStatus();
            A11yLogger.Info(
                $"MenuProbe NGUI: UICamera type exists={status.TypeExists}, selectedReadable={status.SelectedReadable}, hoveredReadable={status.HoveredReadable}");

            GameObject selectedObject = NguiReflection.GetSelectedObject();
            GameObject hoveredObject = NguiReflection.GetHoveredObject();
            string selectedName = selectedObject != null ? selectedObject.name : "(null)";
            string selectedPath = selectedObject != null ? MenuProbe.BuildHierarchyPath(selectedObject.transform) : "(null)";
            string hoveredName = hoveredObject != null ? hoveredObject.name : "(null)";
            string hoveredPath = hoveredObject != null ? MenuProbe.BuildHierarchyPath(hoveredObject.transform) : "(null)";

            A11yLogger.Info($"MenuProbe NGUI: selectedObject={selectedName}, path={selectedPath}");
            A11yLogger.Info($"MenuProbe NGUI: hoveredObject={hoveredName}, path={hoveredPath}");

            if (hoveredObject != null)
            {
                LogNguiLabelSubtreeDump("hoveredObject", hoveredObject, NguiLabelSnapshotLimit);
            }

            GameObject resolved = selectedObject ?? hoveredObject;
            if (resolved == null)
            {
                A11yLogger.Info("MenuProbe NGUI: no selected or hovered object to scan for UILabel text.");
                return;
            }

            if (!NguiReflection.HasUILabel)
            {
                A11yLogger.Info("MenuProbe NGUI: UILabel binding not available.");
                return;
            }

            List<string> labelTexts = CollectNguiLabelTexts(resolved, 10);
            if (labelTexts.Count == 0)
            {
                A11yLogger.Info("MenuProbe NGUI: selected object has no UILabel text children.");
                return;
            }

            for (int i = 0; i < labelTexts.Count; i++)
            {
                A11yLogger.Info($"MenuProbe NGUI UILabel[{i + 1}]: \"{labelTexts[i]}\"");
            }
        }

        private static List<string> CollectNguiLabelTexts(GameObject root, int limit)
        {
            List<string> results = new List<string>();
            if (root == null || limit <= 0)
            {
                return results;
            }

            Stack<Transform> stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0 && results.Count < limit)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                Component[] components = current.gameObject.GetComponents<Component>();
                if (components != null)
                {
                    for (int i = 0; i < components.Length && results.Count < limit; i++)
                    {
                        Component component = components[i];
                        if (component == null || !NguiReflection.IsLabel(component))
                        {
                            continue;
                        }

                        string text = NguiReflection.GetLabelText(component);
                        text = VisibilityUtil.NormalizeText(text);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        results.Add(TrimSnapshotText(text));
                    }
                }

                int childCount = current.childCount;
                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    stack.Push(current.GetChild(childIndex));
                }
            }

            return results;
        }

        private static void LogNguiLabelSubtreeDump(string label, GameObject root, int limit)
        {
            if (root == null)
            {
                return;
            }

            if (!NguiReflection.HasUILabel)
            {
                A11yLogger.Info($"MenuProbe NGUI {label} UILabel subtree dump: UILabel binding not available.");
                return;
            }

            List<NguiLabelRawEntry> entries = new List<NguiLabelRawEntry>();
            int totalLabels = 0;
            HashSet<int> seenInstanceIds = new HashSet<int>();
            Stack<Transform> stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null)
                {
                    continue;
                }

                Component[] components = current.gameObject.GetComponents<Component>();
                if (components != null)
                {
                    for (int i = 0; i < components.Length; i++)
                    {
                        Component component = components[i];
                        if (component == null || !NguiReflection.IsLabel(component))
                        {
                            continue;
                        }

                        int instanceId = component.GetInstanceID();
                        if (!seenInstanceIds.Add(instanceId))
                        {
                            continue;
                        }

                        totalLabels++;
                        if (entries.Count < limit)
                        {
                            entries.Add(BuildNguiLabelRawEntry(component, current));
                        }
                    }
                }

                int childCount = current.childCount;
                for (int childIndex = 0; childIndex < childCount; childIndex++)
                {
                    stack.Push(current.GetChild(childIndex));
                }
            }

            A11yLogger.Info($"MenuProbe NGUI {label} UILabel subtree dump: totalUILabel={totalLabels}, showing {entries.Count}");
            for (int i = 0; i < entries.Count; i++)
            {
                LogNguiLabelRawEntry($"MenuProbe NGUI {label} UILabel[{i + 1}]", entries[i]);
            }
        }

        private static void LogNguiUILabelRawDump()
        {
            if (!NguiReflection.HasUILabel)
            {
                A11yLogger.Info("MenuProbe NGUI RAW UILabel dump: UILabel binding not available.");
                return;
            }

            List<Transform> transforms = FindAllTransforms();
            if (transforms == null || transforms.Count == 0)
            {
                A11yLogger.Info("MenuProbe NGUI RAW UILabel dump: no transforms found.");
                return;
            }

            int totalLabels = 0;
            List<NguiLabelRawEntry> entries = new List<NguiLabelRawEntry>();
            HashSet<int> seenInstanceIds = new HashSet<int>();

            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform == null)
                {
                    continue;
                }

                Component[] components = transform.gameObject.GetComponents<Component>();
                if (components == null)
                {
                    continue;
                }

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null || !NguiReflection.IsLabel(component))
                    {
                        continue;
                    }

                    int instanceId = component.GetInstanceID();
                    if (!seenInstanceIds.Add(instanceId))
                    {
                        continue;
                    }

                    totalLabels++;
                    if (entries.Count < NguiLabelSnapshotLimit)
                    {
                        entries.Add(BuildNguiLabelRawEntry(component, transform));
                    }
                }
            }

            A11yLogger.Info($"MenuProbe NGUI RAW UILabel dump: totalUILabel={totalLabels}, showing {entries.Count}");
            for (int i = 0; i < entries.Count; i++)
            {
                LogNguiLabelRawEntry($"MenuProbe NGUI RAW UILabel[{i + 1}]", entries[i]);
            }
        }

        private static NguiLabelRawEntry BuildNguiLabelRawEntry(Component component, Transform transform)
        {
            string path = MenuProbe.BuildHierarchyPath(transform);
            bool activeInHierarchy = transform.gameObject.activeInHierarchy;
            string enabledState = "unknown";
            int rawLength = 0;
            int processedLength = 0;
            string sample = string.Empty;
            string localizeTerm = null;

            try
            {
                if (NguiReflection.TryGetUILabelTextDetails(component, out string rawText, out string processedText, out localizeTerm))
                {
                    rawLength = rawText?.Length ?? 0;
                    processedLength = processedText?.Length ?? 0;
                    string sampleSource = !string.IsNullOrWhiteSpace(processedText) ? processedText : rawText;
                    sample = string.IsNullOrWhiteSpace(sampleSource)
                        ? string.Empty
                        : TrimSnapshotText(VisibilityUtil.NormalizeText(sampleSource) ?? sampleSource);
                }

                bool? enabled = NguiReflection.GetUILabelEnabled(component);
                enabledState = enabled.HasValue ? (enabled.Value ? "true" : "false") : "unknown";
            }
            catch (Exception ex)
            {
                if (!nguiLabelEntryExceptionLogged)
                {
                    nguiLabelEntryExceptionLogged = true;
                    A11yLogger.Info($"MenuProbe NGUI UILabel entry failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return new NguiLabelRawEntry(path, activeInHierarchy, enabledState, rawLength, processedLength, sample, localizeTerm);
        }

        private static void LogNguiLabelRawEntry(string prefix, NguiLabelRawEntry entry)
        {
            string localizeInfo = string.IsNullOrWhiteSpace(entry.LocalizeTerm)
                ? string.Empty
                : $", localize=\"{TrimSnapshotText(entry.LocalizeTerm)}\"";
            A11yLogger.Info(
                $"{prefix}: path={entry.Path}, activeInHierarchy={entry.ActiveInHierarchy}, enabled={entry.EnabledState}, rawLength={entry.RawLength}, processedLength={entry.ProcessedLength}, sample=\"{entry.Sample}\"{localizeInfo}");
        }

        private static void LogNguiUILabelSnapshot()
        {
            List<Transform> transforms = FindAllTransforms();
            if (transforms == null || transforms.Count == 0)
            {
                A11yLogger.Info("MenuProbe NGUI UILabel snapshot: no transforms found.");
                return;
            }

            int totalLabels = 0;
            List<NguiLabelEntry> visibleLabels = new List<NguiLabelEntry>();

            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform == null)
                {
                    continue;
                }

                GameObject gameObject = transform.gameObject;
                if (gameObject == null)
                {
                    continue;
                }

                Component[] components = gameObject.GetComponents<Component>();
                if (components == null)
                {
                    continue;
                }

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null || !NguiReflection.IsLabel(component))
                    {
                        continue;
                    }

                    totalLabels++;
                    if (!gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    string text = NguiReflection.GetLabelText(component);
                    text = VisibilityUtil.NormalizeText(text);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    string path = MenuProbe.BuildHierarchyPath(transform);
                    visibleLabels.Add(new NguiLabelEntry(text, path));
                }
            }

            int limit = Mathf.Min(visibleLabels.Count, NguiLabelSnapshotLimit);
            A11yLogger.Info($"MenuProbe NGUI UILabel snapshot: totalUILabel={totalLabels}, visibleTextLabels={visibleLabels.Count}, showing {limit}");
            for (int i = 0; i < limit; i++)
            {
                NguiLabelEntry entry = visibleLabels[i];
                string text = TrimSnapshotText(entry.Text);
                A11yLogger.Info($"MenuProbe NGUI UILabel[{i + 1}]: text=\"{text}\", path={entry.Path}");
            }
        }

        private static void LogEventSystemDiagnostics()
        {
            Il2CppSystem.Type eventSystemType = Il2CppInterop.Runtime.Il2CppType.Of<EventSystem>();
            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(eventSystemType);
            EventSystem eventSystem = null;
            if (found != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] is EventSystem candidate)
                    {
                        eventSystem = candidate;
                        break;
                    }
                }
            }

            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            string selectedName = selected != null ? selected.name : "(null)";
            A11yLogger.Info($"MenuProbe diagnostics: EventSystem exists={eventSystem != null}, currentSelected={selectedName}");
        }

        private static List<Transform> FindAllTransforms()
        {
            Il2CppSystem.Type transformType = Il2CppInterop.Runtime.Il2CppType.Of<Transform>();
            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(transformType);
            if (found == null)
            {
                return new List<Transform>();
            }

            List<Transform> results = new List<Transform>(found.Length);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is Transform transform)
                {
                    results.Add(transform);
                }
            }

            return results;
        }

        private static void LogSceneDistribution(List<Transform> transforms)
        {
            Dictionary<string, int> sceneCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (transforms == null)
            {
                A11yLogger.Info("MenuProbe diagnostics: scene distribution unavailable (no transforms).");
                return;
            }

            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform == null || transform.gameObject == null)
                {
                    continue;
                }

                Scene scene = transform.gameObject.scene;
                string sceneName = scene.IsValid() ? scene.name : "(invalid)";
                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    sceneName = "(unnamed)";
                }

                if (sceneCounts.TryGetValue(sceneName, out int current))
                {
                    sceneCounts[sceneName] = current + 1;
                }
                else
                {
                    sceneCounts[sceneName] = 1;
                }
            }

            List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>(sceneCounts);
            entries.Sort((left, right) =>
            {
                int compare = right.Value.CompareTo(left.Value);
                if (compare != 0)
                {
                    return compare;
                }

                return string.Compare(left.Key, right.Key, StringComparison.Ordinal);
            });

            int limit = Mathf.Min(entries.Count, 10);
            A11yLogger.Info($"MenuProbe diagnostics: scene distribution entries={entries.Count}, showing top {limit}");
            for (int i = 0; i < limit; i++)
            {
                KeyValuePair<string, int> entry = entries[i];
                A11yLogger.Info($"MenuProbe diagnostics scene[{i + 1}]: {entry.Key}={entry.Value}");
            }
        }

        private static void LogComponentHistogramAndKeywords(List<Transform> transforms)
        {
            if (transforms == null)
            {
                A11yLogger.Info("MenuProbe diagnostics: component histogram unavailable (no transforms).");
                return;
            }

            Dictionary<string, int> histogram = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> keywordMatches = new HashSet<string>(StringComparer.Ordinal);
            string[] keywords =
            {
                "tmpro",
                "tmp",
                "textmesh",
                "gui",
                "ongui",
                "imgui",
                "label",
                "menu",
                "panel",
                "button",
                "select",
                "ngui",
                "widget",
                "sprite",
                "coherent",
                "web",
                "html",
                "browser",
                "localiz",
                "uix",
                "ui",
                "screen",
                "front"
            };

            int totalComponents = 0;
            int tmpComponentCount = 0;
            int textMeshComponentCount = 0;
            int nguiComponentCount = 0;
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform transform = transforms[i];
                if (transform == null)
                {
                    continue;
                }

                Component[] components = transform.gameObject.GetComponents<Component>();
                if (components == null)
                {
                    continue;
                }

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                    {
                        continue;
                    }

                    totalComponents++;
                    string typeName = GetIl2CppTypeName(component);
                    if (histogram.TryGetValue(typeName, out int current))
                    {
                        histogram[typeName] = current + 1;
                    }
                    else
                    {
                        histogram[typeName] = 1;
                    }

                    if (typeName.Contains("TMPro", StringComparison.Ordinal) || typeName.Contains("TMP_", StringComparison.Ordinal))
                    {
                        tmpComponentCount++;
                    }

                    if (typeName.Contains("TextMesh", StringComparison.Ordinal))
                    {
                        textMeshComponentCount++;
                    }

                    if (IsNguiComponentName(typeName))
                    {
                        nguiComponentCount++;
                    }

                    string lower = typeName.ToLowerInvariant();
                    for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
                    {
                        if (lower.Contains(keywords[keywordIndex]))
                        {
                            keywordMatches.Add(typeName);
                            break;
                        }
                    }
                }
            }

            List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>(histogram);
            entries.Sort((left, right) =>
            {
                int compare = right.Value.CompareTo(left.Value);
                if (compare != 0)
                {
                    return compare;
                }

                return string.Compare(left.Key, right.Key, StringComparison.Ordinal);
            });

            int limit = Mathf.Min(entries.Count, 40);
            A11yLogger.Info($"MenuProbe diagnostics: component histogram totalComponents={totalComponents}, uniqueTypes={histogram.Count}, showing top {limit}");
            for (int i = 0; i < limit; i++)
            {
                KeyValuePair<string, int> entry = entries[i];
                A11yLogger.Info($"MenuProbe diagnostics component[{i + 1}]: {entry.Key}={entry.Value}");
            }

            if (entries.Count > limit)
            {
                A11yLogger.Info("MenuProbe diagnostics: (other component types omitted)");
            }

            A11yLogger.Info($"MenuProbe diagnostics: explicit counters TMP/TMPro={tmpComponentCount}, TextMesh={textMeshComponentCount}, NGUI/UI={nguiComponentCount}");

            List<string> keywordList = new List<string>(keywordMatches);
            List<KeyValuePair<string, int>> keywordEntries = new List<KeyValuePair<string, int>>(keywordList.Count);
            for (int i = 0; i < keywordList.Count; i++)
            {
                string typeName = keywordList[i];
                if (histogram.TryGetValue(typeName, out int count))
                {
                    keywordEntries.Add(new KeyValuePair<string, int>(typeName, count));
                }
                else
                {
                    keywordEntries.Add(new KeyValuePair<string, int>(typeName, 0));
                }
            }

            keywordEntries.Sort((left, right) =>
            {
                int compare = right.Value.CompareTo(left.Value);
                if (compare != 0)
                {
                    return compare;
                }

                return string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase);
            });

            A11yLogger.Info($"MenuProbe diagnostics: keyword component types matches={keywordEntries.Count}");
            for (int i = 0; i < keywordEntries.Count; i++)
            {
                KeyValuePair<string, int> entry = keywordEntries[i];
                A11yLogger.Info($"MenuProbe diagnostics keyword[{i + 1}]: {entry.Key}={entry.Value}");
            }
        }

        private static string GetIl2CppTypeName(Component component)
        {
            if (component == null)
            {
                return "(null)";
            }

            Il2CppSystem.Type il2CppType = component.GetIl2CppType();
            if (il2CppType != null)
            {
                if (!string.IsNullOrWhiteSpace(il2CppType.FullName))
                {
                    return il2CppType.FullName;
                }

                if (!string.IsNullOrWhiteSpace(il2CppType.Name))
                {
                    return il2CppType.Name;
                }
            }

            return "(unknown)";
        }

        private static bool IsNguiComponentName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (typeName.Contains("NGUI", StringComparison.Ordinal))
            {
                return true;
            }

            int lastDot = typeName.LastIndexOf('.');
            string shortName = lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
            if (shortName == "UILabel" || shortName == "UIPanel" || shortName == "UIWidget" || shortName == "UIButton")
            {
                return true;
            }

            return false;
        }

        private static void LogCameraCensus()
        {
            Il2CppSystem.Type cameraType = Il2CppInterop.Runtime.Il2CppType.Of<Camera>();
            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(cameraType);
            if (found == null)
            {
                A11yLogger.Info("MenuProbe diagnostics: camera census unavailable (no cameras).");
                return;
            }

            int cameraCount = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is Camera)
                {
                    cameraCount++;
                }
            }

            A11yLogger.Info($"MenuProbe diagnostics: camera census total={cameraCount}");
            int index = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is Camera camera)
                {
                    index++;
                    GameObject cameraObject = camera.gameObject;
                    string name = cameraObject != null ? cameraObject.name : "(null)";
                    bool activeInHierarchy = cameraObject != null && cameraObject.activeInHierarchy;
                    A11yLogger.Info(
                        $"MenuProbe diagnostics camera[{index}]: name={name}, enabled={camera.enabled}, activeInHierarchy={activeInHierarchy}, depth={camera.depth:0.###}, cullingMask={camera.cullingMask}, clearFlags={camera.clearFlags}");
                }
            }
        }

        private void HandleHotkeys()
        {
            Settings activeSettings = settings ?? Settings.Instance;
            if (activeSettings == null)
            {
                if (!loggedSettingsUnavailable)
                {
                    loggedSettingsUnavailable = true;
                    A11yLogger.Warning("Hotkeys disabled because settings failed to initialize.");
                }

                return;
            }

            if (HotkeyUtil.IsPressed(activeSettings.SpeakFocusBinding))
            {
                SpeakFocusNow();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ReadScreenBinding))
            {
                screenReview.EnterOrRefresh();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ExitReviewBinding))
            {
                screenReview.Exit();
            }

            if (HotkeyUtil.IsPressed(activeSettings.NextItemBinding))
            {
                screenReview.Next();
            }

            if (HotkeyUtil.IsPressed(activeSettings.PreviousItemBinding))
            {
                screenReview.Previous();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ReadAllBinding))
            {
                screenReview.ReadAll();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ActivateBinding))
            {
                ActivateCurrent();
            }

            if (HotkeyUtil.IsPressed(activeSettings.RepeatLastBinding))
            {
                speechService?.RepeatLast();
            }

            if (HotkeyUtil.IsPressed(activeSettings.StopSpeakingBinding))
            {
                speechService?.Stop();
            }

            if (HotkeyUtil.IsPressed(activeSettings.ToggleFocusAutoSpeakBinding))
            {
                activeSettings.AutoSpeakFocusChanges = !activeSettings.AutoSpeakFocusChanges;
                activeSettings.Save();
                string state = activeSettings.AutoSpeakFocusChanges ? "on" : "off";
                speechService?.Speak($"Focus auto speak {state}", A11ySpeechPriority.Normal, "toggle_focus", false);
            }
        }

        private void SpeakFocusNow()
        {
            if (focusTracker == null)
            {
                return;
            }

            string narration = focusTracker.BuildFocusNarration(focusTracker.GetCurrentFocus());
            if (!string.IsNullOrWhiteSpace(narration))
            {
                speechService?.Speak(narration, A11ySpeechPriority.Normal, "focus_manual", false);
            }
        }

        private void ActivateCurrent()
        {
            GameObject target = screenReview.InReviewMode ? screenReview.GetCurrentItem()?.Target : focusTracker?.GetCurrentFocus();
            if (target == null)
            {
                speechService?.Speak("Nothing to activate", A11ySpeechPriority.Normal, "activate", false);
                return;
            }

            bool activated = ActivationUtil.Activate(target);
            if (activated)
            {
                string name = AccessibleNameResolver.Resolve(target)?.Name;
                string label = string.IsNullOrWhiteSpace(name) ? "item" : name;
                speechService?.Speak($"Activated {label}", A11ySpeechPriority.Normal, "activate", false);
            }
            else
            {
                speechService?.Speak("Cannot activate", A11ySpeechPriority.Normal, "activate", false);
            }
        }
    }
}
