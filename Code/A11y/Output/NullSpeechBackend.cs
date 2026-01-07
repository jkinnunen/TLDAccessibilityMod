namespace TLDAccessibility.A11y.Output
{
    internal sealed class NullSpeechBackend : IA11ySpeechBackend
    {
        public bool IsAvailable => false;

        public void Speak(string text)
        {
        }

        public void Stop()
        {
        }
    }
}
