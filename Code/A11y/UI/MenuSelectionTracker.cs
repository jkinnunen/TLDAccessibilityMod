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

        private readonly A11ySpeechService speechService;
        private string lastSpokenLabel;
        private int lastSpokenInstanceId = -1;
        private GameObject lastSelectedItem;
        private string lastSelectedLabel;
        private string lastSelectionSource;

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

        public void LogDiagnostics()
        {
            Type menuItemType = GetBasicMenuItemType();
            if (menuItemType == null)
            {
                A11yLogger.Info("Menu selection diagnostics: BasicMenuItem type not found.");
                return;
            }

            GameObject root = ResolveDiagnosticsRoot();
            if (root == null)
            {
                A11yLogger.Info("Menu selection diagnostics: no active menu root available.");
                return;
            }

            string rootPath = MenuProbe.BuildHierarchyPath(root.transform);
            A11yLogger.Info($"Menu selection diagnostics: root={root.name}, path={rootPath}");
            if (lastSelectedItem != null)
            {
                string lastPath = MenuProbe.BuildHierarchyPath(lastSelectedItem.transform);
                string label = string.IsNullOrWhiteSpace(lastSelectedLabel) ? "(none)" : lastSelectedLabel;
                string source = string.IsNullOrWhiteSpace(lastSelectionSource) ? "(unknown)" : lastSelectionSource;
                A11yLogger.Info($"Menu selection diagnostics: lastSelected={lastSelectedItem.name}, path={lastPath}, label=\"{label}\", source={source}");
            }

            Component basicMenuComponent = FindBasicMenuComponent(root);
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

            string label = ResolveMenuItemLabel(itemObject);
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
            if (itemObject == null)
            {
                return null;
            }

            string localizeTerm = null;
            Component[] components = itemObject.GetComponentsInChildren<Component>(true);
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

                    string candidateText = !string.IsNullOrWhiteSpace(processedText) ? processedText : rawText;
                    candidateText = VisibilityUtil.NormalizeText(candidateText);
                    if (!string.IsNullOrWhiteSpace(candidateText))
                    {
                        return candidateText;
                    }
                }
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

        private GameObject ResolveDiagnosticsRoot()
        {
            if (lastSelectedItem != null)
            {
                Component basicMenuComponent = FindBasicMenuComponent(lastSelectedItem);
                if (basicMenuComponent != null)
                {
                    return basicMenuComponent.gameObject;
                }

                Transform rootTransform = lastSelectedItem.transform.root;
                return rootTransform != null ? rootTransform.gameObject : lastSelectedItem;
            }

            Type type = GetBasicMenuType();
            if (type == null)
            {
                return null;
            }

            UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(type));
            if (found == null)
            {
                return null;
            }

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] is Component component && component.gameObject != null)
                {
                    return component.gameObject;
                }
            }

            return null;
        }

        private static Component FindBasicMenuComponent(GameObject itemObject)
        {
            Type type = GetBasicMenuType();
            if (itemObject == null || type == null)
            {
                return null;
            }

            Transform current = itemObject.transform;
            while (current != null)
            {
                Component component = current.gameObject.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(type));
                if (component != null)
                {
                    return component;
                }

                current = current.parent;
            }

            return null;
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
