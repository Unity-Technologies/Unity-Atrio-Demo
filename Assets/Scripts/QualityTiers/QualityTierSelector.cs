using System;
using UnityEngine;

namespace QualityTiers
{
    /// <summary>
    /// The recommended tier for this device and why it was chosen.
    /// </summary>
    public struct TierDecision
    {
        public Tier tier;
        public string reason;
        public float benchmarkScore;

        public bool HasBenchmarkScore => !float.IsNaN(benchmarkScore);
    }

    /// <summary>
    /// Picks the platform's High or Low quality level from the hardware before the first scene loads,
    /// following the rules in QualityTierSettings. Low is the default whenever nothing proves the device is fast enough.
    /// </summary>
    public static class QualityTierSelector
    {
        const string LogPrefix = "[QualityTiers] ";
        const string CommandLineArgument = "-quality-tier";
        const string BenchmarkKeyPref = "QualityTiers.BenchmarkKey";
        const string BenchmarkScorePref = "QualityTiers.BenchmarkScore";

        public static QualityTierSettings Settings { get; private set; }
        public static PlatformTierConfig Config { get; private set; }

        /// <summary>
        /// The recommendation for this device, or null when none was made (unsupported platform, or the Editor with Apply In Editor off).
        /// </summary>
        public static TierDecision? Recommendation { get; private set; }

        public static string CurrentLevelName
        {
            get
            {
                string[] names = QualitySettings.names;
                int level = QualitySettings.GetQualityLevel();
                return level >= 0 && level < names.Length ? names[level] : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Settings = null;
            Config = null;
            Recommendation = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SelectOnStartup()
        {
            Settings = Resources.Load<QualityTierSettings>(QualityTierSettings.ResourcePath);
            if (Settings == null)
            {
                Debug.LogWarning(LogPrefix + $"No QualityTierSettings asset found at Resources/{QualityTierSettings.ResourcePath}; the quality level is left as is.");
                return;
            }

            if (!TryGetPlatform(out TierPlatform platform)) return;
            Config = Settings.GetPlatform(platform);
            if (Config == null) return;

            if (Application.isEditor && !Settings.applyInEditor) return;

            HardwareInfo hardware = HardwareInfo.Current;
            TierDecision decision = Decide(Settings, Config, hardware);
            Recommendation = decision;
            Apply(decision.tier);
            Debug.Log(LogPrefix + $"{platform}: {LevelName(decision.tier)} ({decision.reason}). {hardware}.");
        }

        public static string LevelName(Tier tier) =>
            Config == null ? null : tier == Tier.High ? Config.highQualityLevel : Config.lowQualityLevel;

        public static TierDecision Decide(QualityTierSettings settings, PlatformTierConfig config, in HardwareInfo hardware)
        {
            var decision = new TierDecision { benchmarkScore = float.NaN };

            if (TryGetCommandLineOverride(out Tier overrideTier))
            {
                decision.tier = overrideTier;
                decision.reason = $"{CommandLineArgument} on the command line";
                return decision;
            }

            RuleOutcome outcome = config.Evaluate(hardware, out QualityTierRule rule);
            decision.reason = rule != null ? $"rule \"{rule.label}\"" : "no rule matched";

            bool calibrationRun = settings.benchmarkAllDevicesInDevelopmentBuilds && Debug.isDebugBuild && !Application.isEditor;
            if (outcome == RuleOutcome.Benchmark || calibrationRun)
                decision.benchmarkScore = GetBenchmarkScore(settings);

            if (outcome == RuleOutcome.Benchmark)
            {
                if (!decision.HasBenchmarkScore)
                {
                    decision.tier = Tier.Low;
                    decision.reason += ", benchmark failed";
                }
                else if (config.benchmarkHighThreshold <= 0f)
                {
                    decision.tier = Tier.Low;
                    decision.reason += $", benchmark {decision.benchmarkScore:F0} (no threshold set)";
                }
                else
                {
                    decision.tier = decision.benchmarkScore >= config.benchmarkHighThreshold ? Tier.High : Tier.Low;
                    decision.reason += $", benchmark {decision.benchmarkScore:F0} vs threshold {config.benchmarkHighThreshold:F0}";
                }
            }
            else
            {
                decision.tier = outcome == RuleOutcome.High ? Tier.High : Tier.Low;
                if (decision.HasBenchmarkScore) decision.reason += $", benchmark {decision.benchmarkScore:F0}";
            }

            if (decision.tier == Tier.High && config.minMemoryMBForHigh > 0 && hardware.memoryMB < config.minMemoryMBForHigh)
            {
                decision.tier = Tier.Low;
                decision.reason += $", but {hardware.memoryMB} MB RAM is below {config.minMemoryMBForHigh} MB";
            }

            return decision;
        }

        static bool Apply(Tier tier)
        {
            string levelName = LevelName(tier);
            int index = Array.IndexOf(QualitySettings.names, levelName);
            if (index < 0)
            {
                Debug.LogWarning(LogPrefix + $"Quality level \"{levelName}\" is not available on this platform.");
                return false;
            }

#if UNITY_EDITOR
            RememberEditorLevel();
#endif
            if (QualitySettings.GetQualityLevel() != index)
                QualitySettings.SetQualityLevel(index, true);
            return true;
        }

        // The score is cached per device, GPU driver and OS version, so the benchmark normally runs once per install.
        static float GetBenchmarkScore(QualityTierSettings settings)
        {
            string key = $"{QualityTierBenchmark.Version}|{SystemInfo.deviceModel}|{SystemInfo.graphicsDeviceName}|{SystemInfo.graphicsDeviceVersion}|{SystemInfo.operatingSystem}";
            if (!Application.isEditor && PlayerPrefs.GetString(BenchmarkKeyPref, null) == key)
                return PlayerPrefs.GetFloat(BenchmarkScorePref, float.NaN);

            float score = QualityTierBenchmark.Run(settings.benchmarkShader);
            if (!Application.isEditor && !float.IsNaN(score))
            {
                PlayerPrefs.SetString(BenchmarkKeyPref, key);
                PlayerPrefs.SetFloat(BenchmarkScorePref, score);
                PlayerPrefs.Save();
            }
            return score;
        }

        static bool TryGetCommandLineOverride(out Tier tier)
        {
            tier = Tier.Low;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], CommandLineArgument, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(args[i + 1], "high", StringComparison.OrdinalIgnoreCase)) { tier = Tier.High; return true; }
                if (string.Equals(args[i + 1], "low", StringComparison.OrdinalIgnoreCase)) { tier = Tier.Low; return true; }
            }
            return false;
        }

