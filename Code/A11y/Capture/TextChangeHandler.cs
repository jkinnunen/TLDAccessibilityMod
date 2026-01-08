using TLDAccessibility.A11y.Model;
using TLDAccessibility.A11y.Output;
using TLDAccessibility.A11y.UI;
using UnityEngine;

namespace TLDAccessibility.A11y.Capture
{
    internal static class TextChangeHandler
    {
        public static A11ySpeechService SpeechService { get; set; }

        public static void HandleTextChange(Component component, string text)
        {
            if (SpeechService == null || !Settings.Instance.AutoSpeakTextChanges)
            {
                return;
            }

            if (component == null)
            {
                return;
            }

            string normalized = VisibilityUtil.NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!VisibilityUtil.IsElementVisible(component, false))
            {
                return;
            }

            if (Settings.Instance.SuppressNumericAutoSpeech && IsMostlyNumeric(normalized))
            {
                return;
            }

            string sourceId = $"text_{component.GetInstanceID()}";
            SpeechService.Speak(normalized, A11ySpeechPriority.Normal, sourceId, true);
        }

        private static bool IsMostlyNumeric(string text)
        {
            int digits = 0;
            int letters = 0;
            foreach (char c in text)
            {
                if (char.IsDigit(c))
                {
                    digits++;
                }
                else if (char.IsLetter(c))
                {
                    letters++;
                }
            }

            return digits > 0 && letters == 0;
        }
    }
}
