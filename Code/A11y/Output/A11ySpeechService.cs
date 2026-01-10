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
        internal enum SpeechSuppressionReason
        {
            None,
            EmptyText,
            CooldownText,
            CooldownSource,
            AutoRateLimit,
            Exception
        }

        internal sealed class SpeechRequestOptions
        {
            public bool BypassCooldown { get; set; }
            public bool BypassAutoRateLimit { get; set; }
        }

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
            fallbackBackend = OperatingSystem.IsWindows()
                ? new SystemSpeechBackend()
                : new NullSpeechBackend();
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
            TrySpeak(text, priority, sourceId, isAuto, out _, null);
        }

        public bool TrySpeak(
            string text,
            A11ySpeechPriority priority,
            string sourceId,
            bool isAuto,
            out SpeechSuppressionReason suppressionReason,
            SpeechRequestOptions options)
        {
            suppressionReason = SpeechSuppressionReason.None;

            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    suppressionReason = SpeechSuppressionReason.EmptyText;
                    return false;
                }

                float now = Time.unscaledTime;
                float cooldown = Settings.Instance.CooldownSeconds;
                bool bypassCooldown = options?.BypassCooldown ?? false;
                if (!bypassCooldown)
                {
                    if (lastTextTimes.TryGetValue(text, out float lastTextTime) && now - lastTextTime < cooldown)
                    {
                        suppressionReason = SpeechSuppressionReason.CooldownText;
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(sourceId) && lastSourceTimes.TryGetValue(sourceId, out float lastSourceTime))
                    {
                        if (now - lastSourceTime < cooldown)
                        {
                            suppressionReason = SpeechSuppressionReason.CooldownSource;
                            return false;
                        }
                    }
                }

                bool bypassAutoRateLimit = options?.BypassAutoRateLimit ?? false;
                if (isAuto && !bypassAutoRateLimit && !CanAutoSpeak(now))
                {
                    suppressionReason = SpeechSuppressionReason.AutoRateLimit;
                    return false;
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
                    return true;
                }

                Enqueue(item);
                return true;
            }
            catch (Exception ex)
            {
                suppressionReason = SpeechSuppressionReason.Exception;
                A11yLogger.Warning($"Speech request failed: {ex}");
                return false;
            }
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

            bool primaryAvailable = primaryBackend.IsAvailable;
            bool fallbackAvailable = fallbackBackend?.IsAvailable ?? false;
            if (!primaryAvailable && !fallbackAvailable)
            {
                A11yLogger.Warning("Speech output unavailable: no backend is ready.");
                return;
            }

            bool spoke = false;
            if (primaryAvailable)
            {
                spoke = primaryBackend.Speak(item.Text);
            }

            if (!spoke && fallbackAvailable)
            {
                A11yLogger.Warning("Primary speech backend unavailable or failed; attempting fallback.");
                spoke = fallbackBackend.Speak(item.Text);
            }

            if (!spoke)
            {
                return;
            }

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
