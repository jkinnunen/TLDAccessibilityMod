using System;
using System.Collections.Generic;
using System.Linq;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using UnityEngine;

namespace TLDAccessibility.A11y.Output
{
    internal sealed class A11ySpeechService
    {
        private readonly IA11ySpeechBackend primaryBackend;
        private readonly IA11ySpeechBackend fallbackBackend;
        private readonly Queue<SpokenItem> queue = new Queue<SpokenItem>();
        private readonly Dictionary<string, float> lastSourceTimes = new Dictionary<string, float>();
        private readonly Dictionary<string, float> lastTextTimes = new Dictionary<string, float>();
        private readonly Queue<float> recentAutoTimes = new Queue<float>();
        private string lastSpokenText;
        private float lastSpokenTime;

        public A11ySpeechService()
        {
            primaryBackend = new TolkBackend();
            if (primaryBackend.IsAvailable)
            {
                fallbackBackend = null;
            }
            else
            {
                fallbackBackend = OperatingSystem.IsWindows()
                    ? new SystemSpeechBackend()
                    : new NullSpeechBackend();
            }
        }

        public bool IsAvailable => primaryBackend.IsAvailable || (fallbackBackend?.IsAvailable ?? false);

        public void Update()
        {
            if (queue.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            float minInterval = Settings.Instance.MinIntervalSeconds;
            if (now - lastSpokenTime < minInterval)
            {
                return;
            }

            SpokenItem next = DequeueNext();
            if (next == null)
            {
                return;
            }

            SpeakInternal(next);
        }

        public void Speak(string text, A11ySpeechPriority priority, string sourceId = null, bool isAuto = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            float now = Time.unscaledTime;
            float cooldown = Settings.Instance.CooldownSeconds;
            if (lastTextTimes.TryGetValue(text, out float lastTextTime) && now - lastTextTime < cooldown)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(sourceId) && lastSourceTimes.TryGetValue(sourceId, out float lastSourceTime))
            {
                if (now - lastSourceTime < cooldown)
                {
                    return;
                }
            }

            if (isAuto && !CanAutoSpeak(now))
            {
                return;
            }

            SpokenItem item = new SpokenItem
            {
                Text = text,
                Priority = priority,
                SourceId = sourceId,
                IsAuto = isAuto,
                EnqueueTime = now
            };

            if (priority == A11ySpeechPriority.Critical)
            {
                queue.Clear();
                SpeakInternal(item);
                return;
            }

            Enqueue(item);
        }

        public void Stop()
        {
            queue.Clear();
            primaryBackend.Stop();
            fallbackBackend?.Stop();
        }

        public void RepeatLast()
        {
            if (string.IsNullOrWhiteSpace(lastSpokenText))
            {
                return;
            }

            Speak(lastSpokenText, A11ySpeechPriority.Normal, "repeat", false);
        }

        private void SpeakInternal(SpokenItem item)
        {
            if (item == null)
            {
                return;
            }

            IA11ySpeechBackend backend = primaryBackend.IsAvailable ? primaryBackend : fallbackBackend;
            if (backend == null || !backend.IsAvailable)
            {
                return;
            }

            backend.Speak(item.Text);
            lastSpokenText = item.Text;
            lastSpokenTime = Time.unscaledTime;
            lastTextTimes[item.Text] = lastSpokenTime;
            if (!string.IsNullOrWhiteSpace(item.SourceId))
            {
                lastSourceTimes[item.SourceId] = lastSpokenTime;
            }

            if (item.IsAuto)
            {
                recentAutoTimes.Enqueue(lastSpokenTime);
            }
        }

        private bool CanAutoSpeak(float now)
        {
            float window = 1f;
            while (recentAutoTimes.Count > 0 && now - recentAutoTimes.Peek() > window)
            {
                recentAutoTimes.Dequeue();
            }

            return recentAutoTimes.Count < Settings.Instance.MaxAutoPerSecond;
        }

        private void Enqueue(SpokenItem item)
        {
            List<SpokenItem> items = queue.ToList();
            items.Add(item);
            items = items.OrderByDescending(i => i.Priority).ThenBy(i => i.EnqueueTime).ToList();
            queue.Clear();
            foreach (SpokenItem queued in items)
            {
                queue.Enqueue(queued);
            }
        }

        private SpokenItem DequeueNext()
        {
            if (queue.Count == 0)
            {
                return null;
            }

            return queue.Dequeue();
        }
    }
}
