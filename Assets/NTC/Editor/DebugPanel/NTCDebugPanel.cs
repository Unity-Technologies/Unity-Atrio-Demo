using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEditor;
using UnityEditor.Experimental.Rendering.NeuralGraphics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Experimental.Rendering.NeuralGraphics;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NTC.Editor
{
    /// <summary>
    /// Editor-only stats panel with an NTC on/off switch, shown in Play mode. Ported from Fantasy Kingdom's
    /// StatsCanvasController. Editor-assembly MonoBehaviours can't be attached to GameObjects, so this static
    /// class instantiates a component-free prefab on entering Play mode and ticks it from the player loop.
    /// Hotkeys (default Tab = show/hide, N = toggle NTC) are set in Preferences > NTC Debug Panel.
    /// </summary>
    [InitializeOnLoad]
    static class NTCDebugPanel
    {
        const string k_PrefabGuid = "46dbdab0d393e9447bd0975150300e73"; // DebugPanel/NTCStatsCanvas.prefab
        // ProfilingSampler name of URP's GBufferPass (GBufferPass.cs).
        const string k_GBufferMarkerName = "Draw GBuffer";
        const int k_SampleCount = 30;
        const string k_NotAvailable = "n/a";

        const string k_PanelKeyPref = "NTC.DebugPanel.PanelKey";
        const string k_NTCKeyPref = "NTC.DebugPanel.NTCKey";

        static GameObject s_Instance;
        static GameObject s_EventSystem;
        static GameObject s_Panel;
        static NTCToggleSwitch s_Switch;
        static Text s_NTCLabel, s_GBuffer, s_GPU, s_CPU, s_FPS;
        static Key s_PanelKey, s_NTCKey;

        static NTCSettings s_Settings;

        static ProfilerRecorder s_CpuMainThreadTimeRecorder;
        static ProfilerRecorder s_GpuFrameTimeRecorder;
        static ProfilerRecorder s_MainThreadTimeRecorder;
        static readonly List<ProfilerRecorderSample> s_RecorderSamples = new List<ProfilerRecorderSample>(k_SampleCount);

        static ProfilingSampler s_GBufferSampler;
        static readonly float[] s_GBufferSamples = new float[k_SampleCount];
        static int s_GBufferSampleIndex;
        static int s_GBufferSampleCount;

        static NTCDebugPanel()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static Key PanelKey
        {
            get => (Key)EditorPrefs.GetInt(k_PanelKeyPref, (int)Key.Tab);
            set { EditorPrefs.SetInt(k_PanelKeyPref, (int)value); s_PanelKey = value; }
        }

        static Key NTCKey
        {
            get => (Key)EditorPrefs.GetInt(k_NTCKeyPref, (int)Key.N);
            set { EditorPrefs.SetInt(k_NTCKeyPref, (int)value); s_NTCKey = value; }
        }

        [SettingsProvider]
        static SettingsProvider CreatePreferences() => new SettingsProvider("Preferences/NTC Debug Panel", SettingsScope.User)
        {
            guiHandler = _ =>
            {
                EditorGUI.BeginChangeCheck();
                var panelKey = (Key)EditorGUILayout.EnumPopup("Show/Hide Panel", PanelKey);
                var ntcKey = (Key)EditorGUILayout.EnumPopup("Toggle NTC", NTCKey);
                if (EditorGUI.EndChangeCheck())
                {
                    PanelKey = panelKey;
                    NTCKey = ntcKey;
                }
                EditorGUILayout.HelpBox("Play mode only. Gamepad Select also shows/hides the panel. Set a key to None to disable it.", MessageType.None);
            },
            keywords = new[] { "NTC", "Debug", "Panel", "Hotkey" },
        };

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    Setup();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Teardown();
                }
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                Teardown();
            }
        }

        static void Setup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(k_PrefabGuid));
            if (prefab == null)
            {
                Debug.LogWarning($"NTCDebugPanel: prefab with GUID {k_PrefabGuid} not found.");
                return;
            }

            s_Instance = Object.Instantiate(prefab);
            s_Instance.name = prefab.name;
            Object.DontDestroyOnLoad(s_Instance);

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                s_EventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Object.DontDestroyOnLoad(s_EventSystem);
            }

            var root = s_Instance.transform;
            s_Panel = root.Find("Panel").gameObject;
            var button = Find<Button>(root, "Panel/NTCGroup/Button");
            s_Switch = new NTCToggleSwitch(
                Find<Image>(root, "Panel/NTCGroup/Button"),
                Find<Text>(root, "Panel/NTCGroup/Button/On"),
                Find<Text>(root, "Panel/NTCGroup/Button/Off"),
                Find<RectTransform>(root, "Panel/NTCGroup/Button/Elipse"));
            s_NTCLabel = Find<Text>(root, "Panel/NTCGroup/NTCLabel");
            s_GBuffer = Find<Text>(root, "Panel/GBuffer/GBuffer_Stat");
            s_GPU = Find<Text>(root, "Panel/GPU/GPU_Stat");
            s_CPU = Find<Text>(root, "Panel/CPU/CPU_Stat");
            s_FPS = Find<Text>(root, "Panel/FPS/FPS_Stat");
            s_Panel.SetActive(false);

            s_PanelKey = PanelKey;
            s_NTCKey = NTCKey;

            if (GraphicsSettings.TryGetRenderPipelineSettings(out s_Settings))
            {
                s_Switch.SetImmediate(s_Settings.enabled);
                button.onClick.AddListener(ToggleNTC);
            }
            else
            {
                s_Switch.SetImmediate(false);
                button.interactable = false;
            }
            UpdateNTCLabel();

            s_CpuMainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Main Thread Frame Time", k_SampleCount);
            s_GpuFrameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", k_SampleCount);
            s_MainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", k_SampleCount);

            s_GBufferSampler = ProfilingSampler.Create(k_GBufferMarkerName, MarkerFlags.Default);
            if (s_GBufferSampler != null)
                s_GBufferSampler.enableRecording = true;
            s_GBufferSampleIndex = 0;
            s_GBufferSampleCount = 0;

            SetPlayerLoopTick(true);
        }

        static void Teardown()
        {
            SetPlayerLoopTick(false);

            // NTCMaterialSwitcher puts the setting and the swapped materials back on exit.
            s_Settings = null;

            s_CpuMainThreadTimeRecorder.Dispose();
            s_GpuFrameTimeRecorder.Dispose();
            s_MainThreadTimeRecorder.Dispose();

            if (s_GBufferSampler != null)
            {
                s_GBufferSampler.enableRecording = false;
                s_GBufferSampler.Dispose();
                s_GBufferSampler = null;
            }

            // Play mode objects are destroyed on exit; just drop the references.
            s_Instance = s_EventSystem = s_Panel = null;
            s_Switch = null;
            s_NTCLabel = s_GBuffer = s_GPU = s_CPU = s_FPS = null;
        }

        // Runs inside the player loop's Update phase, like MonoBehaviour.Update, so wasPressedThisFrame and
        // per-frame sampling line up with rendered frames.
        static void SetPlayerLoopTick(bool enable)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].type != typeof(UnityEngine.PlayerLoop.Update))
                    continue;

                var updateSystems = new List<PlayerLoopSystem>(systems[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                updateSystems.RemoveAll(s => s.type == typeof(NTCDebugPanel));
                if (enable)
                    updateSystems.Add(new PlayerLoopSystem { type = typeof(NTCDebugPanel), updateDelegate = Tick });
                systems[i].subSystemList = updateSystems.ToArray();
                PlayerLoop.SetPlayerLoop(loop);
                return;
            }
        }

        static void Tick()
        {
            if (s_Instance == null)
                return;

            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (WasPressed(kb, s_PanelKey) || (gp != null && gp.selectButton.wasPressedThisFrame))
                s_Panel.SetActive(!s_Panel.activeSelf);

            if (WasPressed(kb, s_NTCKey))
                ToggleNTC();

            s_Switch.Tick(Time.unscaledDeltaTime);
            SampleGBuffer();

            if (!s_Panel.activeSelf)
                return;

            s_GBuffer.text = s_GBufferSampler != null ? $"{GetGBufferAverage():F2}" : k_NotAvailable;
            s_GPU.text = FormatMs(s_GpuFrameTimeRecorder);
            s_CPU.text = FormatMs(s_CpuMainThreadTimeRecorder);

            double mainThreadNs = GetRecorderFrameAverage(s_MainThreadTimeRecorder);
            s_FPS.text = mainThreadNs > 0 ? $"{1 / (mainThreadNs * 1e-9):F1}" : k_NotAvailable;

            UpdateNTCLabel();
        }

        static void ToggleNTC()
        {
            if (s_Settings == null)
                return;
            // Swaps the tracked materials between their NTC and conventional shaders, for this
            // play session only.
            NTCMaterialSwitcher.SetNTCEnabled(!s_Settings.enabled);
            s_Switch?.Animate(s_Settings.enabled);
            UpdateNTCLabel();
        }

        static bool WasPressed(Keyboard kb, Key key)
            => kb != null && key != Key.None && kb[key].wasPressedThisFrame;

        static void UpdateNTCLabel()
        {
            if (s_NTCLabel == null)
                return;
            if (s_Settings == null)
                s_NTCLabel.text = "NTC (unavailable)";
            else if (s_Settings.enabled && !s_Settings.hasValidWeights)
                s_NTCLabel.text = "NTC (no weights)";
            else
                s_NTCLabel.text = "NTC";
        }

        static void SampleGBuffer()
        {
            if (s_GBufferSampler == null)
                return;
            s_GBufferSamples[s_GBufferSampleIndex] = s_GBufferSampler.gpuElapsedTime;
            s_GBufferSampleIndex = (s_GBufferSampleIndex + 1) % k_SampleCount;
            s_GBufferSampleCount = Mathf.Min(s_GBufferSampleCount + 1, k_SampleCount);
        }

        static float GetGBufferAverage()
        {
            if (s_GBufferSampleCount == 0)
                return 0;
            float sum = 0;
            for (int i = 0; i < s_GBufferSampleCount; i++)
                sum += s_GBufferSamples[i];
            return sum / s_GBufferSampleCount;
        }

        static string FormatMs(ProfilerRecorder recorder)
        {
            double ns = GetRecorderFrameAverage(recorder);
            return ns > 0 ? $"{ns * 1e-6:F1}" : k_NotAvailable;
        }

        static double GetRecorderFrameAverage(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0)
                return 0;

            s_RecorderSamples.Clear();
            recorder.CopyTo(s_RecorderSamples);
            double r = 0;
            for (var i = 0; i < s_RecorderSamples.Count; ++i)
                r += s_RecorderSamples[i].Value;
            return r / s_RecorderSamples.Count;
        }

        static T Find<T>(Transform root, string path) where T : Component
        {
            var child = root.Find(path);
            var component = child != null ? child.GetComponent<T>() : null;
            if (component == null)
                throw new InvalidOperationException($"NTCDebugPanel: '{path}' ({typeof(T).Name}) not found in {root.name}.");
            return component;
        }
    }
}
