using UnityEngine;

namespace TLDAccessibility.A11y.Model
{
    internal sealed class SnapshotItem
    {
        public GameObject Target { get; set; }
        public string SpokenText { get; set; }
        public Rect ScreenRect { get; set; }
        public bool HasRect { get; set; }
    }
}
