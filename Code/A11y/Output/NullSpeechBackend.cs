namespace TLDAccessibility.A11y.Output
{
    internal sealed class NullSpeechBackend : IA11ySpeechBackend
    {
        public bool IsAvailable => false;

        public bool Speak(string text)
        {
            return false;
        }

        public void Stop()
        {
        }
    }
}
