// NOTE: Keep this file and type casing as NguiReflection; do not add NGUIReflection.cs or Windows checkouts will break.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
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
        private static bool uiLabelTextExceptionLogged;
        private static readonly HashSet<string> uiLabelTargetExceptionCallSites = new HashSet<string>();
        private static readonly HashSet<int> loggedLocalizeLabels = new HashSet<int>();
        private static bool nonUILabelLogged;
        private static readonly Dictionary<Type, UILabelMembers> uiLabelMembersByType = new Dictionary<Type, UILabelMembers>();

        private sealed class UILabelMembers
        {
            public PropertyInfo TextProperty { get; set; }
            public FieldInfo TextField { get; set; }
            public PropertyInfo ProcessedProperty { get; set; }
            public FieldInfo ProcessedField { get; set; }
            public PropertyInfo IsVisibleProperty { get; set; }
            public FieldInfo IsVisibleField { get; set; }
            public PropertyInfo EnabledProperty { get; set; }
            public FieldInfo EnabledField { get; set; }
            public PropertyInfo AlphaProperty { get; set; }
            public FieldInfo AlphaField { get; set; }
            public PropertyInfo WorldCornersProperty { get; set; }
            public FieldInfo WorldCornersField { get; set; }
        }

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
            return IsUILabelComponent(component);
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

            Type runtimeType = component.GetType();
            UILabelMembers members = GetUILabelMembers(runtimeType);
            return ReadBool(component, members.IsVisibleProperty, members.IsVisibleField);
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

            Type runtimeType = component.GetType();
            UILabelMembers members = GetUILabelMembers(runtimeType);
            return ReadBool(component, members.EnabledProperty, members.EnabledField);
        }

        public static float? GetUILabelAlpha(Component component)
        {
            if (component == null)
            {
                return null;
            }

            Type runtimeType = component.GetType();
            UILabelMembers members = GetUILabelMembers(runtimeType);
            return ReadFloat(component, members.AlphaProperty, members.AlphaField);
        }

        public static Vector3[] GetUILabelWorldCorners(Component component)
        {
            if (component == null)
            {
                return null;
            }

            Type runtimeType = component.GetType();
            UILabelMembers members = GetUILabelMembers(runtimeType);
            return ReadMemberValue(component, runtimeType, members.WorldCornersProperty, members.WorldCornersField) as Vector3[];
        }

        public static string GetLabelText(Component component)
        {
            if (component == null)
            {
                return null;
            }

            if (!TryGetUILabelTextDetails(component, out string rawText, out string processedText, out string localizeTerm))
            {
                return null;
            }

            string text = !string.IsNullOrWhiteSpace(rawText) ? rawText : processedText;
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(localizeTerm))
            {
                LogLocalizeLabel(component, localizeTerm);
            }

            return text;
        }

        public static bool TryGetUILabelTextDetails(
            Component component,
            out string rawText,
            out string processedText,
            out string localizeTerm,
            [CallerMemberName] string callSite = null)
        {
            rawText = null;
            processedText = null;
            localizeTerm = null;

            if (component == null)
            {
                return false;
            }

            string runtimeName = GetComponentTypeName(component);
            if (!string.Equals(runtimeName, UILabelTypeName, StringComparison.Ordinal))
            {
                LogNonUILabelOnce(component);
                return false;
            }

            try
            {
                var uiLabel = component.TryCast<global::Il2Cpp.UILabel>();
                if (uiLabel != null)
                {
                    rawText = ConvertToString(uiLabel.text);
                    processedText = ConvertToString(uiLabel.processedText);
                    localizeTerm = GetUILocalizeTerm(component.gameObject);
                    return true;
                }

                Type runtimeType = component.GetType();
                UILabelMembers members = GetUILabelMembers(runtimeType);
                rawText = ConvertToString(ReadMemberValue(component, runtimeType, members.TextProperty, members.TextField));
                processedText = ConvertToString(ReadMemberValue(component, runtimeType, members.ProcessedProperty, members.ProcessedField));

                localizeTerm = GetUILocalizeTerm(component.gameObject);
                return true;
            }
            catch (TargetException ex)
            {
                LogTargetExceptionOnce(callSite, ex);
                return false;
            }
            catch (ArgumentException ex)
            {
                LogOnce(ref uiLabelTextExceptionLogged, $"NGUI: UILabel text reflection failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            catch (NullReferenceException ex)
            {
                LogOnce(ref uiLabelTextExceptionLogged, $"NGUI: UILabel text reflection failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogOnce(ref uiLabelTextExceptionLogged, $"NGUI: UILabel text reflection failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static UILabelMembers GetUILabelMembers(Type runtimeType)
        {
            if (runtimeType == null)
            {
                return new UILabelMembers();
            }

            if (uiLabelMembersByType.TryGetValue(runtimeType, out UILabelMembers cached))
            {
                return cached;
            }

            Type labelType = GetUILabelType();
            UILabelMembers members = new UILabelMembers
            {
                TextProperty = ResolveUILabelProperty(runtimeType, labelType, "text"),
                TextField = ResolveUILabelField(runtimeType, labelType, "text"),
                ProcessedProperty = ResolveUILabelProperty(runtimeType, labelType, "processedText"),
                ProcessedField = ResolveUILabelField(runtimeType, labelType, "processedText"),
                IsVisibleProperty = ResolveUILabelProperty(runtimeType, labelType, "isVisible"),
                IsVisibleField = ResolveUILabelField(runtimeType, labelType, "isVisible"),
                EnabledProperty = ResolveUILabelProperty(runtimeType, labelType, "enabled"),
                EnabledField = ResolveUILabelField(runtimeType, labelType, "enabled"),
                AlphaProperty = ResolveUILabelProperty(runtimeType, labelType, "alpha"),
                AlphaField = ResolveUILabelField(runtimeType, labelType, "alpha"),
                WorldCornersProperty = ResolveUILabelProperty(runtimeType, labelType, "worldCorners"),
                WorldCornersField = ResolveUILabelField(runtimeType, labelType, "worldCorners")
            };

            uiLabelMembersByType[runtimeType] = members;
            return members;
        }

        private static object ReadMemberValue(Component component, Type componentType, PropertyInfo property, FieldInfo field)
        {
            if (component == null || componentType == null)
            {
                return null;
            }

            if (property != null && property.DeclaringType != null && property.DeclaringType.IsAssignableFrom(componentType))
            {
                return property.GetValue(component);
            }

            if (field != null && field.DeclaringType != null && field.DeclaringType.IsAssignableFrom(componentType))
            {
                return field.GetValue(component);
            }

            return null;
        }

        private static PropertyInfo ResolveUILabelProperty(Type runtimeType, Type labelType, string name)
        {
            if (runtimeType == null)
            {
                return null;
            }

            PropertyInfo property = runtimeType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property;
            }

            if (labelType != null && labelType != runtimeType)
            {
                return labelType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return null;
        }

        private static FieldInfo ResolveUILabelField(Type runtimeType, Type labelType, string name)
        {
            if (runtimeType == null)
            {
                return null;
            }

            FieldInfo field = runtimeType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field;
            }

            if (labelType != null && labelType != runtimeType)
            {
                return labelType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return null;
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

        internal static string GetUILocalizeTerm(GameObject target)
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

        private static bool IsUILabelComponent(Component component)
        {
            if (component == null)
            {
                return false;
            }

            string name = GetComponentTypeName(component);
            if (string.Equals(name, UILabelTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            Type labelType = GetUILabelType();
            Type runtimeType = component.GetType();
            return labelType != null && labelType.IsAssignableFrom(runtimeType);
        }

        private static void LogNonUILabelOnce(Component component)
        {
            if (nonUILabelLogged)
            {
                return;
            }

            nonUILabelLogged = true;
            string typeName = component != null ? component.GetType().Name : "(null)";
            A11yLogger.Info($"NGUI: UILabel text lookup skipped for non-UILabel component type={typeName}.");
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

            int instanceId = GetComponentStableId(component);
            if (!loggedLocalizeLabels.Add(instanceId))
            {
                return;
            }

            string objectName = component.gameObject != null ? component.gameObject.name : "(null)";
            A11yLogger.Info($"NGUI UILabel has UILocalize key/term=\"{localizeTerm}\" on {objectName}.");
        }

        private static int GetComponentStableId(Component component)
        {
            return component != null ? component.GetInstanceID() : 0;
        }

        private static void LogTargetExceptionOnce(string callSite, Exception ex)
        {
            string label = string.IsNullOrWhiteSpace(callSite) ? "(unknown)" : callSite;
            if (!uiLabelTargetExceptionCallSites.Add(label))
            {
                return;
            }

            A11yLogger.Info($"NGUI: UILabel text reflection failed: {ex.GetType().Name}: {ex.Message}");
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