        static bool TryGetPlatform(out TierPlatform platform)
        {
#if UNITY_EDITOR
            // In the Editor, follow the active build target so Play mode uses that platform's levels.
            switch (UnityEditor.EditorUserBuildSettings.activeBuildTarget)
            {
                case UnityEditor.BuildTarget.Android: platform = TierPlatform.Android; return true;
                case UnityEditor.BuildTarget.iOS: platform = TierPlatform.iOS; return true;
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64: platform = TierPlatform.Windows; return true;
                case UnityEditor.BuildTarget.StandaloneOSX: platform = TierPlatform.macOS; return true;
                case UnityEditor.BuildTarget.StandaloneLinux64: platform = TierPlatform.Linux; return true;
            }
#else
            switch (Application.platform)
            {
                case RuntimePlatform.Android: platform = TierPlatform.Android; return true;
                case RuntimePlatform.IPhonePlayer: platform = TierPlatform.iOS; return true;
                case RuntimePlatform.WindowsPlayer: platform = TierPlatform.Windows; return true;
                case RuntimePlatform.OSXPlayer: platform = TierPlatform.macOS; return true;
                case RuntimePlatform.LinuxPlayer: platform = TierPlatform.Linux; return true;
            }
#endif
            platform = default;
            return false;
        }

#if UNITY_EDITOR
        // Quality changes made in Play mode would otherwise stay selected in the Editor afterwards.
        // The level is kept by name: Play mode lists only the active platform's levels, so indices differ from Edit mode.
        // Editor-lifetime state, deliberately left out of ResetStatics: it is cleared on EnteredEditMode,
        // and resetting it mid-play would orphan the RestoreEditorLevel subscription.
        static string s_EditorLevelBeforePlay;

        static void RememberEditorLevel()
        {
            if (!Application.isPlaying || s_EditorLevelBeforePlay != null) return;
            s_EditorLevelBeforePlay = CurrentLevelName;
            UnityEditor.EditorApplication.playModeStateChanged += RestoreEditorLevel;
        }

        static void RestoreEditorLevel(UnityEditor.PlayModeStateChange change)
        {
            if (change != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
            UnityEditor.EditorApplication.playModeStateChanged -= RestoreEditorLevel;
            int index = Array.IndexOf(QualitySettings.names, s_EditorLevelBeforePlay);
            if (index >= 0 && QualitySettings.GetQualityLevel() != index)
                QualitySettings.SetQualityLevel(index, true);
            s_EditorLevelBeforePlay = null;
        }
#endif
    }
}
