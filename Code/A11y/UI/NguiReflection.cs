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
        private const int DefaultChildDepth = 3;
        private const int DefaultParentDepth = 5;

        private static bool uiLabelTypeChecked;
        private static Type uiLabelType;
        private static bool uiLabelMembersCached;
        private static PropertyInfo uiLabelIsVisibleProperty;
        private static FieldInfo uiLabelIsVisibleField;
        private static PropertyInfo uiLabelEnabledProperty;
        private static FieldInfo uiLabelEnabledField;
        private static PropertyInfo uiLabelAlphaProperty;
        private static FieldInfo uiLabelAlphaField;
        private static PropertyInfo uiLabelWorldCornersProperty;
        private static FieldInfo uiLabelWorldCornersField;
        private static bool uiCameraInitialized;
        private static Type uiCameraType;
        private static MethodInfo selectedGetter;
        private static MethodInfo hoveredGetter;
        private static bool uiCameraTypeMissingLogged;
        private static bool selectedGetterMissingLogged;
        private static bool hoveredGetterMissingLogged;
        private static bool selectedGetterExceptionLogged;
        private static bool hoveredGetterExceptionLogged;

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

            Type type = component.GetType();
            PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return ConvertToString(property.GetValue(component));
            }

            FieldInfo field = type.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return ConvertToString(field.GetValue(component));
            }

            return null;
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
