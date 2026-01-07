using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using TLDAccessibility.A11y.UI;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.Capture
{
    internal static class TextChangePatches
    {
        public static void Apply(Harmony harmony)
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
                foreach (MethodInfo method in typeof(TMP_Text).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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

            private static void Postfix(TMP_Text __instance, string text)
            {
                TextChangeHandler.HandleTextChange(__instance, text);
            }
        }

        [HarmonyPatch(typeof(TMP_Text), "set_text")]
        private static class TmpTextSetterPatch
        {
            private static void Postfix(TMP_Text __instance, string value)
            {
                TextChangeHandler.HandleTextChange(__instance, value);
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

        private static void PatchNgui(Harmony harmony)
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
