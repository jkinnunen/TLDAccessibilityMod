using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class TmpReflection
    {
        private const string TmpTextTypeName = "TMPro.TMP_Text";
        private const string TmpDropdownTypeName = "TMPro.TMP_Dropdown";
        private const string TmpInputFieldTypeName = "TMPro.TMP_InputField";

        private static Type tmpTextType;
        private static bool tmpTextChecked;
        private static bool tmpTextMembersCached;
        private static PropertyInfo tmpTextProperty;
        private static PropertyInfo tmpRectTransformProperty;
        private static MethodInfo tmpTextSetter;
        private static IReadOnlyList<MethodInfo> tmpSetTextMethods;
        private static Type tmpDropdownType;
        private static bool tmpDropdownChecked;
        private static PropertyInfo tmpDropdownValueProperty;
        private static PropertyInfo tmpDropdownOptionsProperty;
        private static Type tmpInputFieldType;
        private static bool tmpInputFieldChecked;
        private static PropertyInfo tmpInputTextProperty;

        public static Type TmpTextType => GetCachedType(ref tmpTextType, ref tmpTextChecked, TmpTextTypeName);

        public static bool HasTmpText => TmpTextType != null;

        public static bool IsTmpText(Component component)
        {
            Type type = TmpTextType;
            if (component == null)
            {
                return false;
            }

            if (type != null)
            {
                return type.IsInstanceOfType(component);
            }

            return string.Equals(component.GetType().FullName, TmpTextTypeName, StringComparison.Ordinal);
        }

        public static Component GetTmpTextComponent(GameObject target)
        {
            Type type = TmpTextType;
            if (type != null)
            {
                return GetComponentByType(target, type);
            }

            if (target == null)
            {
                return null;
            }

            foreach (Component component in target.GetComponents<Component>())
            {
                if (IsTmpText(component))
                {
                    return component;
                }
            }

            return null;
        }

        public static IEnumerable<Component> FindAllTmpTextComponents(bool includeInactive)
        {
            return UnityEngine.Object.FindObjectsOfType<Component>(includeInactive)
                .Where(component => component != null && IsTmpText(component));
        }

        public static IEnumerable<Component> GetTmpTextComponentsInChildren(GameObject target, bool includeInactive)
        {
            if (target == null)
            {
                return Enumerable.Empty<Component>();
            }

            return target.GetComponentsInChildren<Component>(includeInactive)
                .Where(component => component != null && IsTmpText(component));
        }

        public static string GetTmpTextValue(Component component)
        {
            if (component == null)
            {
                return null;
            }

            EnsureTmpTextMembers();
            PropertyInfo property = tmpTextProperty ?? component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(component) as string;
        }

        public static RectTransform GetTmpRectTransform(Component component)
        {
            if (component == null)
            {
                return null;
            }

            EnsureTmpTextMembers();
            PropertyInfo property = tmpRectTransformProperty ?? component.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(component) as RectTransform;
        }

        public static MethodInfo GetTmpTextSetter()
        {
            EnsureTmpTextMembers();
            return tmpTextSetter;
        }

        public static IReadOnlyList<MethodInfo> GetTmpSetTextMethods()
        {
            EnsureTmpTextMembers();
            return tmpSetTextMethods ?? Array.Empty<MethodInfo>();
        }

        public static bool TryGetTmpDropdownValue(GameObject target, out string value)
        {
            value = null;
            Type type = GetCachedType(ref tmpDropdownType, ref tmpDropdownChecked, TmpDropdownTypeName);
            if (type == null || target == null)
            {
                return false;
            }

            Component component = GetComponentByType(target, type);
            if (component == null)
            {
                return false;
            }

            tmpDropdownValueProperty ??= type.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            tmpDropdownOptionsProperty ??= type.GetProperty("options", BindingFlags.Instance | BindingFlags.Public);
            if (tmpDropdownValueProperty == null || tmpDropdownOptionsProperty == null)
            {
                return false;
            }

            object optionsValue = tmpDropdownOptionsProperty.GetValue(component);
            if (optionsValue is not IList optionsList)
            {
                return false;
            }

            object rawIndex = tmpDropdownValueProperty.GetValue(component);
            if (rawIndex is not int index || index < 0 || index >= optionsList.Count)
            {
                return false;
            }

            object option = optionsList[index];
            if (option == null)
            {
                return false;
            }

            PropertyInfo textProperty = option.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            value = textProperty?.GetValue(option) as string;
            return !string.IsNullOrWhiteSpace(value);
        }

        public static bool TryGetTmpInputValue(GameObject target, out string value)
        {
            value = null;
            Type type = GetCachedType(ref tmpInputFieldType, ref tmpInputFieldChecked, TmpInputFieldTypeName);
            if (type == null || target == null)
            {
                return false;
            }

            Component component = GetComponentByType(target, type);
            if (component == null)
            {
                return false;
            }

            tmpInputTextProperty ??= type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            value = tmpInputTextProperty?.GetValue(component) as string;
            return value != null;
        }

        private static Type GetCachedType(ref Type cachedType, ref bool checkedOnce, string typeName)
        {
            if (checkedOnce)
            {
                return cachedType;
            }

            cachedType = FindType(typeName);
            checkedOnce = true;
            return cachedType;
        }

        private static Type FindType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = FindTypeInAssembly(assembly, typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static Type FindTypeInAssembly(Assembly assembly, string typeName)
        {
            if (assembly == null)
            {
                return null;
            }

            Type type = assembly.GetType(typeName, false);
            if (type != null)
            {
                return type;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
            {
                return null;
            }

            foreach (Type candidate in types)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void EnsureTmpTextMembers()
        {
            if (tmpTextMembersCached)
            {
                return;
            }

            Type type = TmpTextType;
            if (type == null)
            {
                tmpTextMembersCached = true;
                return;
            }

            tmpTextProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            tmpRectTransformProperty = type.GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public);
            tmpTextSetter = tmpTextProperty?.GetSetMethod(false);
            tmpSetTextMethods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == "SetText")
                .Where(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
                })
                .ToList();
            tmpTextMembersCached = true;
        }

        internal static Component GetComponentByType(GameObject target, Type type)
        {
            if (type == null || target == null)
            {
                return null;
            }

            Component[] components = target.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component != null && type.IsInstanceOfType(component))
                {
                    return component;
                }
            }

            return null;
        }
    }
}
