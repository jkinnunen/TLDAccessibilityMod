using System;
using System.IO;
using System.Reflection;
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
                string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(modDir))
                {
                    string updatedPath = $"{modDir};{currentPath}";
                    Environment.SetEnvironmentVariable("PATH", updatedPath);
                    A11yLogger.Info($"Tolk PATH prepended with mod directory: {modDir}");
                }

                A11yLogger.Info($"Tolk current directory: {Environment.CurrentDirectory}");

                if (Tolk_Load())
                {
                    isAvailable = Tolk_IsLoaded();
                    if (isAvailable)
                    {
                        bool sapiEnabled = Tolk_TrySAPI(true);
                        A11yLogger.Info($"Tolk SAPI try enabled: {sapiEnabled}");

                        string screenReader = DetectScreenReader();
                        A11yLogger.Info($"Tolk detected screen reader: {screenReader}");
                    }
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

        public bool Speak(string text)
        {
            if (!isAvailable || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                bool success = Tolk_Output(text, true);
                if (!success)
                {
                    isAvailable = false;
                    A11yLogger.Warning("Tolk output returned false.");
                }

                return success;
            }
            catch (Exception ex)
            {
                isAvailable = false;
                A11yLogger.Warning($"Tolk speak failed: {ex.Message}");
                return false;
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
        private static extern bool Tolk_Output(string text, bool interrupt);

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Tolk_DetectScreenReader();

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Tolk_TrySAPI(bool trySapi);

        [DllImport("Tolk", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Stop();

        private static string DetectScreenReader()
        {
            IntPtr result = Tolk_DetectScreenReader();
            string name = result == IntPtr.Zero ? null : Marshal.PtrToStringUni(result);
            return string.IsNullOrWhiteSpace(name) ? "(none)" : name;
        }
    }
}
