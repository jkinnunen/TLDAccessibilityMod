using System;
using System.Speech.Synthesis;
using System.Runtime.Versioning;
using TLDAccessibility.A11y.Logging;

namespace TLDAccessibility.A11y.Output
{
    [SupportedOSPlatform("windows")]
    internal sealed class SystemSpeechBackend : IA11ySpeechBackend
    {
        private readonly SpeechSynthesizer synthesizer;
        private readonly bool isAvailable;

        public SystemSpeechBackend()
        {
            if (!OperatingSystem.IsWindows())
            {
                isAvailable = false;
                A11yLogger.Warning("System speech backend unavailable: non-Windows platform.");
                return;
            }

            try
            {
                synthesizer = new SpeechSynthesizer();
                synthesizer.SetOutputToDefaultAudioDevice();
                isAvailable = true;
                A11yLogger.Info("System speech backend initialized.");
            }
            catch (Exception ex)
            {
                isAvailable = false;
                A11yLogger.Warning($"System speech backend unavailable: {ex.Message}");
            }
        }

        public bool IsAvailable => isAvailable;

        public void Speak(string text)
        {
            if (!isAvailable || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            synthesizer.SpeakAsyncCancelAll();
            synthesizer.SpeakAsync(text);
        }

        public void Stop()
        {
            if (!isAvailable)
            {
                return;
            }

            synthesizer.SpeakAsyncCancelAll();
        }
    }
}
