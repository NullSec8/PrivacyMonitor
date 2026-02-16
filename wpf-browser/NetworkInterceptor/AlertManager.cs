using System;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Event args for high-risk or critical risk detected (realtime or after replay).
    /// </summary>
    public sealed class RiskAlertEventArgs : EventArgs
    {
        public InterceptedRequestItem Item { get; }
        public string RiskLevel { get; }
        public int RiskScore { get; }
        public bool IsReplay { get; }

        public RiskAlertEventArgs(InterceptedRequestItem item, string riskLevel, int riskScore, bool isReplay = false)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            RiskLevel = riskLevel ?? "";
            RiskScore = riskScore;
            IsReplay = isReplay;
        }
    }

    /// <summary>
    /// Manages alerts for high-risk and critical requests. Configurable thresholds;
    /// raises events for UI notification, optional sound/flash/tray integration.
    /// </summary>
    public sealed class AlertManager
    {
        private int _highRiskThreshold = 70;
        private int _criticalThreshold = 85;
        private bool _alertOnHigh = true;
        private bool _alertOnCritical = true;
        private bool _playSoundOnCritical = true;

        /// <summary>Score &gt;= this triggers High alert (default 70).</summary>
        public int HighRiskThreshold { get => _highRiskThreshold; set => _highRiskThreshold = Math.Clamp(value, 0, 100); }

        /// <summary>Score &gt;= this triggers Critical alert (default 85).</summary>
        public int CriticalThreshold { get => _criticalThreshold; set => _criticalThreshold = Math.Clamp(value, 0, 100); }

        /// <summary>Raise HighRiskDetected when score &gt;= HighRiskThreshold.</summary>
        public bool AlertOnHigh { get => _alertOnHigh; set => _alertOnHigh = value; }

        /// <summary>Raise CriticalRiskDetected when score &gt;= CriticalThreshold.</summary>
        public bool AlertOnCritical { get => _alertOnCritical; set => _alertOnCritical = value; }

        /// <summary>Play system beep when Critical alert fires (optional).</summary>
        public bool PlaySoundOnCritical { get => _playSoundOnCritical; set => _playSoundOnCritical = value; }

        /// <summary>Raised when a request reaches High risk (score &gt;= HighRiskThreshold). Marshal to UI thread if needed.</summary>
        public event EventHandler<RiskAlertEventArgs>? HighRiskDetected;

        /// <summary>Raised when a request reaches Critical risk (score &gt;= CriticalThreshold). Marshal to UI thread if needed.</summary>
        public event EventHandler<RiskAlertEventArgs>? CriticalRiskDetected;

        /// <summary>Call when a request's risk is updated (e.g. after RecordResponse or replay). Fires events if thresholds exceeded.</summary>
        public void Evaluate(InterceptedRequestItem item, bool isReplay = false)
        {
            if (item == null) return;
            int score = item.RiskScore;
            string level = item.RiskLevel ?? "";

            if (_alertOnCritical && score >= _criticalThreshold && (level == "Critical" || score >= _criticalThreshold))
            {
                CriticalRiskDetected?.Invoke(this, new RiskAlertEventArgs(item, level, score, isReplay));
                if (_playSoundOnCritical)
                {
                    try { System.Media.SystemSounds.Beep.Play(); } catch { }
                }
            }
            else if (_alertOnHigh && score >= _highRiskThreshold)
            {
                HighRiskDetected?.Invoke(this, new RiskAlertEventArgs(item, level, score, isReplay));
            }
        }
    }
}
