using System;
using System.Runtime.InteropServices;
using TLDAccessibility.A11y.Logging;

namespace TLDAccessibility.A11y.Output
{
    internal sealed class TolkBackend : IA11ySpeechBackend
    {
        private bool isAvailable;

        public TolkBackend()
        {
            try
            {
                if (Tolk_Load())
                {
                    isAvailable = Tolk_IsLoaded();
                }
                else
                {
                    isAvailable = false;
                }
            }
            catch (DllNotFoundException)
            {
                isAvailable = false;
            }
            catch (Exception ex)
            {
                isAvailable = false;
                A11yLogger.Warning($"Tolk initialization failed: {ex.Message}");
            }

            if (!isAvailable)
            {
                A11yLogger.Warning("Tolk backend unavailable.");
            }
            else
            {
                A11yLogger.Info("Tolk backend initialized.");
            }
        }

        public bool IsAvailable => isAvailable;

        public void Speak(string text)
        {
            if (!isAvailable || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                Tolk_Speak(text, true);
            }
            catch (Exception ex)
            {
                isAvailable = false;
                A11yLogger.Warning($"Tolk speak failed: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!isAvailable)
            {
                return;
            }

            Tolk_Stop();
        }

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Load();

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Tolk_IsLoaded();

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern bool Tolk_Speak(string text, bool interrupt);

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Stop();
    }
}
