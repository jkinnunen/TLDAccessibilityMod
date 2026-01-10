using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TLDAccessibility.A11y.Logging;
using TLDAccessibility.A11y.Model;
using UnityEngine;

// Changelog: Added queue/engine diagnostics, output result tracking, and safe queue clearing for improved narration reliability.
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
        private float lastOutputOkTime;
        private string lastOutputError;
        private string lastOutputErrorType;
        private float lastOutputElapsedMs;

        public A11ySpeechService()
        {
            primaryBackend = new TolkBackend();
            fallbackBackend = OperatingSystem.IsWindows()
                ? new SystemSpeechBackend()
                : new NullSpeechBackend();
        }

        public bool IsAvailable => primaryBackend.IsAvailable || (fallbackBackend?.IsAvailable ?? false);

        public int QueueCount => queue.Count;

        public float LastOutputOkTime => lastOutputOkTime;

        public string LastOutputError => lastOutputError;

        public string LastOutputErrorType => lastOutputErrorType;

        public float LastOutputElapsedMs => lastOutputElapsedMs;

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

            SpeakInternal(next, false);
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
                    LogSpeechSuppressed(text, sourceId, suppressionReason);
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
                        LogSpeechSuppressed(text, sourceId, suppressionReason);
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(sourceId) && lastSourceTimes.TryGetValue(sourceId, out float lastSourceTime))
                    {
                        if (now - lastSourceTime < cooldown)
                        {
                            suppressionReason = SpeechSuppressionReason.CooldownSource;
                            LogSpeechSuppressed(text, sourceId, suppressionReason);
                            return false;
                        }
                    }
                }

                bool bypassAutoRateLimit = options?.BypassAutoRateLimit ?? false;
                if (isAuto && !bypassAutoRateLimit && !CanAutoSpeak(now))
                {
                    suppressionReason = SpeechSuppressionReason.AutoRateLimit;
                    LogSpeechSuppressed(text, sourceId, suppressionReason);
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
                    LogSpeechEnqueue(item, true);
                    SpeakInternal(item, true);
                    return true;
                }

                Enqueue(item);
                LogSpeechEnqueue(item, false);
                return true;
            }
            catch (Exception ex)
            {
                suppressionReason = SpeechSuppressionReason.Exception;
                LogSpeechSuppressed(text, sourceId, suppressionReason);
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

        public void ClearQueue(string reason)
        {
            int count = queue.Count;
            queue.Clear();
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "(none)" : reason;
            A11yLogger.Info($"Speech queue cleared: reason={safeReason} cleared={count}");
        }

        public void RepeatLast()
        {
            if (string.IsNullOrWhiteSpace(lastSpokenText))
            {
                return;
            }

            Speak(lastSpokenText, A11ySpeechPriority.Normal, "repeat", false);
        }

        private void SpeakInternal(SpokenItem item, bool interrupt)
        {
            if (item == null)
            {
                return;
            }

            bool primaryAvailable = primaryBackend.IsAvailable;
            bool fallbackAvailable = fallbackBackend?.IsAvailable ?? false;
            if (!primaryAvailable && !fallbackAvailable)
            {
                lastOutputError = "No backend available.";
                lastOutputErrorType = "Unavailable";
                A11yLogger.Warning("Speech output unavailable: no backend is ready.");
                return;
            }

            bool spoke = false;
            if (primaryAvailable)
            {
                spoke = TrySpeakBackend(primaryBackend, item.Text, interrupt);
            }

            if (!spoke && fallbackAvailable)
            {
                A11yLogger.Warning("Primary speech backend unavailable or failed; attempting fallback.");
                spoke = TrySpeakBackend(fallbackBackend, item.Text, interrupt);
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

        private bool TrySpeakBackend(IA11ySpeechBackend backend, string text, bool interrupt)
        {
            string engineName = backend?.GetType().Name ?? "(unknown)";
            int textLen = text?.Length ?? 0;
            A11yLogger.Info($"Speech OUT BEGIN engine={engineName} interrupt={interrupt.ToString().ToLowerInvariant()} textLen={textLen}");
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                bool ok = backend != null && backend.Speak(text);
                timer.Stop();
                lastOutputElapsedMs = (float)timer.Elapsed.TotalMilliseconds;
                if (ok)
                {
                    lastOutputOkTime = Time.unscaledTime;
                    lastOutputError = string.Empty;
                    lastOutputErrorType = string.Empty;
                    A11yLogger.Info($"Speech OUT END ok=true elapsedMs={lastOutputElapsedMs:0} err=\"\"");
                    return true;
                }

                lastOutputError = "backend_returned_false";
                lastOutputErrorType = engineName;
                A11yLogger.Info($"Speech OUT END ok=false elapsedMs={lastOutputElapsedMs:0} err=\"{lastOutputError}\"");
                return false;
            }
            catch (Exception ex)
            {
                timer.Stop();
                lastOutputElapsedMs = (float)timer.Elapsed.TotalMilliseconds;
                lastOutputError = ex.Message;
                lastOutputErrorType = ex.GetType().Name;
                A11yLogger.Warning($"Speech OUT FAIL exType={ex.GetType().Name} msg=\"{ex.Message}\"");
                A11yLogger.Info($"Speech OUT END ok=false elapsedMs={lastOutputElapsedMs:0} err=\"{ex.Message}\"");
                return false;
            }
        }

        private void LogSpeechEnqueue(SpokenItem item, bool interrupt)
        {
            if (item == null)
            {
                return;
            }

            string safeText = string.IsNullOrWhiteSpace(item.Text) ? "(none)" : item.Text;
            string safeSource = string.IsNullOrWhiteSpace(item.SourceId) ? "(none)" : item.SourceId;
            int count = queue.Count;
            A11yLogger.Info($"Speech ENQUEUE q={count} interrupt={interrupt.ToString().ToLowerInvariant()} text=\"{safeText}\" source={safeSource}");
        }

        private void LogSpeechSuppressed(string text, string sourceId, SpeechSuppressionReason reason)
        {
            string safeText = string.IsNullOrWhiteSpace(text) ? "(none)" : text;
            string safeSource = string.IsNullOrWhiteSpace(sourceId) ? "(none)" : sourceId;
            A11yLogger.Info($"Speech SUPPRESS reason={reason} text=\"{safeText}\" source={safeSource}");
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

            SpokenItem item = queue.Dequeue();
            string safeText = string.IsNullOrWhiteSpace(item?.Text) ? "(none)" : item.Text;
            A11yLogger.Info($"Speech DEQUEUE q={queue.Count} text=\"{safeText}\"");
            return item;
        }
    }
}
