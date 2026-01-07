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

            bool canPatchTmp = TmpReflection.GetTmpTextSetter() != null || TmpReflection.GetTmpSetTextMethods().Count > 0;
            // If TMP_Text lacks patchable members, fall back to periodic polling.
            TmpTextPolling.Enabled = TmpReflection.HasTmpText && !canPatchTmp;
        }

        [HarmonyPatch]
        private static class TmpTextSetTextPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                // Patch TMP_Text.SetText(string, ...) overloads to detect runtime text changes.
                return TmpReflection.GetTmpSetTextMethods();
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
                // Patch TMP_Text.text setter for direct property assignments.
                return TmpReflection.GetTmpTextSetter();
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
