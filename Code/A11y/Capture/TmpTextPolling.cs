using System;
using System.Collections.Generic;
using TLDAccessibility.A11y.UI;
using UnityEngine;

namespace TLDAccessibility.A11y.Capture
{
    internal static class TmpTextPolling
    {
        private const float PollIntervalSeconds = 0.5f;
        private static readonly Dictionary<int, string> LastTextByInstance = new Dictionary<int, string>();
        private static float nextPollTime;

        public static bool Enabled { get; set; }

        public static void Update()
        {
            if (!Enabled || !TmpReflection.HasTmpText)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < nextPollTime)
            {
                return;
            }

            nextPollTime = now + PollIntervalSeconds;
            HashSet<int> seen = new HashSet<int>();

            foreach (Component component in TmpReflection.FindAllTmpTextComponents(true))
            {
                if (component == null)
                {
                    continue;
                }

                int id = component.GetInstanceID();
                seen.Add(id);
                string normalized = VisibilityUtil.NormalizeText(TmpReflection.GetTmpTextValue(component));

                if (!LastTextByInstance.TryGetValue(id, out string last))
                {
                    LastTextByInstance[id] = normalized;
                    continue;
                }

                if (string.Equals(last, normalized, StringComparison.Ordinal))
                {
                    continue;
                }

                LastTextByInstance[id] = normalized;
                TextChangeHandler.HandleTextChange(component, normalized);
            }

            if (LastTextByInstance.Count != seen.Count)
            {
                List<int> toRemove = new List<int>();
                foreach (int id in LastTextByInstance.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        toRemove.Add(id);
                    }
                }

                foreach (int id in toRemove)
                {
                    LastTextByInstance.Remove(id);
                }
            }
        }
    }
}
