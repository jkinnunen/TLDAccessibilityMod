using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal sealed class MenuSelectionTracker
    {
        private static readonly string[] CandidateMethodNames =
        {
            "SetSelected",
            "SetHighlighted",
            "OnSelect",
            "OnHover",
            "SetFocus",
            "SetActive"
        };

        private static readonly string[] CandidateMethodFragments =
        {
            "select",
            "highlight",
            "hover",
            "focus",
            "active"
        };

        private static bool basicMenuItemTypeChecked;
        private static Type basicMenuItemType;
        private static bool basicMenuTypeChecked;
        private static Type basicMenuType;
        private static bool localizationTypeChecked;
        private static Type localizationType;
        private static bool localizationMethodChecked;
        private static MethodInfo localizationGetMethod;
        private static bool localizationExceptionLogged;
        private static bool patchExceptionLogged;
        private const float PollIntervalSeconds = 0.2f;

        private readonly A11ySpeechService speechService;
        private string lastSpokenLabel;
        private int lastSpokenInstanceId = -1;
        private GameObject lastSelectedItem;
        private string lastSelectedLabel;
        private string lastSelectionSource;
        private float nextPollTime;
        private int lastLoggedMenuCandidateId = -1;

        public MenuSelectionTracker(A11ySpeechService speechService)
        {
            this.speechService = speechService;
        }

        public static void ApplyHarmonyPatches(HarmonyLib.Harmony harmony, MenuSelectionTracker tracker)
        {
            if (harmony == null || tracker == null)
            {
                return;
            }

            MenuSelectionPatches.Tracker = tracker;
            harmony.PatchAll(typeof(MenuSelectionPatches));
        }

        public void Update()
        {
            if (Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            if (TryResolveSelection(out SelectionSnapshot snapshot))
            {
                HandleMenuItemSelected(snapshot.SelectedComponent, snapshot.Source);
            }
        }

        public void LogDiagnostics()
        {
            Type menuItemType = GetBasicMenuItemType();
            if (menuItemType == null)
            {
                A11yLogger.Info("Menu selection diagnostics: BasicMenuItem type not found.");
                return;
            }

            MenuCandidate candidate = FindActiveMenuCandidate(true);
            if (candidate == null || candidate.MenuComponent == null)
            {
                A11yLogger.Info("Menu selection diagnostics: no active menu root available.");
                return;
            }

            GameObject root = candidate.MenuComponent.gameObject;
            string rootPath = candidate.Path;
            A11yLogger.Info($"Menu selection diagnostics: root={root.name}, path={rootPath}, activeItems={candidate.ActiveItemCount}, totalItems={candidate.TotalItemCount}");
            if (lastSelectedItem != null)
            {
                string lastPath = MenuProbe.BuildHierarchyPath(lastSelectedItem.transform);
                string label = string.IsNullOrWhiteSpace(lastSelectedLabel) ? "(none)" : lastSelectedLabel;
                string source = string.IsNullOrWhiteSpace(lastSelectionSource) ? "(unknown)" : lastSelectionSource;
                A11yLogger.Info($"Menu selection diagnostics: lastSelected={lastSelectedItem.name}, path={lastPath}, label=\"{label}\", source={source}");
            }

            Component basicMenuComponent = candidate.MenuComponent;
            if (basicMenuComponent != null && TryGetSelectionIndex(basicMenuComponent, out int selectedIndex))
            {
                A11yLogger.Info($"Menu selection diagnostics: selectedIndex={selectedIndex}");
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            HashSet<int> seenInstanceIds = new HashSet<int>();
            int totalItems = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (!IsBasicMenuItem(component))
                {
                    continue;
                }

                int instanceId = component.GetInstanceID();
                if (!seenInstanceIds.Add(instanceId))
                {
                    continue;
                }

                totalItems++;
                GameObject itemObject = component.gameObject;
                string label = ResolveMenuItemLabel(itemObject) ?? itemObject.name;
                label = VisibilityUtil.NormalizeText(label);
                string path = MenuProbe.BuildHierarchyPath(itemObject.transform);
                bool activeInHierarchy = itemObject.activeInHierarchy;
                A11yLogger.Info(
                    $"Menu selection item[{totalItems}]: path={path}, activeInHierarchy={activeInHierarchy}, label=\"{label}\"");
            }

            if (totalItems == 0)
            {
                A11yLogger.Info("Menu selection diagnostics: no BasicMenuItem entries found under root.");
            }
        }

        internal MenuSelectionProbeSnapshot CaptureProbeSnapshot()
        {
            SelectionSnapshot snapshot = TryResolveSelection(out SelectionSnapshot selection)
                ? selection
                : selection;

            return new MenuSelectionProbeSnapshot(
                snapshot.MenuPath,
                snapshot.SelectedItemPath,
                snapshot.LabelText,
                snapshot.FailureReason);
        }

        internal void HandleMenuItemSelected(Component itemComponent, string source)
        {
            if (itemComponent == null)
            {
                return;
            }

            GameObject itemObject = itemComponent.gameObject;
            if (itemObject == null)
            {
                return;
            }

            string label = ResolveMenuItemLabel(itemObject, false);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = itemObject.name;
            }

            label = VisibilityUtil.NormalizeText(label);
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            int instanceId = itemObject.GetInstanceID();
            lastSelectedItem = itemObject;
            lastSelectedLabel = label;
            lastSelectionSource = source;

            if (instanceId == lastSpokenInstanceId && string.Equals(label, lastSpokenLabel, StringComparison.Ordinal))
            {
                return;
            }

            speechService?.Speak(label, A11ySpeechPriority.Normal, "menu_selection", true);
            lastSpokenInstanceId = instanceId;
            lastSpokenLabel = label;
        }

        private static string ResolveMenuItemLabel(GameObject itemObject)
        {
            return ResolveMenuItemLabel(itemObject, true);
        }

        private static string ResolveMenuItemLabel(GameObject itemObject, bool includeInactive)
        {
            if (itemObject == null)
            {
                return null;
            }

            string localizeTerm = null;
            string bestProcessed = null;
            string bestRaw = null;
            Component[] components = itemObject.GetComponentsInChildren<Component>(includeInactive);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !NguiReflection.IsLabel(component))
                {
                    continue;
                }

                if (NguiReflection.TryGetUILabelTextDetails(component, out string rawText, out string processedText, out string labelLocalizeTerm))
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

            if (string.IsNullOrWhiteSpace(localizeTerm))
            {
                localizeTerm = FindLocalizeTerm(itemObject);
            }

            if (string.IsNullOrWhiteSpace(localizeTerm))
            {
                return null;
            }

            string resolved = ResolveLocalizationTerm(localizeTerm);
            return string.IsNullOrWhiteSpace(resolved) ? localizeTerm : resolved;
        }

        private static string FindLocalizeTerm(GameObject itemObject)
        {
            if (itemObject == null)
            {
                return null;
            }

            Component[] components = itemObject.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                string term = NguiReflection.GetUILocalizeTerm(component.gameObject);
                if (!string.IsNullOrWhiteSpace(term))
                {
                    return term;
                }
            }

            return null;
        }

        private static string ResolveLocalizationTerm(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return null;
            }

            MethodInfo method = GetLocalizationGetMethod();
            if (method == null)
            {
                return term;
            }

            try
            {
                object value = method.Invoke(null, new object[] { term });
                if (value is string resolved && !string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }
            catch (Exception ex)
            {
                if (!localizationExceptionLogged)
                {
                    localizationExceptionLogged = true;
                    A11yLogger.Warning($"Menu selection localization failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return term;
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

        private static MethodInfo GetLocalizationGetMethod()
        {
            if (localizationMethodChecked)
            {
                return localizationGetMethod;
            }

            localizationMethodChecked = true;
            Type type = GetLocalizationType();
            if (type == null)
            {
                return null;
            }

            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null)
                {
                    continue;
                }

                if (method.ReturnType != typeof(string))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                if (method.Name == "Get" || method.Name == "GetString")
                {
                    localizationGetMethod = method;
                    break;
                }
            }

            return localizationGetMethod;
        }

        private static Type GetLocalizationType()
        {
            if (localizationTypeChecked)
            {
                return localizationType;
            }

            localizationTypeChecked = true;
            localizationType = FindTypeByName("Il2Cpp.Localization") ?? FindTypeByName("Localization");
            return localizationType;
        }

        private bool TryResolveSelection(out SelectionSnapshot snapshot)
        {
            snapshot = new SelectionSnapshot("(none)", "(null)", "(none)", "polling", "No active menu candidates.");

            MenuCandidate candidate = FindActiveMenuCandidate(false);
            if (candidate == null || candidate.MenuComponent == null)
            {
                snapshot = new SelectionSnapshot("(none)", "(null)", "(none)", "polling", "No BasicMenu candidates found.");
                return false;
            }

            string menuPath = candidate.Path;
            Component selectedItem = null;
            string failureReason;
            if (!TryResolveSelectedItem(candidate, out selectedItem, out failureReason))
            {
                snapshot = new SelectionSnapshot(menuPath, "(null)", "(none)", "polling", failureReason);
                return false;
            }

            string selectedPath = selectedItem != null && selectedItem.transform != null
                ? MenuProbe.BuildHierarchyPath(selectedItem.transform)
                : "(null)";
            string labelText = selectedItem != null ? ResolveMenuItemLabel(selectedItem.gameObject, false) : null;
            labelText = string.IsNullOrWhiteSpace(labelText) ? "(none)" : labelText;
            snapshot = new SelectionSnapshot(menuPath, selectedPath, labelText, "polling", string.Empty, selectedItem);
            return selectedItem != null;
        }

        private bool TryResolveSelectedItem(MenuCandidate candidate, out Component selectedItem, out string failureReason)
        {
            selectedItem = null;
            failureReason = string.Empty;

            Component menuComponent = candidate.MenuComponent;
            if (menuComponent == null)
            {
                failureReason = "Active menu component missing.";
                return false;
            }

            if (TryGetSelectedItemFromMenuComponent(menuComponent, out selectedItem, out failureReason))
            {
                return selectedItem != null;
            }

            if (TryGetSelectionIndex(menuComponent, out int selectedIndex))
            {
                List<Component> activeItems = CollectMenuItems(candidate.MenuComponent.gameObject, false);
                if (selectedIndex >= 0 && selectedIndex < activeItems.Count)
                {
                    selectedItem = activeItems[selectedIndex];
                    return selectedItem != null;
                }

                failureReason = $"Selected index {selectedIndex} did not map to an active BasicMenuItem (count={activeItems.Count}).";
                return false;
            }

            if (TrySelectBestVisibleItem(candidate.MenuComponent.gameObject, out selectedItem))
            {
                return true;
            }

            failureReason = string.IsNullOrWhiteSpace(failureReason)
                ? "Unable to resolve selection from BasicMenu fields or visible BasicMenuItem entries."
                : failureReason;
            return false;
        }

        private static bool TryGetSelectedItemFromMenuComponent(
            Component menuComponent,
            out Component selectedItem,
            out string failureReason)
        {
            selectedItem = null;
            failureReason = string.Empty;
            if (menuComponent == null)
            {
                failureReason = "Menu component missing.";
                return false;
            }

            Type runtimeType = menuComponent.GetType();
            Type menuItemType = GetBasicMenuItemType();
            if (menuItemType == null)
            {
                failureReason = "BasicMenuItem type not found.";
                return false;
            }

            FieldInfo[] fields = runtimeType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                string name = field.Name.ToLowerInvariant();
                if (!name.Contains("selected") && !name.Contains("current") && !name.Contains("highlight"))
                {
                    continue;
                }

                object value = field.GetValue(menuComponent);
                if (TryExtractMenuItemComponent(value, menuItemType, out selectedItem))
                {
                    return selectedItem != null;
                }
            }

            PropertyInfo[] properties = runtimeType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property == null || !property.CanRead || string.IsNullOrWhiteSpace(property.Name))
                {
                    continue;
                }

                string name = property.Name.ToLowerInvariant();
                if (!name.Contains("selected") && !name.Contains("current") && !name.Contains("highlight"))
                {
                    continue;
                }

                object value = property.GetValue(menuComponent, null);
                if (TryExtractMenuItemComponent(value, menuItemType, out selectedItem))
                {
                    return selectedItem != null;
                }
            }

            failureReason = "No selected item reference found on BasicMenu.";
            return false;
        }

        private static bool TryExtractMenuItemComponent(object value, Type menuItemType, out Component selectedItem)
        {
            selectedItem = null;
            if (value == null)
            {
                return false;
            }

            if (value is Component component)
            {
                if (IsBasicMenuItem(component))
                {
                    selectedItem = component;
                    return true;
                }

                Component child = component.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(menuItemType));
                if (child != null)
                {
                    selectedItem = child;
                    return true;
                }
            }

            if (value is GameObject gameObject)
            {
                Component menuItemComponent = gameObject.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(menuItemType));
                if (menuItemComponent != null)
                {
                    selectedItem = menuItemComponent;
                    return true;
                }
            }

            return false;
        }

        private static bool TrySelectBestVisibleItem(GameObject root, out Component selectedItem)
        {
            selectedItem = null;
            if (root == null)
            {
                return false;
            }

            List<Component> items = CollectMenuItems(root, false);
            if (items.Count == 0)
            {
                return false;
            }

            int bestScore = int.MinValue;
            Component bestItem = null;
            for (int i = 0; i < items.Count; i++)
            {
                Component item = items[i];
                int score = ScoreMenuItemVisibility(item);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestItem = item;
                }
            }

            if (bestItem == null || bestScore <= 0)
            {
                return false;
            }

            selectedItem = bestItem;
            return true;
        }

        private static int ScoreMenuItemVisibility(Component item)
        {
            if (item == null || item.gameObject == null || !item.gameObject.activeInHierarchy)
            {
                return 0;
            }

            int score = 1;
            string label = ResolveMenuItemLabel(item.gameObject, false);
            if (!string.IsNullOrWhiteSpace(label) && !IsPlaceholderText(label))
            {
                score += 3;
            }

            Component[] components = item.gameObject.GetComponentsInChildren<Component>(false);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !NguiReflection.IsLabel(component))
                {
                    continue;
                }

                bool? visible = NguiReflection.GetUILabelIsVisible(component);
                if (visible.HasValue && visible.Value)
                {
                    score += 5;
                }

                bool? enabled = NguiReflection.GetUILabelEnabled(component);
                if (enabled.HasValue && enabled.Value)
                {
                    score += 2;
                }

                float? alpha = NguiReflection.GetUILabelAlpha(component);
                if (alpha.HasValue && alpha.Value > 0.2f)
                {
                    score += 2;
                }
            }

            return score;
        }

        private static List<Component> CollectMenuItems(GameObject root, bool includeInactive)
        {
            List<Component> results = new List<Component>();
            if (root == null)
            {
                return results;
            }

            Component[] components = root.GetComponentsInChildren<Component>(includeInactive);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (IsBasicMenuItem(component))
                {
                    results.Add(component);
                }
            }

            return results;
        }

        private static bool TryGetSelectionIndex(Component basicMenuComponent, out int selectedIndex)
        {
            selectedIndex = -1;
            if (basicMenuComponent == null)
            {
                return false;
            }

            Type type = basicMenuComponent.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.FieldType != typeof(int))
                {
                    continue;
                }

                string name = field.Name?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(name) || !name.Contains("selected") || !name.Contains("index"))
                {
                    continue;
                }

                selectedIndex = (int)field.GetValue(basicMenuComponent);
                return true;
            }

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property == null || property.PropertyType != typeof(int) || !property.CanRead)
                {
                    continue;
                }

                string name = property.Name?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(name) || !name.Contains("selected") || !name.Contains("index"))
                {
                    continue;
                }

                selectedIndex = (int)property.GetValue(basicMenuComponent, null);
                return true;
            }

            return false;
        }

        private static bool IsBasicMenuItem(Component component)
        {
            if (component == null)
            {
                return false;
            }

            Type runtimeType = component.GetType();
            if (basicMenuItemType != null && basicMenuItemType.IsAssignableFrom(runtimeType))
            {
                return true;
            }

            string name = runtimeType.Name;
            return string.Equals(name, "BasicMenuItem", StringComparison.Ordinal);
        }

        private MenuCandidate FindActiveMenuCandidate(bool logCandidates)
        {
            List<MenuCandidate> candidates = FindMenuCandidates(out int menuItemCount);
            if (candidates.Count == 0)
            {
                return null;
            }

            MenuCandidate chosen = ChooseActiveMenuCandidate(candidates);

            if (logCandidates || (chosen != null && chosen.InstanceId != lastLoggedMenuCandidateId))
            {
                lastLoggedMenuCandidateId = chosen?.InstanceId ?? -1;
                LogMenuCandidates(candidates, chosen, menuItemCount);
            }

            return chosen;
        }

        private static List<MenuCandidate> FindMenuCandidates(out int menuItemCount)
        {
            List<MenuCandidate> results = new List<MenuCandidate>();
            menuItemCount = 0;
            Type menuType = GetBasicMenuType();
            Type menuItemType = GetBasicMenuItemType();
            if (menuType == null || menuItemType == null)
            {
                return results;
            }

            UnityEngine.Object[] menus = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(menuType));
            if (menus == null)
            {
                return results;
            }

            UnityEngine.Object[] menuItems = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(menuItemType));
            menuItemCount = menuItems?.Length ?? 0;

            for (int i = 0; i < menus.Length; i++)
            {
                if (menus[i] is Component component && component.gameObject != null)
                {
                    results.Add(BuildMenuCandidate(component));
                }
            }

            return results;
        }

        private static MenuCandidate BuildMenuCandidate(Component menuComponent)
        {
            GameObject root = menuComponent.gameObject;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            int total = 0;
            int active = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !IsBasicMenuItem(component))
                {
                    continue;
                }

                total++;
                if (component.gameObject.activeInHierarchy)
                {
                    active++;
                }
            }

            string path = MenuProbe.BuildHierarchyPath(root.transform);
            return new MenuCandidate(menuComponent, path, root.activeInHierarchy, active, total);
        }

        private static MenuCandidate ChooseActiveMenuCandidate(List<MenuCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            MenuCandidate best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                MenuCandidate candidate = candidates[i];
                int score = ScoreCandidate(candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static void LogMenuCandidates(List<MenuCandidate> candidates, MenuCandidate chosen, int menuItemCount)
        {
            if (candidates == null || candidates.Count == 0)
            {
                A11yLogger.Info("Menu selection discovery: no BasicMenu candidates found.");
                return;
            }

            A11yLogger.Info($"Menu selection discovery: BasicMenuItem total={menuItemCount}");

            List<MenuCandidate> ordered = new List<MenuCandidate>(candidates);
            ordered.Sort((left, right) =>
            {
                int scoreLeft = ScoreCandidate(left);
                int scoreRight = ScoreCandidate(right);
                int compare = scoreRight.CompareTo(scoreLeft);
                if (compare != 0)
                {
                    return compare;
                }

                return string.Compare(left.Path, right.Path, StringComparison.Ordinal);
            });

            int limit = Math.Min(5, ordered.Count);
            for (int i = 0; i < limit; i++)
            {
                MenuCandidate candidate = ordered[i];
                A11yLogger.Info(
                    $"Menu selection candidate[{i + 1}]: path={candidate.Path}, activeInHierarchy={candidate.ActiveInHierarchy}, activeItems={candidate.ActiveItemCount}, totalItems={candidate.TotalItemCount}");
            }

            if (chosen == null)
            {
                A11yLogger.Info("Menu selection discovery: no active menu chosen.");
                return;
            }

            A11yLogger.Info(
                $"Menu selection discovery: chosen path={chosen.Path}, activeInHierarchy={chosen.ActiveInHierarchy}, activeItems={chosen.ActiveItemCount}, totalItems={chosen.TotalItemCount}");
        }

        private static int ScoreCandidate(MenuCandidate candidate)
        {
            int score = 0;
            if (candidate.ActiveInHierarchy)
            {
                score += 1000;
            }

            if (candidate.ActiveItemCount > 0)
            {
                score += 100;
            }

            score += candidate.ActiveItemCount * 10;
            score += candidate.TotalItemCount;
            return score;
        }

        private static Type GetBasicMenuItemType()
        {
            if (basicMenuItemTypeChecked)
            {
                return basicMenuItemType;
            }

            basicMenuItemTypeChecked = true;
            basicMenuItemType = FindTypeByName("Il2Cpp.BasicMenuItem") ?? FindTypeByName("BasicMenuItem");
            return basicMenuItemType;
        }

        private static Type GetBasicMenuType()
        {
            if (basicMenuTypeChecked)
            {
                return basicMenuType;
            }

            basicMenuTypeChecked = true;
            basicMenuType = FindTypeByName("Il2Cpp.BasicMenu") ?? FindTypeByName("BasicMenu");
            return basicMenuType;
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

        internal readonly struct MenuSelectionProbeSnapshot
        {
            public MenuSelectionProbeSnapshot(string menuPath, string selectedPath, string labelText, string failureReason)
            {
                MenuPath = menuPath;
                SelectedPath = selectedPath;
                LabelText = labelText;
                FailureReason = failureReason;
            }

            public string MenuPath { get; }
            public string SelectedPath { get; }
            public string LabelText { get; }
            public string FailureReason { get; }
        }

        private sealed class MenuCandidate
        {
            public MenuCandidate(Component menuComponent, string path, bool activeInHierarchy, int activeItemCount, int totalItemCount)
            {
                MenuComponent = menuComponent;
                Path = path;
                ActiveInHierarchy = activeInHierarchy;
                ActiveItemCount = activeItemCount;
                TotalItemCount = totalItemCount;
                InstanceId = menuComponent != null ? menuComponent.GetInstanceID() : 0;
            }

            public Component MenuComponent { get; }
            public string Path { get; }
            public bool ActiveInHierarchy { get; }
            public int ActiveItemCount { get; }
            public int TotalItemCount { get; }
            public int InstanceId { get; }
        }

        private readonly struct SelectionSnapshot
        {
            public SelectionSnapshot(
                string menuPath,
                string selectedItemPath,
                string labelText,
                string source,
                string failureReason,
                Component selectedComponent = null)
            {
                MenuPath = menuPath;
                SelectedItemPath = selectedItemPath;
                LabelText = labelText;
                Source = source;
                FailureReason = failureReason;
                SelectedComponent = selectedComponent;
            }

            public string MenuPath { get; }
            public string SelectedItemPath { get; }
            public string LabelText { get; }
            public string Source { get; }
            public string FailureReason { get; }
            public Component SelectedComponent { get; }
        }

        [HarmonyPatch]
        private static class MenuSelectionPatches
        {
            public static MenuSelectionTracker Tracker { get; set; }

            [HarmonyPatch]
            private static class BasicMenuItemSelectionPatch
            {
                private static IEnumerable<MethodBase> TargetMethods()
                {
                    Type type = GetBasicMenuItemType();
                    if (type == null)
                    {
                        return Array.Empty<MethodBase>();
                    }

                    List<MethodBase> results = new List<MethodBase>();
                    MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo method = methods[i];
                        if (method == null)
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
                        {
                            continue;
                        }

                        if (IsMenuSelectionMethod(method.Name))
                        {
                            results.Add(method);
                        }
                    }

                    return results;
                }

                [HarmonyPostfix]
                private static void Postfix(object __instance, bool __0, MethodBase __originalMethod)
                {
                    if (!__0 || Tracker == null)
                    {
                        return;
                    }

                    if (!(__instance is Component component))
                    {
                        return;
                    }

                    try
                    {
                        Tracker.HandleMenuItemSelected(component, __originalMethod?.Name);
                    }
                    catch (Exception ex)
                    {
                        if (!patchExceptionLogged)
                        {
                            patchExceptionLogged = true;
                            string methodName = __originalMethod?.Name ?? "(unknown)";
                            A11yLogger.Warning($"Menu selection patch failed ({methodName}): {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            private static bool IsMenuSelectionMethod(string methodName)
            {
                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return false;
                }

                for (int i = 0; i < CandidateMethodNames.Length; i++)
                {
                    if (string.Equals(methodName, CandidateMethodNames[i], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                string lowered = methodName.ToLowerInvariant();
                for (int i = 0; i < CandidateMethodFragments.Length; i++)
                {
                    if (lowered.Contains(CandidateMethodFragments[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
