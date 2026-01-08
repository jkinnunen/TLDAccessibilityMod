namespace TLDAccessibility.A11y.Output
{
    internal interface IA11ySpeechBackend
    {
        bool IsAvailable { get; }
        bool Speak(string text);
        void Stop();
    }
}
