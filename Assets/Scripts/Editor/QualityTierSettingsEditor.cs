using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace QualityTiers
{
    [CustomEditor(typeof(QualityTierSettings))]
    public class QualityTierSettingsEditor : Editor
    {
        // Simulator state lives only in the inspector session.
        static TierPlatform s_SimPlatform = TierPlatform.Android;
        static HardwareInfo s_SimHardware = new HardwareInfo
        {
            gpuName = "Adreno (TM) 840",
            deviceModel = "samsung SM-S948B",
            processorType = "ARMv8 64 bit",
            memoryMB = 11264,
            cpuCores = 8,
        };

        float m_BenchmarkScore = float.NaN;

        public override void OnInspectorGUI()
        {
            var settings = (QualityTierSettings)target;

            DrawValidation(settings);
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            DrawThisMachine(settings);

            EditorGUILayout.Space(10);
            DrawSimulator(settings);
        }

        static void DrawValidation(QualityTierSettings settings)
        {
            if (settings.benchmarkShader == null)
                EditorGUILayout.HelpBox("Benchmark Shader is not set, so benchmarked devices get Low.", MessageType.Warning);

            string[] levelNames = QualitySettings.names;
            foreach (PlatformTierConfig config in settings.platforms)
            {
                if (config == null) continue;
                foreach (string levelName in new[] { config.highQualityLevel, config.lowQualityLevel })
                {
                    if (Array.IndexOf(levelNames, levelName) < 0)
                        EditorGUILayout.HelpBox($"{config.platform}: there is no quality level named \"{levelName}\".", MessageType.Error);
                }

                if (config.benchmarkHighThreshold <= 0f)
                    EditorGUILayout.HelpBox($"{config.platform}: Benchmark High Threshold is not calibrated yet, so devices that are benchmarked get Low.", MessageType.Info);

                foreach (QualityTierRule rule in config.rules)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.pattern)) continue;
                    try
                    {
                        _ = new Regex(rule.pattern);
                    }
                    catch (ArgumentException exception)
                    {
                        EditorGUILayout.HelpBox($"{config.platform}, rule \"{rule.label}\": invalid pattern. {exception.Message}", MessageType.Error);
                    }
                }
            }
        }

        void DrawThisMachine(QualityTierSettings settings)
        {
            EditorGUILayout.LabelField("This Machine", EditorStyles.boldLabel);

            HardwareInfo hardware = HardwareInfo.Current;
            TierPlatform? platform = EditorMachinePlatform();
            EditorGUILayout.LabelField("Hardware", hardware.ToString(), EditorStyles.wordWrappedMiniLabel);

            PlatformTierConfig config = platform.HasValue ? settings.GetPlatform(platform.Value) : null;
            if (config == null)
            {
                EditorGUILayout.HelpBox("No platform section matches this machine.", MessageType.None);
                return;
            }

            DrawEvaluation(config, hardware, m_BenchmarkScore);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Benchmark"))
                    m_BenchmarkScore = QualityTierBenchmark.Run(settings.benchmarkShader);
                if (GUILayout.Button("Copy Hardware Info"))
                    EditorGUIUtility.systemCopyBuffer = hardware.ToString();
                if (GUILayout.Button("Use In Simulator"))
                {
                    s_SimPlatform = platform.Value;
                    s_SimHardware = hardware;
                }
            }

            if (!float.IsNaN(m_BenchmarkScore))
                EditorGUILayout.LabelField("Benchmark Score", m_BenchmarkScore.ToString("F0"));
        }

        static void DrawSimulator(QualityTierSettings settings)
        {
            EditorGUILayout.LabelField("Simulator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Enter what a device reports to see which rule would decide its level.", EditorStyles.wordWrappedMiniLabel);

            s_SimPlatform = (TierPlatform)EditorGUILayout.EnumPopup("Platform", s_SimPlatform);
            s_SimHardware.gpuName = EditorGUILayout.TextField("GPU Name", s_SimHardware.gpuName);
            s_SimHardware.deviceModel = EditorGUILayout.TextField("Device Model", s_SimHardware.deviceModel);
            s_SimHardware.processorType = EditorGUILayout.TextField("Processor Type", s_SimHardware.processorType);
            s_SimHardware.memoryMB = EditorGUILayout.IntField("System RAM (MB)", s_SimHardware.memoryMB);
            s_SimHardware.cpuCores = EditorGUILayout.IntField("CPU Cores", s_SimHardware.cpuCores);

            PlatformTierConfig config = settings.GetPlatform(s_SimPlatform);
            if (config == null)
            {
                EditorGUILayout.HelpBox($"There is no {s_SimPlatform} section.", MessageType.Warning);
                return;
            }
            DrawEvaluation(config, s_SimHardware, float.NaN);
        }

        static void DrawEvaluation(PlatformTierConfig config, in HardwareInfo hardware, float benchmarkScore)
        {
            RuleOutcome outcome = config.Evaluate(hardware, out QualityTierRule rule);
            string decidedBy = rule != null ? $"Rule \"{rule.label}\"" : "No rule matched, fallback";

            string result;
            if (outcome != RuleOutcome.Benchmark)
                result = outcome == RuleOutcome.High ? config.highQualityLevel : config.lowQualityLevel;
            else if (float.IsNaN(benchmarkScore))
                result = "decided by the benchmark on the device";
            else if (config.benchmarkHighThreshold <= 0f)
                result = $"{config.lowQualityLevel} (score {benchmarkScore:F0}, no threshold set)";
            else
                result = $"{(benchmarkScore >= config.benchmarkHighThreshold ? config.highQualityLevel : config.lowQualityLevel)} (score {benchmarkScore:F0} vs {config.benchmarkHighThreshold:F0})";

            if (outcome != RuleOutcome.Low && config.minMemoryMBForHigh > 0 && hardware.memoryMB < config.minMemoryMBForHigh)
                result += $", but High is lowered to {config.lowQualityLevel} below {config.minMemoryMBForHigh} MB RAM";

            EditorGUILayout.HelpBox($"{decidedBy} → {outcome}\nResult: {result}", MessageType.None);
        }

        static TierPlatform? EditorMachinePlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor: return TierPlatform.Windows;
                case RuntimePlatform.OSXEditor: return TierPlatform.macOS;
                case RuntimePlatform.LinuxEditor: return TierPlatform.Linux;
                default: return null;
            }
        }
    }
}
