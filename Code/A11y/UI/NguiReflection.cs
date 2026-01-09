// NOTE: Keep this file and type casing as NguiReflection; do not add NGUIReflection.cs or Windows checkouts will break.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TLDAccessibility.A11y.Logging;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class NguiReflection
    {
        private const string UILabelTypeName = "UILabel";
        private const string UIButtonTypeName = "UIButton";
        private const string UILocalizeTypeName = "UILocalize";
        private const int DefaultChildDepth = 3;
        private const int DefaultParentDepth = 5;

        private static bool uiLabelTypeChecked;
        private static Type uiLabelType;
        private static bool uiButtonTypeChecked;
        private static Type uiButtonType;
        private static bool uiLocalizeTypeChecked;
        private static Type uiLocalizeType;
        private static bool uiLabelMembersCached;
        private static PropertyInfo uiLabelTextProperty;
        private static FieldInfo uiLabelTextField;
        private static PropertyInfo uiLabelProcessedTextProperty;
        private static FieldInfo uiLabelProcessedTextField;
        private static PropertyInfo uiLabelIsVisibleProperty;
        private static FieldInfo uiLabelIsVisibleField;
        private static PropertyInfo uiLabelEnabledProperty;
        private static FieldInfo uiLabelEnabledField;
        private static PropertyInfo uiLabelAlphaProperty;
        private static FieldInfo uiLabelAlphaField;
        private static PropertyInfo uiLabelWorldCornersProperty;
        private static FieldInfo uiLabelWorldCornersField;
        private static bool uiLocalizeMembersCached;
        private static PropertyInfo uiLocalizeKeyProperty;
        private static FieldInfo uiLocalizeKeyField;
        private static PropertyInfo uiLocalizeTermProperty;
        private static FieldInfo uiLocalizeTermField;
        private static bool uiCameraInitialized;
        private static Type uiCameraType;
        private static MethodInfo selectedGetter;
        private static MethodInfo hoveredGetter;
        private static bool uiCameraTypeMissingLogged;
        private static bool selectedGetterMissingLogged;
        private static bool hoveredGetterMissingLogged;
        private static bool selectedGetterExceptionLogged;
        private static bool hoveredGetterExceptionLogged;
        private static readonly HashSet<int> loggedLocalizeLabels = new HashSet<int>();

        internal readonly struct UiCameraStatus
        {
            public UiCameraStatus(bool typeExists, bool selectedReadable, bool hoveredReadable)
            {
                TypeExists = typeExists;
                SelectedReadable = selectedReadable;
                HoveredReadable = hoveredReadable;
            }

            public bool TypeExists { get; }
            public bool SelectedReadable { get; }
            public bool HoveredReadable { get; }
        }

        public static GameObject GetSelectedOrHoveredObject()
        {
            EnsureUiCameraReflection();
            GameObject selected = ReadGameObject(selectedGetter, "selectedObject", ref selectedGetterMissingLogged, ref selectedGetterExceptionLogged);
            if (selected != null)
            {
                return selected;
            }

            return ReadGameObject(hoveredGetter, "hoveredObject", ref hoveredGetterMissingLogged, ref hoveredGetterExceptionLogged);
        }

        public static GameObject GetSelectedObject()
        {
            EnsureUiCameraReflection();
            return ReadGameObject(selectedGetter, "selectedObject", ref selectedGetterMissingLogged, ref selectedGetterExceptionLogged);
        }

        public static GameObject GetHoveredObject()
        {
            EnsureUiCameraReflection();
            return ReadGameObject(hoveredGetter, "hoveredObject", ref hoveredGetterMissingLogged, ref hoveredGetterExceptionLogged);
        }

        public static UiCameraStatus GetUiCameraStatus()
        {
            EnsureUiCameraReflection();
            return new UiCameraStatus(uiCameraType != null, selectedGetter != null, hoveredGetter != null);
        }

        public static string ResolveLabelText(GameObject target)
        {
            return ResolveLabelText(target, DefaultChildDepth, DefaultParentDepth);
        }

        public static string ResolveLabelText(GameObject target, int childDepth, int parentDepth)
        {
            if (target == null)
            {
                return null;
            }

            Component label = FindLabelOnObjectOrChildren(target, childDepth);
            if (label == null)
            {
                label = FindLabelOnParents(target, parentDepth);
            }

            if (label == null)
            {
                label = FindLabelOnButtonTweenTarget(target, childDepth);
            }

            if (label == null)
            {
                return null;
            }

            string text = GetLabelText(label);
            text = VisibilityUtil.NormalizeText(text);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public static bool IsLabel(Component component)
        {
            return GetComponentTypeName(component) == UILabelTypeName;
        }

        public static Type GetUILabelType()
        {
            if (uiLabelTypeChecked)
            {
                return uiLabelType;
            }

            uiLabelType = FindTypeByName($"Il2Cpp.{UILabelTypeName}") ?? FindTypeByName(UILabelTypeName);
            uiLabelTypeChecked = true;
            return uiLabelType;
        }

        public static bool HasUILabel => GetUILabelType() != null;

        public static Type GetUIButtonType()
        {
            if (uiButtonTypeChecked)
            {
                return uiButtonType;
            }

            uiButtonType = FindTypeByName($"Il2Cpp.{UIButtonTypeName}") ?? FindTypeByName(UIButtonTypeName);
            uiButtonTypeChecked = true;
            return uiButtonType;
        }

        public static bool HasUIButton => GetUIButtonType() != null;

        public static string GetUILabelText(Component component)
        {
            return GetLabelText(component);
        }

        public static bool? GetUILabelIsVisible(Component component)
        {
            if (component == null)
            {
                return null;
            }

            EnsureUILabelMembers();
            return ReadBool(component, uiLabelIsVisibleProperty, uiLabelIsVisibleField);
        }

        public static bool? GetUILabelEnabled(Component component)
        {
            if (component == null)
            {
                return null;
            }

            if (component is Behaviour behaviour)
            {
                return behaviour.enabled;
            }

            EnsureUILabelMembers();
            return ReadBool(component, uiLabelEnabledProperty, uiLabelEnabledField);
        }

        public static float? GetUILabelAlpha(Component component)
        {
            if (component == null)
            {
                return null;
            }

            EnsureUILabelMembers();
            return ReadFloat(component, uiLabelAlphaProperty, uiLabelAlphaField);
        }

        public static Vector3[] GetUILabelWorldCorners(Component component)
        {
            if (component == null)
            {
                return null;
            }

            EnsureUILabelMembers();
            object value = uiLabelWorldCornersProperty?.GetValue(component) ?? uiLabelWorldCornersField?.GetValue(component);
            return value as Vector3[];
        }

        public static string GetLabelText(Component component)
        {
            if (component == null)
            {
                return null;
            }

            TryGetUILabelTextDetails(component, out string rawText, out string processedText, out string localizeTerm);
            string text = !string.IsNullOrWhiteSpace(rawText) ? rawText : processedText;
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(localizeTerm))
            {
                LogLocalizeLabel(component, localizeTerm);
            }

            return text;
        }

        public static void TryGetUILabelTextDetails(Component component, out string rawText, out string processedText, out string localizeTerm)
        {
            rawText = null;
            processedText = null;
            localizeTerm = null;

            if (component == null)
            {
                return;
            }

            EnsureUILabelMembers();
            rawText = ConvertToString(uiLabelTextProperty?.GetValue(component) ?? uiLabelTextField?.GetValue(component));
            processedText = ConvertToString(uiLabelProcessedTextProperty?.GetValue(component) ?? uiLabelProcessedTextField?.GetValue(component));

            if (rawText == null || processedText == null)
            {
                Type type = component.GetType();
                if (rawText == null)
                {
                    PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rawText = ConvertToString(property?.GetValue(component));
                    if (rawText == null)
                    {
                        FieldInfo field = type.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        rawText = ConvertToString(field?.GetValue(component));
                    }
                }

                if (processedText == null)
                {
                    PropertyInfo property = type.GetProperty("processedText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    processedText = ConvertToString(property?.GetValue(component));
                    if (processedText == null)
                    {
                        FieldInfo field = type.GetField("processedText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        processedText = ConvertToString(field?.GetValue(component));
                    }
                }
            }

            localizeTerm = GetUILocalizeTerm(component.gameObject);
        }

        private static void EnsureUILabelMembers()
        {
            if (uiLabelMembersCached)
            {
                return;
            }

            Type type = GetUILabelType();
            if (type == null)
            {
                uiLabelMembersCached = true;
                return;
            }

            uiLabelTextProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelTextField = type.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelProcessedTextProperty = type.GetProperty("processedText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelProcessedTextField = type.GetField("processedText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelIsVisibleProperty = type.GetProperty("isVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelIsVisibleField = type.GetField("isVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelEnabledProperty = type.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelEnabledField = type.GetField("enabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelAlphaProperty = type.GetProperty("alpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelAlphaField = type.GetField("alpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelWorldCornersProperty = type.GetProperty("worldCorners", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelWorldCornersField = type.GetField("worldCorners", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLabelMembersCached = true;
        }

        private static void EnsureUILocalizeMembers()
        {
            if (uiLocalizeMembersCached)
            {
                return;
            }

            Type type = GetUILocalizeType();
            if (type == null)
            {
                uiLocalizeMembersCached = true;
                return;
            }

            uiLocalizeKeyProperty = type.GetProperty("key", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLocalizeKeyField = type.GetField("key", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLocalizeTermProperty = type.GetProperty("term", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLocalizeTermField = type.GetField("term", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            uiLocalizeMembersCached = true;
        }

        private static Type GetUILocalizeType()
        {
            if (uiLocalizeTypeChecked)
            {
                return uiLocalizeType;
            }

            uiLocalizeType = FindTypeByName($"Il2Cpp.{UILocalizeTypeName}") ?? FindTypeByName(UILocalizeTypeName);
            uiLocalizeTypeChecked = true;
            return uiLocalizeType;
        }

        private static string GetUILocalizeTerm(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            System.Type type = GetUILocalizeType();
            if (type == null)
            {
                return null;
            }

            Component component = target.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(type));
            if (component == null)
            {
                return null;
            }

            EnsureUILocalizeMembers();
            object value = uiLocalizeKeyProperty?.GetValue(component) ?? uiLocalizeKeyField?.GetValue(component);
            if (value == null)
            {
                value = uiLocalizeTermProperty?.GetValue(component) ?? uiLocalizeTermField?.GetValue(component);
            }

            return ConvertToString(value);
        }

        private static bool? ReadBool(Component component, PropertyInfo property, FieldInfo field)
        {
            object value = property?.GetValue(component) ?? field?.GetValue(component);
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return null;
        }

        private static float? ReadFloat(Component component, PropertyInfo property, FieldInfo field)
        {
            object value = property?.GetValue(component) ?? field?.GetValue(component);
            if (value is float floatValue)
            {
                return floatValue;
            }

            return null;
        }

        private static void EnsureUiCameraReflection()
        {
            if (uiCameraInitialized)
            {
                return;
            }

            uiCameraType = FindTypeByName("Il2Cpp.UICamera") ?? FindTypeByName("UICamera");
            if (uiCameraType != null)
            {
                selectedGetter = AccessTools.PropertyGetter(uiCameraType, "selectedObject")
                    ?? AccessTools.Method(uiCameraType, "get_selectedObject");
                hoveredGetter = AccessTools.PropertyGetter(uiCameraType, "hoveredObject")
                    ?? AccessTools.Method(uiCameraType, "get_hoveredObject");
            }
            else
            {
                LogOnce(ref uiCameraTypeMissingLogged, "NGUI: UICamera type not found; selected/hovered lookup disabled.");
            }

            uiCameraInitialized = true;
        }

        private static GameObject ReadGameObject(
            MethodInfo getter,
            string label,
            ref bool missingLogged,
            ref bool exceptionLogged)
        {
            if (getter == null)
            {
                LogOnce(ref missingLogged, $"NGUI: UICamera {label} getter not available.");
                return null;
            }

            try
            {
                object value = getter.Invoke(null, null);
                return CoerceGameObject(value);
            }
            catch (Exception ex)
            {
                LogOnce(ref exceptionLogged, $"NGUI: UICamera {label} getter failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static GameObject CoerceGameObject(object value)
        {
            if (value is GameObject gameObject)
            {
                return gameObject;
            }

            if (value is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private static Component FindLabelOnObjectOrChildren(GameObject target, int maxDepth)
        {
            if (target == null || target.transform == null)
            {
                return null;
            }

            Stack<TransformNode> stack = new Stack<TransformNode>();
            stack.Push(new TransformNode(target.transform, 0));

            while (stack.Count > 0)
            {
                TransformNode node = stack.Pop();
                Transform current = node.Transform;
                if (current == null)
                {
                    continue;
                }

                Component label = FindLabelOnGameObject(current.gameObject);
                if (label != null)
                {
                    return label;
                }

                if (node.Depth >= maxDepth)
                {
                    continue;
                }

                int childCount = current.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    stack.Push(new TransformNode(current.GetChild(i), node.Depth + 1));
                }
            }

            return null;
        }

        private static Component FindLabelOnParents(GameObject target, int maxDepth)
        {
            if (target == null || target.transform == null)
            {
                return null;
            }

            Transform current = target.transform.parent;
            for (int depth = 0; depth < maxDepth && current != null; depth++)
            {
                Component label = FindLabelOnGameObject(current.gameObject);
                if (label != null)
                {
                    return label;
                }

                current = current.parent;
            }

            return null;
        }

        private static Component FindLabelOnButtonTweenTarget(GameObject target, int childDepth)
        {
            if (target == null)
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            if (components == null)
            {
                return null;
            }

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !IsButton(component))
                {
                    continue;
                }

                GameObject tweenTarget = GetTweenTarget(component);
                if (tweenTarget == null)
                {
                    continue;
                }

                Component label = FindLabelOnObjectOrChildren(tweenTarget, childDepth);
                if (label != null)
                {
                    return label;
                }
            }

            return null;
        }

        private static GameObject GetTweenTarget(Component button)
        {
            if (button == null)
            {
                return null;
            }

            Type type = button.GetType();
            PropertyInfo property = type.GetProperty("tweenTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return CoerceGameObject(property.GetValue(button));
            }

            FieldInfo field = type.GetField("tweenTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return CoerceGameObject(field.GetValue(button));
            }

            return null;
        }

        private static bool IsButton(Component component)
        {
            return GetComponentTypeName(component) == UIButtonTypeName;
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

            Type managedType = component.GetType();
            return managedType?.Name ?? string.Empty;
        }

        private static Component FindLabelOnGameObject(GameObject target)
        {
            if (target == null || !target.activeInHierarchy)
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            if (components == null)
            {
                return null;
            }

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (IsLabel(component))
                {
                    string text = GetLabelText(component);
                    if (!string.IsNullOrWhiteSpace(VisibilityUtil.NormalizeText(text)))
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private static string ConvertToString(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            return value.ToString();
        }

        private readonly struct TransformNode
        {
            public TransformNode(Transform transform, int depth)
            {
                Transform = transform;
                Depth = depth;
            }

            public Transform Transform { get; }
            public int Depth { get; }
        }

        private static void LogOnce(ref bool guard, string message)
        {
            if (guard)
            {
                return;
            }

            guard = true;
            A11yLogger.Info(message);
        }

        private static void LogLocalizeLabel(Component component, string localizeTerm)
        {
            if (component == null || string.IsNullOrWhiteSpace(localizeTerm))
            {
                return;
            }

            int instanceId = component.GetInstanceID();
            if (!loggedLocalizeLabels.Add(instanceId))
            {
                return;
            }

            string objectName = component.gameObject != null ? component.gameObject.name : "(null)";
            A11yLogger.Info($"NGUI UILabel has UILocalize key/term=\"{localizeTerm}\" on {objectName}.");
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
    }
}
