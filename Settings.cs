using UnityEngine;
using UnityModManagerNet;

namespace CabImmersionPack
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        // Not drawn automatically (no [Draw]); controlled via the preset buttons
        // and the "Global Intensity" slider that Main.cs draws manually.
        public float globalIntensity = 1.0f;

        [Header("Suspension & Impact Motion")]
        [Draw("Rail Joint Impact Intensity", Min = 0f, Max = 6f, Precision = 1)]
        public float railJointsPower = 1.0f;

        [Draw("Rail Joint Interval (meters)", Min = 2f, Max = 50f, Precision = 0, Tooltip = "Distance between rail joints in meters. Allows matching joint clicks to custom track mods.")]
        public float railJointInterval = 20.0f;

        [Draw("Braking Pitch Intensity", Min = 0f, Max = 6f, Precision = 1)]
        public float brakingDivePower = 3.0f;

        [Draw("Acceleration Pitch Intensity", Min = 0f, Max = 6f, Precision = 1)]
        public float accelerationPullPower = 3.0f;

        [Draw("Stop Recoil Intensity", Min = 0f, Max = 6f, Precision = 1)]
        public float stopRecoilPower = 3.0f;

        [Draw("Track Elevation Bumps", Min = 0f, Max = 6f, Precision = 1)]
        public float trackGradeBumpPower = 1.0f;

        [Header("Curves & G-Forces")]
        [Draw("Curve Body Roll", Min = 0f, Max = 6f, Precision = 1)]
        public float curveRollPower = 3.0f;

        [Draw("Curve Look-Ahead", Min = 0f, Max = 6f, Precision = 1)]
        public float curveLookAheadPower = 3.0f;

        [Draw("Lateral G-Force Shift", Min = 0f, Max = 6f, Precision = 1)]
        public float lateralGPower = 3.0f;

        [Draw("Turnout / Switch Impulse", Min = 0f, Max = 6f, Precision = 1)]
        public float turnoutImpulsePower = 3.0f;

        [Draw("Curve Wheel Friction Jitter", Min = 0f, Max = 6f, Precision = 1)]
        public float curveFrictionPower = 3.0f;

        [Header("Vibrations & Head Physics")]
        [Draw("Engine Vibration Intensity", Min = 0f, Max = 6f, Precision = 1)]
        public float engineVibePower = 3.0f;

        [Draw("Track Sway / Hunting Motion", Min = 0f, Max = 6f, Precision = 1)]
        public float trackSwayPower = 3.0f;

        [Draw("High-Speed Jitter", Min = 0f, Max = 6f, Precision = 1)]
        public float highSpeedJitterPower = 3.0f;

        [Draw("Idle Breathing Motion", Min = 0f, Max = 6f, Precision = 1)]
        public float idleBreathingPower = 3.0f;

        [Draw("Head Inertia / Weight", Min = 0f, Max = 1f, Precision = 2)]
        public float headWeight = 0.5f;

        [Header("Misc & Controls")]
        [Draw("Coupler Slack Impulse", Min = 0f, Max = 6f, Precision = 1)]
        public float couplerSlackPower = 3.0f;

        [Draw("Impact Sensitivity", Min = 0f, Max = 6f, Precision = 1)]
        public float impactSensitivity = 3.0f;

        [Draw("Derailment Effects", Min = 0f, Max = 6f, Precision = 1)]
        public float derailmentPower = 3.0f;

        [Draw("Look-Direction Relative Physics", Tooltip = "When enabled, forces apply relative to where the camera is looking.")]
        public bool enableLookDirectionPhysics = true;

        [Draw("Enable Alt / Free-Look Interactions", Tooltip = "When disabled, camera motion pauses whenever the cursor is visible or Alt is held.")]
        public bool enableAltMode = true;

        [Draw("Debug Overlay", Tooltip = "Shows live diagnostic info (car, speed, camera, last error) in the mod window.")]
        public bool showDebugOverlay = false;

        public enum PresetLevel
        {
            Custom = 0,
            VeryLight = 1,
            Light = 2,
            Medium = 3,
            Strong = 4
        }

        // Applies one of the four ready-made presets by scaling the overall
        // effect strength. Individual sliders keep their relative balance;
        // only the global multiplier changes.
        public void ApplyPreset(PresetLevel level)
        {
            switch (level)
            {
                case PresetLevel.VeryLight:
                    globalIntensity = 0.35f;
                    break;
                case PresetLevel.Light:
                    globalIntensity = 0.65f;
                    break;
                case PresetLevel.Medium:
                    globalIntensity = 1.0f;
                    break;
                case PresetLevel.Strong:
                    globalIntensity = 1.6f;
                    break;
                default:
                    return;
            }

            ClampAll();
        }

        // Keeps every slider inside its declared [Draw] range even if the
        // saved XML file was hand-edited or came from an older version.
        public void ClampAll()
        {
            globalIntensity = Mathf.Clamp(globalIntensity, 0.1f, 3f);

            railJointsPower = Mathf.Clamp(railJointsPower, 0f, 6f);
            railJointInterval = Mathf.Clamp(railJointInterval, 2f, 50f);
            brakingDivePower = Mathf.Clamp(brakingDivePower, 0f, 6f);
            accelerationPullPower = Mathf.Clamp(accelerationPullPower, 0f, 6f);
            stopRecoilPower = Mathf.Clamp(stopRecoilPower, 0f, 6f);
            trackGradeBumpPower = Mathf.Clamp(trackGradeBumpPower, 0f, 6f);

            curveRollPower = Mathf.Clamp(curveRollPower, 0f, 6f);
            curveLookAheadPower = Mathf.Clamp(curveLookAheadPower, 0f, 6f);
            lateralGPower = Mathf.Clamp(lateralGPower, 0f, 6f);
            turnoutImpulsePower = Mathf.Clamp(turnoutImpulsePower, 0f, 6f);
            curveFrictionPower = Mathf.Clamp(curveFrictionPower, 0f, 6f);

            engineVibePower = Mathf.Clamp(engineVibePower, 0f, 6f);
            trackSwayPower = Mathf.Clamp(trackSwayPower, 0f, 6f);
            highSpeedJitterPower = Mathf.Clamp(highSpeedJitterPower, 0f, 6f);
            idleBreathingPower = Mathf.Clamp(idleBreathingPower, 0f, 6f);
            headWeight = Mathf.Clamp01(headWeight);

            couplerSlackPower = Mathf.Clamp(couplerSlackPower, 0f, 6f);
            impactSensitivity = Mathf.Clamp(impactSensitivity, 0f, 6f);
            derailmentPower = Mathf.Clamp(derailmentPower, 0f, 6f);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            ClampAll();
            Save(this, modEntry);
        }

        public void OnChange()
        {
            ClampAll();
        }
    }
}