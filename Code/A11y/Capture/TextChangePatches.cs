using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TLDAccessibility.A11y.UI;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.Capture
{
    internal static class TextChangePatches
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            harmony.PatchAll(typeof(TextChangePatches));
            PatchNgui(harmony);
        }

        [HarmonyPatch]
        private static class TmpTextSetTextPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                Type tmpTextType = TmpReflection.TmpTextType;
                if (tmpTextType == null)
                {
                    yield break;
                }

                foreach (MethodInfo method in tmpTextType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "SetText")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length > 0 && parameters[0].ParameterType == typeof(string))
                    {
                        yield return method;
                    }
                }
            }

            private static void Postfix(object __instance, string text)
            {
                if (__instance is UnityEngine.Component component)
                {
                    TextChangeHandler.HandleTextChange(component, text);
                }
            }
        }

        [HarmonyPatch]
        private static class TmpTextSetterPatch
        {
            private static MethodBase TargetMethod()
            {
                Type tmpTextType = TmpReflection.TmpTextType;
                return tmpTextType != null ? AccessTools.PropertySetter(tmpTextType, "text") : null;
            }

            private static void Postfix(object __instance, string value)
            {
                if (__instance is UnityEngine.Component component)
                {
                    TextChangeHandler.HandleTextChange(component, value);
                }
            }
        }

        [HarmonyPatch(typeof(Text), "set_text")]
        private static class UiTextSetterPatch
        {
            private static void Postfix(Text __instance, string value)
            {
                TextChangeHandler.HandleTextChange(__instance, value);
            }
        }

        private static void PatchNgui(HarmonyLib.Harmony harmony)
        {
            Type uiLabelType = NGUIReflection.GetUILabelType();
            if (uiLabelType == null)
            {
                return;
            }

            MethodInfo setter = AccessTools.PropertySetter(uiLabelType, "text");
            if (setter == null)
            {
                return;
            }

            HarmonyMethod postfix = new HarmonyMethod(typeof(TextChangePatches), nameof(NguiTextPostfix));
            harmony.Patch(setter, postfix: postfix);
        }

        public static void NguiTextPostfix(object __instance, string value)
        {
            if (__instance is UnityEngine.Component component)
            {
                TextChangeHandler.HandleTextChange(component, value);
            }
        }
    }
}
