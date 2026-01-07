using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TLDAccessibility.A11y.UI
{
    internal static class NGUIReflection
    {
        private static Type uiLabelType;
        private static PropertyInfo textProperty;
        private static PropertyInfo enabledProperty;
        private static PropertyInfo alphaProperty;
        private static PropertyInfo isVisibleProperty;
        private static PropertyInfo worldCornersProperty;

        public static bool HasUILabel => GetUILabelType() != null;

        public static Type GetUILabelType()
        {
            if (uiLabelType != null)
            {
                return uiLabelType;
            }

            uiLabelType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UILabel"))
                .FirstOrDefault(t => t != null);

            if (uiLabelType != null)
            {
                textProperty = uiLabelType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                enabledProperty = uiLabelType.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                alphaProperty = uiLabelType.GetProperty("alpha", BindingFlags.Instance | BindingFlags.Public);
                isVisibleProperty = uiLabelType.GetProperty("isVisible", BindingFlags.Instance | BindingFlags.Public);
                worldCornersProperty = uiLabelType.GetProperty("worldCorners", BindingFlags.Instance | BindingFlags.Public);
            }

            return uiLabelType;
        }

        public static string GetUILabelText(Component component)
        {
            if (component == null || textProperty == null)
            {
                return null;
            }

            return textProperty.GetValue(component, null) as string;
        }

        public static bool? GetUILabelEnabled(Component component)
        {
            if (component == null || enabledProperty == null)
            {
                return null;
            }

            object value = enabledProperty.GetValue(component, null);
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return null;
        }

        public static float? GetUILabelAlpha(Component component)
        {
            if (component == null || alphaProperty == null)
            {
                return null;
            }

            object value = alphaProperty.GetValue(component, null);
            if (value is float floatValue)
            {
                return floatValue;
            }

            return null;
        }

        public static bool? GetUILabelIsVisible(Component component)
        {
            if (component == null || isVisibleProperty == null)
            {
                return null;
            }

            object value = isVisibleProperty.GetValue(component, null);
            if (value is bool boolValue)
            {
                return boolValue;
            }

            return null;
        }

        public static Vector3[] GetUILabelWorldCorners(Component component)
        {
            if (component == null || worldCornersProperty == null)
            {
                return null;
            }

            return worldCornersProperty.GetValue(component, null) as Vector3[];
        }
    }
}
