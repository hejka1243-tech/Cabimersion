using System;
using UnityEngine;
using UnityModManagerNet;

namespace CabImmersionPack
{
    public static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Logger { get; private set; }
        public static bool Enabled { get; private set; }
        public static Settings Settings { get; private set; }

        private static GameObject managerObject;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUnload = OnUnload;

            // Load saved settings from the XML file
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Settings.ClampAll();

            try
            {
                managerObject = new GameObject("ImmersionCameraManager");
                managerObject.AddComponent<ImmersionCameraEffects>();
                UnityEngine.Object.DontDestroyOnLoad(managerObject);

                Logger.Log("Cab Immersion Pack loaded successfully.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error while initializing the mod: {ex}");
                return false;
            }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Enabled = false;
            if (managerObject != null)
            {
                UnityEngine.Object.Destroy(managerObject);
                managerObject = null;
            }
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Intensity Presets");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Very Light")) Settings.ApplyPreset(Settings.PresetLevel.VeryLight);
            if (GUILayout.Button("Light")) Settings.ApplyPreset(Settings.PresetLevel.Light);
            if (GUILayout.Button("Medium")) Settings.ApplyPreset(Settings.PresetLevel.Medium);
            if (GUILayout.Button("Strong")) Settings.ApplyPreset(Settings.PresetLevel.Strong);
            GUILayout.EndHorizontal();

            GUILayout.Label($"Global Intensity: {Settings.globalIntensity:0.00}x");
            Settings.globalIntensity = GUILayout.HorizontalSlider(Settings.globalIntensity, 0.1f, 3f);

            GUILayout.Space(8);
            Settings.Draw(modEntry); // Automatic UMM drawing for the sliders above

            if (Settings.showDebugOverlay && ImmersionCameraEffects.Instance != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("Debug");
                GUILayout.Label(ImmersionCameraEffects.Instance.GetDebugInfo());
            }
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry); // Persists to the XML file when the UMM menu closes
        }
    }
}