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
        private static Type tmpDropdownType;
        private static bool tmpDropdownChecked;
        private static Type tmpInputFieldType;
        private static bool tmpInputFieldChecked;

        public static Type TmpTextType => GetCachedType(ref tmpTextType, ref tmpTextChecked, TmpTextTypeName);

        public static bool HasTmpText => TmpTextType != null;

        public static bool IsTmpText(Component component)
        {
            Type type = TmpTextType;
            return type != null && component != null && type.IsInstanceOfType(component);
        }

        public static Component GetTmpTextComponent(GameObject target)
        {
            Type type = TmpTextType;
            return type == null || target == null ? null : target.GetComponent(type);
        }

        public static IEnumerable<Component> FindAllTmpTextComponents(bool includeInactive)
        {
            Type type = TmpTextType;
            if (type == null)
            {
                return Enumerable.Empty<Component>();
            }

            UnityEngine.Object[] found = UnityEngine.Object.FindObjectsOfType(type, includeInactive);
            return found.OfType<Component>();
        }

        public static IEnumerable<Component> GetTmpTextComponentsInChildren(GameObject target, bool includeInactive)
        {
            Type type = TmpTextType;
            if (type == null || target == null)
            {
                return Enumerable.Empty<Component>();
            }

            Component[] found = target.GetComponentsInChildren(type, includeInactive);
            return found.OfType<Component>();
        }

        public static string GetTmpTextValue(Component component)
        {
            if (component == null)
            {
                return null;
            }

            PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(component) as string;
        }

        public static RectTransform GetTmpRectTransform(Component component)
        {
            if (component == null)
            {
                return null;
            }

            PropertyInfo property = component.GetType().GetProperty("rectTransform", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(component) as RectTransform;
        }

        public static bool TryGetTmpDropdownValue(GameObject target, out string value)
        {
            value = null;
            Type type = GetCachedType(ref tmpDropdownType, ref tmpDropdownChecked, TmpDropdownTypeName);
            if (type == null || target == null)
            {
                return false;
            }

            Component component = target.GetComponent(type);
            if (component == null)
            {
                return false;
            }

            PropertyInfo valueProperty = type.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo optionsProperty = type.GetProperty("options", BindingFlags.Instance | BindingFlags.Public);
            if (valueProperty == null || optionsProperty == null)
            {
                return false;
            }

            object optionsValue = optionsProperty.GetValue(component);
            if (optionsValue is not IList optionsList)
            {
                return false;
            }

            object rawIndex = valueProperty.GetValue(component);
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

            Component component = target.GetComponent(type);
            if (component == null)
            {
                return false;
            }

            PropertyInfo textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            value = textProperty?.GetValue(component) as string;
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
