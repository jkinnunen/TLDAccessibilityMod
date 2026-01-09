using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class NGUIReflection
    {
        private static readonly string[] UiLabelTypeNames =
        {
            "UILabel",
            "NGUI.UILabel"
        };

        private static bool uiLabelInitialized;
        private static Type uiLabelType;
        private static PropertyInfo textProperty;
        private static FieldInfo textField;
        private static PropertyInfo enabledProperty;
        private static FieldInfo enabledField;
        private static PropertyInfo alphaProperty;
        private static FieldInfo alphaField;
        private static PropertyInfo isVisibleProperty;
        private static FieldInfo isVisibleField;
        private static PropertyInfo worldCornersProperty;
        private static FieldInfo worldCornersField;

        public static bool HasUILabel => GetUILabelType() != null;

        public static Type GetUILabelType()
        {
            EnsureUILabelReflection();
            return uiLabelType;
        }

        public static string GetUILabelText(Component component)
        {
            EnsureUILabelReflection();
            if (component == null)
            {
                return null;
            }

            object value = ReadValue(component, textProperty, textField);
            return ConvertToString(value);
        }

        public static bool? GetUILabelEnabled(Component component)
        {
            EnsureUILabelReflection();
            if (component == null)
            {
                return null;
            }

            object value = ReadValue(component, enabledProperty, enabledField);
            return ConvertToBool(value);
        }

        public static float? GetUILabelAlpha(Component component)
        {
            EnsureUILabelReflection();
            if (component == null)
            {
                return null;
            }

            object value = ReadValue(component, alphaProperty, alphaField);
            return ConvertToFloat(value);
        }

        public static bool? GetUILabelIsVisible(Component component)
        {
            EnsureUILabelReflection();
            if (component == null)
            {
                return null;
            }

            object value = ReadValue(component, isVisibleProperty, isVisibleField);
            return ConvertToBool(value);
        }

        public static Vector3[] GetUILabelWorldCorners(Component component)
        {
            EnsureUILabelReflection();
            if (component == null)
            {
                return null;
            }

            object value = ReadValue(component, worldCornersProperty, worldCornersField);
            return ConvertToVector3Array(value);
        }

        private static void EnsureUILabelReflection()
        {
            if (uiLabelInitialized)
            {
                return;
            }

            uiLabelType = ResolveType(UiLabelTypeNames);
            if (uiLabelType != null)
            {
                textProperty = AccessTools.Property(uiLabelType, "text");
                textField = AccessTools.Field(uiLabelType, "text");
                enabledProperty = AccessTools.Property(uiLabelType, "enabled");
                enabledField = AccessTools.Field(uiLabelType, "enabled");
                alphaProperty = AccessTools.Property(uiLabelType, "alpha");
                alphaField = AccessTools.Field(uiLabelType, "alpha");
                isVisibleProperty = AccessTools.Property(uiLabelType, "isVisible");
                isVisibleField = AccessTools.Field(uiLabelType, "isVisible");
                worldCornersProperty = AccessTools.Property(uiLabelType, "worldCorners");
                worldCornersField = AccessTools.Field(uiLabelType, "worldCorners");
            }

            uiLabelInitialized = true;
        }

        private static Type ResolveType(IEnumerable<string> typeNames)
        {
            foreach (string typeName in typeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                Type type = AccessTools.TypeByName(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static object ReadValue(Component component, PropertyInfo property, FieldInfo field)
        {
            if (component == null)
            {
                return null;
            }

            if (property != null)
            {
                return property.GetValue(component);
            }

            if (field != null)
            {
                return field.GetValue(component);
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

        private static bool? ConvertToBool(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    return Convert.ToBoolean(convertible);
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (InvalidCastException)
                {
                    return null;
                }
            }

            return null;
        }

        private static float? ConvertToFloat(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    return Convert.ToSingle(convertible);
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (InvalidCastException)
                {
                    return null;
                }
            }

            return null;
        }

        private static Vector3[] ConvertToVector3Array(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is Vector3[] vectors)
            {
                return vectors;
            }

            if (value is IEnumerable enumerable)
            {
                List<Vector3> results = new List<Vector3>();
                foreach (object item in enumerable)
                {
                    if (item is Vector3 vector)
                    {
                        results.Add(vector);
                    }
                    else
                    {
                        return null;
                    }
                }

                return results.Count > 0 ? results.ToArray() : Array.Empty<Vector3>();
            }

            return null;
        }
    }
}
