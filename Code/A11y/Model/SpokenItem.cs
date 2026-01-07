namespace TLDAccessibility.A11y.Model
{
    internal sealed class SpokenItem
    {
        public string Text { get; set; }
        public A11ySpeechPriority Priority { get; set; }
        public string SourceId { get; set; }
        public bool IsAuto { get; set; }
        public float EnqueueTime { get; set; }
    }
}
