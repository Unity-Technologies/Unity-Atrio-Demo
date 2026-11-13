using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace QualityTiers
{
    public enum TierPlatform { Android, iOS, Windows, macOS, Linux }

    public enum Tier { Low, High }

    public enum RuleOutcome { High, Low, Benchmark }

    public enum RuleField { GpuName, DeviceModel, ProcessorType }

    /// <summary>
    /// The hardware strings and numbers that rules are matched against.
    /// </summary>
    [Serializable]
    public struct HardwareInfo
    {
        public string gpuName;
        public string deviceModel;
        public string processorType;
        public int memoryMB;
        public int cpuCores;

        public static HardwareInfo Current => new HardwareInfo
        {
            gpuName = SystemInfo.graphicsDeviceName,
            deviceModel = SystemInfo.deviceModel,
            processorType = SystemInfo.processorType,
            memoryMB = SystemInfo.systemMemorySize,
            cpuCores = SystemInfo.processorCount,
        };

        public string Get(RuleField field)
        {
            switch (field)
            {
                case RuleField.DeviceModel: return deviceModel;
                case RuleField.ProcessorType: return processorType;
                default: return gpuName;
            }
        }

        public override string ToString() =>
            $"GPU \"{gpuName}\", model \"{deviceModel}\", CPU \"{processorType}\", {memoryMB} MB RAM, {cpuCores} cores";
    }

    [Serializable]
    public class QualityTierRule
    {
        [Tooltip("Shown in the log when this rule decides the level.")]
        public string label;

        public bool enabled = true;

        [Tooltip("Which hardware string the pattern is matched against.")]
        public RuleField field;

        [Tooltip("Case-insensitive regular expression. When a version check is set, the first capture group is read as the version number.")]
        public string pattern;

        [Tooltip("When above 0, the captured number must be at least this.")]
        public int minVersion;

        [Tooltip("When above 0, the captured number must be at most this.")]
        public int maxVersion;

        [Tooltip("When above 0, the device must report at least this much system RAM, in MB.")]
        public int minMemoryMB;

        [Tooltip("When above 0, the device must report at least this many CPU cores.")]
        public int minCpuCores;

        public RuleOutcome outcome;

        public QualityTierRule() { }

        public QualityTierRule(string label, RuleField field, string pattern, RuleOutcome outcome, int minVersion = 0, int maxVersion = 0)
        {
            this.label = label;
            this.field = field;
            this.pattern = pattern;
            this.outcome = outcome;
            this.minVersion = minVersion;
            this.maxVersion = maxVersion;
        }

        public bool Matches(in HardwareInfo hardware)
        {
            if (!enabled || string.IsNullOrEmpty(pattern)) return false;

            Match match;
            try
            {
                match = Regex.Match(hardware.Get(field) ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                // An invalid pattern never matches; the inspector reports it.
                return false;
            }
            if (!match.Success) return false;

            if (minVersion > 0 || maxVersion > 0)
            {
                if (match.Groups.Count < 2 || !int.TryParse(match.Groups[1].Value, out int version)) return false;
                if (minVersion > 0 && version < minVersion) return false;
                if (maxVersion > 0 && version > maxVersion) return false;
            }

            if (minMemoryMB > 0 && hardware.memoryMB < minMemoryMB) return false;
            if (minCpuCores > 0 && hardware.cpuCores < minCpuCores) return false;
            return true;
        }
    }

    [Serializable]
    public class PlatformTierConfig
    {
        public TierPlatform platform;

        [Tooltip("Quality level used for the High tier on this platform (matched by name).")]
        public string highQualityLevel;

        [Tooltip("Quality level used for the Low tier on this platform (matched by name).")]
        public string lowQualityLevel;

        [Tooltip("A High result is lowered to Low when the device reports less system RAM than this, in MB. 0 turns the check off.")]
        public int minMemoryMBForHigh;

        [Tooltip("Used when no rule matches. Benchmark measures the GPU on first launch, so hardware released later is still classified.")]
        public RuleOutcome fallback = RuleOutcome.Benchmark;

        [Tooltip("Benchmark score at or above which a device gets High. 0 means not calibrated yet, so benchmarked devices get Low.")]
        public float benchmarkHighThreshold;

        [Tooltip("Checked top to bottom; the first rule that matches decides.")]
        public List<QualityTierRule> rules = new List<QualityTierRule>();

        public RuleOutcome Evaluate(in HardwareInfo hardware, out QualityTierRule matchedRule)
        {
            foreach (QualityTierRule rule in rules)
            {
                if (rule != null && rule.Matches(hardware))
                {
                    matchedRule = rule;
                    return rule.outcome;
                }
            }
            matchedRule = null;
            return fallback;
        }
    }

    /// <summary>
    /// Which quality level each platform gets for its hardware. Loaded from Resources at startup by QualityTierSelector.
    /// </summary>
    [CreateAssetMenu(fileName = "QualityTierSettings", menuName = "Rendering/Quality Tier Settings")]
    public class QualityTierSettings : ScriptableObject
    {
        public const string ResourcePath = "QualityTierSettings";

        [Tooltip("Apply the recommended level when entering Play mode in the Editor. Off keeps the level selected in the Editor.")]
        public bool applyInEditor;

        [Tooltip("Shader used by the GPU benchmark.")]
        public Shader benchmarkShader;

        [Tooltip("In development builds, run the benchmark even when a rule decides the level, so its score appears in the startup log for calibration.")]
        public bool benchmarkAllDevicesInDevelopmentBuilds = true;

        [Header("Platforms")]
        public List<PlatformTierConfig> platforms = CreateDefaultPlatforms();

        public PlatformTierConfig GetPlatform(TierPlatform platform) => platforms.Find(config => config != null && config.platform == platform);

        [ContextMenu("Reset Platforms To Defaults")]
        void ResetPlatformsToDefaults() => platforms = CreateDefaultPlatforms();

        public static List<PlatformTierConfig> CreateDefaultPlatforms() => new List<PlatformTierConfig>
        {
            CreateAndroidDefaults(),
            CreateIOSDefaults(),
            CreateDesktopDefaults(TierPlatform.Windows),
            CreateMacDefaults(),
            CreateDesktopDefaults(TierPlatform.Linux),
        };

        // The High bar on mobile is Galaxy S26 Ultra / iPhone 17 Pro class GPU performance.
        static PlatformTierConfig CreateAndroidDefaults() => new PlatformTierConfig
        {
            platform = TierPlatform.Android,
            highQualityLevel = "Mobile_High",
            lowQualityLevel = "Mobile_Low",
            minMemoryMBForHigh = 10500,
            rules = new List<QualityTierRule>
            {
                new QualityTierRule("Galaxy S26 series", RuleField.DeviceModel, @"SM-S94\d", RuleOutcome.High),
                // The cut-down "8 Elite Gen 5 V Series" GPU is also sold as Adreno 840, so other 840 phones are measured.
                new QualityTierRule("Adreno 840 (full or V Series)", RuleField.GpuName, @"Adreno[^0-9]*(\d+)", RuleOutcome.Benchmark, 840, 849),
                new QualityTierRule("Adreno 830 and older", RuleField.GpuName, @"Adreno[^0-9]*(\d+)", RuleOutcome.Low, 0, 839),
                new QualityTierRule("Xclipse 960 (Exynos 2600)", RuleField.GpuName, @"Xclipse[^0-9]*(\d+)", RuleOutcome.High, 960, 969),
                new QualityTierRule("Xclipse 950 and older", RuleField.GpuName, @"Xclipse[^0-9]*(\d+)", RuleOutcome.Low, 0, 959),
                new QualityTierRule("Mali-G1-Ultra (Dimensity 9500)", RuleField.GpuName, @"Mali-?G1-?Ultra", RuleOutcome.High),
                new QualityTierRule("Mali / Immortalis G-series before G1", RuleField.GpuName, @"(?:Mali|Immortalis)-G\d{2,3}\b", RuleOutcome.Low),
                new QualityTierRule("PowerVR (e.g. Tensor G5)", RuleField.GpuName, @"PowerVR", RuleOutcome.Low),
                new QualityTierRule("Maleoon (Huawei Kirin)", RuleField.GpuName, @"Maleoon", RuleOutcome.Low),
            },
        };

        static PlatformTierConfig CreateIOSDefaults() => new PlatformTierConfig
        {
            platform = TierPlatform.iOS,
            highQualityLevel = "Mobile_High",
            lowQualityLevel = "Mobile_Low",
            rules = new List<QualityTierRule>
            {
                new QualityTierRule("iPhone 17 Pro / Pro Max", RuleField.DeviceModel, @"^iPhone18,[12]$", RuleOutcome.High),
                new QualityTierRule("iPhone 17 / Air / 17e", RuleField.DeviceModel, @"^iPhone18,[3-5]$", RuleOutcome.Low),
                new QualityTierRule("iPhone 16 and older", RuleField.DeviceModel, @"^iPhone(\d+),", RuleOutcome.Low, 0, 17),
                new QualityTierRule("iPad Pro (M4)", RuleField.DeviceModel, @"^iPad16,[3-6]$", RuleOutcome.High),
                new QualityTierRule("A20 Pro and newer", RuleField.GpuName, @"Apple A(\d+) Pro", RuleOutcome.High, 20),
                new QualityTierRule("A18 Pro and older", RuleField.GpuName, @"Apple A(\d+) Pro", RuleOutcome.Low, 0, 18),
                new QualityTierRule("A19 and older (non-Pro)", RuleField.GpuName, @"Apple A(\d+) GPU", RuleOutcome.Low, 0, 19),
                new QualityTierRule("M1 to M3 iPads", RuleField.GpuName, @"Apple M(\d+)", RuleOutcome.Low, 0, 3),
            },
        };

        // The High bar on Windows and Linux is RTX 2080 class performance.
        static PlatformTierConfig CreateDesktopDefaults(TierPlatform platform) => new PlatformTierConfig
        {
            platform = platform,
            highQualityLevel = "Desktop_High",
            lowQualityLevel = "Desktop_Low",
            rules = new List<QualityTierRule>
            {
                // Laptop GPUs are slower than their desktop namesakes, so they are checked first.
                new QualityTierRule("RTX 3080 Laptop and up", RuleField.GpuName, @"RTX (\d{4}).*Laptop", RuleOutcome.High, 3080, 3099),
                new QualityTierRule("RTX 4070 Laptop and up", RuleField.GpuName, @"RTX (\d{4}).*Laptop", RuleOutcome.High, 4070, 4099),
                new QualityTierRule("RTX 5070 Laptop and up", RuleField.GpuName, @"RTX (\d{4}).*Laptop", RuleOutcome.High, 5070, 5099),
                new QualityTierRule("Other RTX 20 to 50 series Laptop", RuleField.GpuName, @"RTX (\d{4}).*Laptop", RuleOutcome.Low, 2000, 5099),
                new QualityTierRule("RTX 3060 Ti", RuleField.GpuName, @"RTX 3060 Ti", RuleOutcome.High),
                new QualityTierRule("RTX 2080 / Super / Ti", RuleField.GpuName, @"RTX (\d{4})", RuleOutcome.High, 2080, 2099),
                new QualityTierRule("RTX 3070 and up", RuleField.GpuName, @"RTX (\d{4})", RuleOutcome.High, 3070, 3099),
                new QualityTierRule("RTX 4060 and up", RuleField.GpuName, @"RTX (\d{4})", RuleOutcome.High, 4060, 4099),
                new QualityTierRule("RTX 5060 and up", RuleField.GpuName, @"RTX (\d{4})", RuleOutcome.High, 5060, 5099),
                new QualityTierRule("RTX 20 to 50 series below the bar", RuleField.GpuName, @"RTX (\d{4})", RuleOutcome.Low, 2000, 5099),
                new QualityTierRule("GTX / GT / MX GeForce", RuleField.GpuName, @"GeForce (?:GTX|GT |MX)", RuleOutcome.Low),
                new QualityTierRule("Radeon RX 6700 XT and up", RuleField.GpuName, @"Radeon RX (\d{4})", RuleOutcome.High, 6700, 6999),
                new QualityTierRule("Radeon RX 7700 XT and up", RuleField.GpuName, @"Radeon RX (\d{4})", RuleOutcome.High, 7700, 7999),
                new QualityTierRule("Radeon RX 9060 XT and up", RuleField.GpuName, @"Radeon RX (\d{4})", RuleOutcome.High, 9060, 9099),
                new QualityTierRule("Radeon RX 5000 to 9000 below the bar", RuleField.GpuName, @"Radeon RX (\d{4})", RuleOutcome.Low, 5000, 9099),
                new QualityTierRule("Radeon RX 400 / 500 / Vega", RuleField.GpuName, @"Radeon (?:RX [45]\d{2}\b|RX Vega|Vega)", RuleOutcome.Low),
                new QualityTierRule("Radeon integrated graphics", RuleField.GpuName, @"Radeon(?:\(TM\))? (?:\d{3}M\b|Graphics)", RuleOutcome.Low),
                new QualityTierRule("Intel Arc B580", RuleField.GpuName, @"Arc.*B580", RuleOutcome.High),
                new QualityTierRule("Intel Arc A-series / B570", RuleField.GpuName, @"Arc.*(?:A\d{3}|B570)", RuleOutcome.Low),
                new QualityTierRule("Intel integrated graphics", RuleField.GpuName, @"Intel.*(?:UHD|Iris|HD Graphics|Arc\(TM\) Graphics|Arc Graphics)", RuleOutcome.Low),
                new QualityTierRule("Software renderer", RuleField.GpuName, @"Basic Render|llvmpipe|SwiftShader", RuleOutcome.Low),
            },
        };

        static PlatformTierConfig CreateMacDefaults() => new PlatformTierConfig
        {
            platform = TierPlatform.macOS,
            highQualityLevel = "Desktop_High",
            lowQualityLevel = "Desktop_Low",
            rules = new List<QualityTierRule>
            {
                new QualityTierRule("M2 Ultra and newer", RuleField.GpuName, @"Apple M(\d+) Ultra", RuleOutcome.High, 2),
                new QualityTierRule("M1 Ultra", RuleField.GpuName, @"Apple M(\d+) Ultra", RuleOutcome.Low, 0, 1),
                new QualityTierRule("M3 Max and newer", RuleField.GpuName, @"Apple M(\d+) Max", RuleOutcome.High, 3),
                new QualityTierRule("M1 / M2 Max", RuleField.GpuName, @"Apple M(\d+) Max", RuleOutcome.Low, 0, 2),
                new QualityTierRule("M1 to M4 Pro", RuleField.GpuName, @"Apple M(\d+) Pro", RuleOutcome.Low, 0, 4),
                new QualityTierRule("M1 to M6 (base)", RuleField.GpuName, @"Apple M(\d+)(?!\d| Pro| Max| Ultra)", RuleOutcome.Low, 0, 6),
                new QualityTierRule("Intel Mac GPUs", RuleField.GpuName, @"Radeon Pro|Intel", RuleOutcome.Low),
            },
        };
    }
}
