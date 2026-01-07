using MelonLoader;

namespace TLDAccessibility.A11y.Logging
{
    internal static class A11yLogger
    {
        private static MelonLogger.Instance logger;

        public static void Initialize(MelonLogger.Instance instance)
        {
            logger = instance;
        }

        public static void Info(string message)
        {
            logger?.Msg(message);
        }

        public static void Warning(string message)
        {
            logger?.Warning(message);
        }

        public static void Error(string message)
        {
            logger?.Error(message);
        }
    }
}
