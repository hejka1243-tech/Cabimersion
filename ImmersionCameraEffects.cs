using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CabImmersionPack
{
    public class ImmersionCameraEffects : MonoBehaviour
    {
        public static ImmersionCameraEffects Instance { get; private set; }

        public float maxRollDegrees = 5.0f;
        public float maxImpactShift = 0.20f;
        public float idleVibePower = 0.0006f;
        public float maxRpmVibePower = 0.0018f;
        public float maxFovAdd = 4.0f;
        public float fovSpeedThreshold = 12f;

        private Camera activeCamera = null;

        private TrainCar cachedCar = null;
        private Func<float> cachedEngineGetter = null;
        private Func<float> cachedSlipGetter = null;

        private float lastYaw = 0f;
        private float lastPitch = 0f;
        private float lastYawRate = 0f;
        private float smoothedYawRate = 0f;
        private float lastAccelZ = 0f;
        private float distanceTraveled = 0f;
        private bool wasMoving = false;
        private bool isFirstFrame = true;
        private bool wasDerailed = false;

        private float interactionWeight = 1.0f;

        private float swayPhase = 0f;
        private float breathPhase = 0f;
        private float rpmPhaseLow = 0f;
        private float rpmPhaseHigh = 0f;

        private Vector3 impactPosVelocity;
        private Vector3 impactPosOffset;
        private Vector3 impactRotVelocity;
        private Vector3 impactRotOffset;

        private Vector3 currentPosOffset = Vector3.zero;
        private Quaternion currentRotOffset = Quaternion.identity;

        private float defaultFov = 60f;
        private bool fovCached = false;

        // ------------------------------------------------------------
        // Physics-rate sampling (FixedUpdate). Speed and acceleration
        // are sampled at the physics tick instead of the render tick,
        // so results no longer depend on framerate vs. physics-rate
        // mismatches (this was causing thresholds to fire erratically
        // or never fire at all at high FPS).
        // ------------------------------------------------------------
        private TrainCar physicsCar = null;
        private Rigidbody physicsRb = null;
        private Vector3 lastFixedLocalVelocity = Vector3.zero;
        private bool physicsFirstSample = true;

        private float speedAbsCached = 0f;
        private float speedZCached = 0f;
        private float smoothedAccelZ = 0f;
        private float rawAccelZCached = 0f;
        private float jerkZCached = 0f;

        // ------------------------------------------------------------
        // Camera offset application (Camera.onPreCull / onPostRender).
        // The offset is pushed onto the camera right before it renders
        // and pulled back off right after, instead of being written
        // permanently in LateUpdate. That way any other system driving
        // the camera transform (headset pose, other camera scripts)
        // is never fighting with a stale offset from a previous frame.
        // ------------------------------------------------------------
        private Vector3 savedLocalPos;
        private Quaternion savedLocalRot;
        private bool offsetApplied = false;

        // Debug / diagnostics
        private string lastError = "";
        private bool errorLoggedOnce = false;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Camera.onPreCull += HandlePreCull;
            Camera.onPostRender += HandlePostRender;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandlePreCull;
            Camera.onPostRender -= HandlePostRender;
            RestoreCameraIfNeeded();
        }

        private void OnDestroy()
        {
            RestoreCameraIfNeeded();
        }

        private void HandlePreCull(Camera cam)
        {
            if (cam == null || cam != activeCamera) return;

            var t = cam.transform;
            savedLocalPos = t.localPosition;
            savedLocalRot = t.localRotation;

            t.localPosition = savedLocalPos + currentPosOffset;
            t.localRotation = savedLocalRot * currentRotOffset;
            offsetApplied = true;
        }

        private void HandlePostRender(Camera cam)
        {
            if (!offsetApplied || cam == null || cam != activeCamera) return;

            cam.transform.localPosition = savedLocalPos;
            cam.transform.localRotation = savedLocalRot;
            offsetApplied = false;
        }

        private void RestoreCameraIfNeeded()
        {
            if (offsetApplied && activeCamera != null)
            {
                activeCamera.transform.localPosition = savedLocalPos;
                activeCamera.transform.localRotation = savedLocalRot;
            }
            offsetApplied = false;
        }

        private void LateUpdate()
        {
            Camera mainCamera = Camera.main;
            TrainCar currentCar = PlayerManager.Car;

            if (activeCamera != mainCamera)
            {
                RestoreCameraIfNeeded();
                activeCamera = mainCamera;
                fovCached = false;
            }

            if (!Main.Enabled || currentCar == null || !currentCar.IsLoco)
            {
                ResetState();
                cachedCar = null;
                physicsCar = null;
                physicsRb = null;
                return;
            }

            if (activeCamera == null) return;

            if (cachedCar != currentCar)
            {
                CacheCarComponents(currentCar);
            }

            Rigidbody locoRb = currentCar.GetComponent<Rigidbody>();
            if (locoRb == null) return;

            // Feed FixedUpdate the current car/rigidbody to sample from.
            physicsCar = currentCar;
            physicsRb = locoRb;

            float engineNorm = GetEnginePowerCached(currentCar);
            float wheelSlip = GetWheelSlipCached();

            try
            {
                ProcessCameraEffects(activeCamera, currentCar, wheelSlip, engineNorm);
            }
            catch (Exception ex)
            {
                lastError = ex.ToString();
                if (!errorLoggedOnce)
                {
                    errorLoggedOnce = true;
                    Main.Logger.Error("ProcessCameraEffects threw an exception, effects disabled until next car change:\n" + lastError);
                }
                ResetState();
            }
        }

        private void FixedUpdate()
        {
            if (physicsRb == null || physicsCar == null)
            {
                physicsFirstSample = true;
                return;
            }

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            Vector3 localVel = physicsCar.transform.InverseTransformDirection(physicsRb.velocity);

            if (physicsFirstSample)
            {
                lastFixedLocalVelocity = localVel;
                physicsFirstSample = false;
                return;
            }

            Vector3 deltaVel = localVel - lastFixedLocalVelocity;
            lastFixedLocalVelocity = localVel;

            Vector3 rawAccel = Vector3.ClampMagnitude(deltaVel / dt, 15f);
            rawAccelZCached = rawAccel.z;

            float smoothing = 1f - Mathf.Exp(-3.5f * dt);
            float previousSmoothed = smoothedAccelZ;
            smoothedAccelZ = Mathf.Lerp(smoothedAccelZ, rawAccel.z, smoothing);

            jerkZCached = (smoothedAccelZ - previousSmoothed) / dt;

            speedAbsCached = Mathf.Abs(localVel.z);
            speedZCached = localVel.z;
        }

        private void CacheCarComponents(TrainCar car)
        {
            cachedCar = car;
            cachedEngineGetter = null;
            cachedSlipGetter = null;

            if (car == null) return;

            Component[] components = car.GetComponentsInChildren<Component>();
            string[] fieldsToSearch = { "engineRPM", "engineRpm", "EngineRPM", "throttle", "throttleAmount", "targetEngineRPM" };

            foreach (Component comp in components)
            {
                if (comp == null) continue;
                Type type = comp.GetType();

                if (cachedEngineGetter == null)
                {
                    foreach (string name in fieldsToSearch)
                    {
                        FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        if (f != null && f.FieldType == typeof(float))
                        {
                            cachedEngineGetter = CreateFieldGetter(comp, f);
                            break;
                        }

                        PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        if (p != null && p.PropertyType == typeof(float))
                        {
                            cachedEngineGetter = CreatePropertyGetter(comp, p);
                            break;
                        }
                    }
                }

                if (cachedSlipGetter == null)
                {
                    FieldInfo f = type.GetField("wheelSlip", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(float))
                    {
                        cachedSlipGetter = CreateFieldGetter(comp, f);
                    }
                }
            }
        }

        private static Func<float> CreateFieldGetter(Component target, FieldInfo field)
        {
            try
            {
                var targetExp = Expression.Constant(target);
                var fieldExp = Expression.Field(targetExp, field);
                return Expression.Lambda<Func<float>>(fieldExp).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Func<float> CreatePropertyGetter(Component target, PropertyInfo prop)
        {
            try
            {
                var targetExp = Expression.Constant(target);
                var propExp = Expression.Property(targetExp, prop);
                return Expression.Lambda<Func<float>>(propExp).Compile();
            }
            catch
            {
                return null;
            }
        }

        private float GetEnginePowerCached(TrainCar car)
        {
            if (cachedEngineGetter != null)
            {
                return Mathf.Clamp01(cachedEngineGetter());
            }

            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null) return Mathf.Clamp01(rb.velocity.magnitude / 20f);
            return 0.2f;
        }

        private float GetWheelSlipCached()
        {
            if (cachedSlipGetter != null)
            {
                return Mathf.Clamp01(cachedSlipGetter());
            }
            return 0f;
        }

        // Zeroes all internal state. No longer touches the camera transform
        // directly — HandlePostRender / RestoreCameraIfNeeded own that.
        private void ResetState()
        {
            currentPosOffset = Vector3.zero;
            currentRotOffset = Quaternion.identity;

            impactPosOffset = Vector3.zero;
            impactPosVelocity = Vector3.zero;
            impactRotOffset = Vector3.zero;
            impactRotVelocity = Vector3.zero;

            lastYaw = 0f;
            lastPitch = 0f;
            lastYawRate = 0f;
            smoothedYawRate = 0f;
            smoothedAccelZ = 0f;
            lastAccelZ = 0f;
            rawAccelZCached = 0f;
            jerkZCached = 0f;
            speedAbsCached = 0f;
            speedZCached = 0f;
            distanceTraveled = 0f;
            wasMoving = false;
            isFirstFrame = true;
            wasDerailed = false;
            fovCached = false;
            interactionWeight = 1.0f;
            physicsFirstSample = true;
        }

        private void ProcessCameraEffects(Camera targetCamera, TrainCar car, float wheelSlip, float engineNorm)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || dt > 0.2f) return;

            var settings = Main.Settings;
            float globalIntensity = Mathf.Clamp(settings.globalIntensity, 0.1f, 3f);

            bool isAltPressed = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
            bool isRmbPressed = UnityEngine.Input.GetMouseButton(1);
            bool isCursorVisible = Cursor.visible || Cursor.lockState != CursorLockMode.Locked;

            bool isDraggingCamera = isAltPressed && isRmbPressed;

            float targetWeight = 1.0f;
            if (isDraggingCamera)
            {
                targetWeight = 0.0f;
            }
            else if (!settings.enableAltMode && (isAltPressed || isCursorVisible))
            {
                targetWeight = 0.0f;
            }

            interactionWeight = Mathf.Lerp(interactionWeight, targetWeight, 1f - Mathf.Exp(-10.0f * dt));

            bool isZooming = !isAltPressed && isRmbPressed;

            if (!isZooming && !fovCached)
            {
                defaultFov = targetCamera.fieldOfView;
                fovCached = true;
            }

            float speedZ = speedZCached;
            float speedAbs = speedAbsCached;

            float currentYaw = car.transform.eulerAngles.y;
            float currentPitch = car.transform.eulerAngles.x;

            float deltaYaw = Mathf.DeltaAngle(lastYaw, currentYaw);
            float deltaPitch = Mathf.DeltaAngle(lastPitch, currentPitch);

            if (isFirstFrame || Mathf.Abs(deltaYaw) > 20.0f || Mathf.Abs(deltaPitch) > 20.0f)
            {
                lastYaw = currentYaw;
                lastPitch = currentPitch;
                lastYawRate = 0f;
                smoothedYawRate = 0f;
                isFirstFrame = false;
                impactPosVelocity = Vector3.zero;
                impactRotVelocity = Vector3.zero;
                return;
            }

            // ==========================================
            // DERAILMENT DETECTION & SHOCK
            // ==========================================
            bool isDerailed = car.derailed;

            if (isDerailed && !wasDerailed)
            {
                float dPower = settings.derailmentPower;
                if (dPower > 0.01f)
                {
                    impactPosVelocity += new Vector3(
                        (UnityEngine.Random.value - 0.5f) * 0.30f,
                        -0.20f - (UnityEngine.Random.value * 0.15f),
                        (UnityEngine.Random.value - 0.5f) * 0.30f
                    ) * dPower;

                    impactRotVelocity += new Vector3(
                        (UnityEngine.Random.value - 0.5f) * 100f,
                        (UnityEngine.Random.value - 0.5f) * 70f,
                        (UnityEngine.Random.value - 0.5f) * 130f
                    ) * dPower;
                }
            }
            wasDerailed = isDerailed;

            // ==========================================
            // 1. BRAKING AND ACCELERATION
            // ==========================================
            float brakePitch = 0f;
            float brakeShiftZ = 0f;

            if (smoothedAccelZ < -0.15f)
            {
                float brakeFactor = Mathf.Clamp(-smoothedAccelZ, 0f, 4.0f);
                brakePitch = brakeFactor * 1.1f * settings.brakingDivePower;
                brakeShiftZ = brakeFactor * 0.012f * settings.brakingDivePower;
            }
            else if (smoothedAccelZ > 0.15f)
            {
                float accelFactor = Mathf.Clamp(smoothedAccelZ, 0f, 3.0f);
                brakePitch = -accelFactor * 0.6f * settings.accelerationPullPower;
                brakeShiftZ = -accelFactor * 0.008f * settings.accelerationPullPower;
            }

            if (wasMoving && speedAbs < 0.15f)
            {
                float recoil = settings.stopRecoilPower * 0.08f;
                impactPosVelocity.z -= recoil;
                impactRotVelocity.x += recoil * 25f;
                wasMoving = false;
            }
            else if (speedAbs > 0.8f)
            {
                wasMoving = true;
            }

            // ==========================================
            // 2. RAIL JOINTS AND PROFILE BUMPS
            // ==========================================
            if (speedAbs > 1.5f && !isDerailed)
            {
                distanceTraveled += speedAbs * dt;

                float jointInterval = Mathf.Max(1.0f, settings.railJointInterval);

                if (distanceTraveled >= jointInterval)
                {
                    distanceTraveled -= jointInterval;
                    float jointForce = Mathf.Clamp(speedAbs / 22.0f, 0.3f, 1.1f) * settings.railJointsPower;
                    impactPosVelocity.y -= jointForce * 0.025f;
                    impactRotVelocity.x += jointForce * 12f;
                }
            }

            float pitchRate = Mathf.Clamp(deltaPitch / dt, -120f, 120f);
            lastPitch = currentPitch;

            if (Mathf.Abs(pitchRate) > 1.2f && speedAbs > 3.0f && !isDerailed)
            {
                float bumpImpulse = Mathf.Clamp(pitchRate * 0.008f * settings.trackGradeBumpPower, -0.15f, 0.15f);
                impactPosVelocity.y += bumpImpulse * 0.02f;
                impactRotVelocity.x -= bumpImpulse * 14f;
            }

            // ==========================================
            // 3. CURVES AND FRICTION
            // ==========================================
            float rawYawRate = Mathf.Clamp(deltaYaw / dt, -150f, 150f);
            float yawAccel = Mathf.Clamp((rawYawRate - lastYawRate) / dt, -300f, 300f);

            lastYaw = currentYaw;
            lastYawRate = rawYawRate;
            smoothedYawRate = Mathf.Lerp(smoothedYawRate, rawYawRate, 1f - Mathf.Exp(-4.0f * dt));

            float lookAheadYaw = Mathf.Clamp(smoothedYawRate * 0.10f * settings.curveLookAheadPower, -5.0f, 5.0f);
            float lateralGShift = Mathf.Clamp(-smoothedYawRate * 0.004f * settings.lateralGPower, -0.10f, 0.10f);
            float continuousRoll = -smoothedYawRate * 0.09f * settings.curveRollPower;
            float continuousShift = -smoothedYawRate * 0.003f * settings.curveRollPower;

            float curveFrictionJitter = 0f;
            if (Mathf.Abs(smoothedYawRate) > 2.2f && speedAbs > 4.0f && !isDerailed)
            {
                float curveIntensity = Mathf.Abs(smoothedYawRate) * (speedAbs / 18f) * settings.curveFrictionPower;
                float noise = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f;
                curveFrictionJitter = noise * 0.002f * Mathf.Clamp01(curveIntensity);
            }

            if (Mathf.Abs(yawAccel) > 10.0f && speedAbs > 2.0f && !isDerailed)
            {
                float impulse = Mathf.Clamp(yawAccel * 0.0020f * settings.turnoutImpulsePower, -0.2f, 0.2f);
                impactRotVelocity.z -= impulse * 18f;
                impactPosVelocity.x += impulse * 0.025f;
            }

            // ==========================================
            // 4. COUPLER SLACK AND IMPACTS
            // ==========================================
            if (Mathf.Abs(rawAccelZCached) > 3.5f)
            {
                float impactForce = Mathf.Clamp(rawAccelZCached * 0.006f * settings.impactSensitivity, -0.4f, 0.4f);
                impactPosVelocity.z -= impactForce * 0.25f;
                impactRotVelocity.x -= impactForce * 40f;
            }

            if (Mathf.Abs(jerkZCached) > 15.0f && speedAbs > 0.5f)
            {
                float slackImpulse = Mathf.Clamp(jerkZCached * 0.0004f * settings.couplerSlackPower, -0.15f, 0.15f);
                impactPosVelocity.z += slackImpulse * 0.20f;
                impactRotVelocity.x += slackImpulse * 18f;
            }

            impactPosVelocity += -24f * impactPosOffset * dt;
            impactPosVelocity *= Mathf.Pow(0.03f, dt);
            impactPosOffset += impactPosVelocity * dt;

            impactRotVelocity += -24f * impactRotOffset * dt;
            impactRotVelocity *= Mathf.Pow(0.03f, dt);
            impactRotOffset += impactRotVelocity * dt;

            // ==========================================
            // 5. VIBRATIONS, HUNTING MOTION & DERAIL RUMBLE
            // ==========================================
            float swayX = 0f, swayRoll = 0f;
            if (speedAbs > 1.2f && !isDerailed)
            {
                swayPhase += dt * Mathf.Clamp(speedAbs * 0.55f, 1.2f, 9.0f);
                float swayFactor = Mathf.InverseLerp(1.2f, 25f, speedAbs) * settings.trackSwayPower;
                swayX = Mathf.Sin(swayPhase) * 0.002f * swayFactor;
                swayRoll = Mathf.Cos(swayPhase * 0.5f) * 0.30f * swayFactor;
            }

            float freqLow = 7f + (engineNorm * 10f);
            float freqHigh = 20f + (engineNorm * 25f);

            rpmPhaseLow += dt * freqLow;
            rpmPhaseHigh += dt * freqHigh;

            float currentVibAmp = Mathf.Lerp(idleVibePower, maxRpmVibePower, engineNorm) * settings.engineVibePower;
            float vibY = (Mathf.Sin(rpmPhaseLow) * 0.5f + Mathf.Cos(rpmPhaseHigh) * 0.3f) * currentVibAmp;
            float vibX = (Mathf.Cos(rpmPhaseLow * 0.7f) * 0.4f) * currentVibAmp;

            if (wheelSlip > 0.05f && !isDerailed)
            {
                float slipNoiseX = (Mathf.PerlinNoise(Time.time * 35f, 10f) - 0.5f) * 2f;
                float slipNoiseY = (Mathf.PerlinNoise(10f, Time.time * 35f) - 0.5f) * 2f;
                vibY += slipNoiseY * 0.0025f * wheelSlip * settings.engineVibePower;
                vibX += slipNoiseX * 0.0025f * wheelSlip * settings.engineVibePower;
            }

            if (isDerailed && settings.derailmentPower > 0.01f && speedAbs > 0.3f)
            {
                float groundRumble = Mathf.Clamp(speedAbs / 8f, 0.2f, 2.2f) * settings.derailmentPower;
                float t = Time.time * 30f;
                vibX += (Mathf.PerlinNoise(t, 0f) - 0.5f) * 0.020f * groundRumble;
                vibY += (Mathf.PerlinNoise(0f, t) - 0.5f) * 0.030f * groundRumble;
                swayRoll += (Mathf.PerlinNoise(t, t) - 0.5f) * 7.0f * groundRumble;
            }

            float hsJitterX = 0f, hsJitterY = 0f, hsJitterRoll = 0f;
            if (speedAbs > 14f && !isDerailed)
            {
                float hsFactor = Mathf.InverseLerp(14f, 35f, speedAbs) * settings.highSpeedJitterPower;
                float timeTicks = Time.time * 24f;

                hsJitterX = (Mathf.PerlinNoise(timeTicks, 0f) - 0.5f) * 0.0014f * hsFactor;
                hsJitterY = (Mathf.PerlinNoise(0f, timeTicks) - 0.5f) * 0.0014f * hsFactor;
                hsJitterRoll = (Mathf.PerlinNoise(timeTicks, timeTicks) - 0.5f) * 0.20f * hsFactor;
            }

            // ==========================================
            // 6. IDLE BREATHING MOTION
            // ==========================================
            float breathY = 0f, breathPitch = 0f;
            if (speedAbs < 1.0f)
            {
                breathPhase += dt * 1.2f;
                breathY = Mathf.Sin(breathPhase) * 0.001f * settings.idleBreathingPower;
                breathPitch = Mathf.Cos(breathPhase) * 0.06f * settings.idleBreathingPower;
            }

            // ==========================================
            // 7. APPLY HEAD WEIGHT & LOOK-DIRECTION RELATIVE PHYSICS
            // ==========================================
            float totalRoll = Mathf.Clamp(continuousRoll + swayRoll + hsJitterRoll + impactRotOffset.z, -maxRollDegrees * 2.5f, maxRollDegrees * 2.5f);
            float currentFovRatio = Mathf.Clamp01(targetCamera.fieldOfView / Mathf.Max(10f, defaultFov));

            // The camera transform is clean here (no leftover offset): any
            // offset from the previous frame was already removed by
            // HandlePostRender right after that frame finished rendering.
            Quaternion headRotInCar = Quaternion.Inverse(car.transform.rotation) * targetCamera.transform.rotation;

            // Global intensity is applied to the raw angle/position inputs
            // (not as a Slerp/Lerp blend factor), so it can scale rotation
            // beyond 1x instead of being capped by the blend's 0-1 range.
            Quaternion carRotQuat = Quaternion.Euler(
                (brakePitch + impactRotOffset.x + breathPitch) * globalIntensity,
                lookAheadYaw * globalIntensity,
                totalRoll * globalIntensity
            );

            Vector3 carPosVector = new Vector3(
                continuousShift + lateralGShift + swayX + vibX + hsJitterX + curveFrictionJitter + impactPosOffset.x,
                vibY + breathY + hsJitterY + impactPosOffset.y,
                Mathf.Clamp(brakeShiftZ + impactPosOffset.z, -maxImpactShift, maxImpactShift)
            ) * globalIntensity;

            Quaternion finalRotQuat;
            Vector3 finalPosVector;

            if (settings.enableLookDirectionPhysics)
            {
                finalRotQuat = Quaternion.Inverse(headRotInCar) * carRotQuat * headRotInCar;
                finalPosVector = Quaternion.Inverse(headRotInCar) * carPosVector;
            }
            else
            {
                finalRotQuat = carRotQuat;
                finalPosVector = carPosVector;
            }

            float blend = Mathf.Clamp01(interactionWeight * currentFovRatio);
            Vector3 targetPos = finalPosVector * blend;
            Quaternion targetRot = Quaternion.Slerp(Quaternion.identity, finalRotQuat, blend);

            // headWeight now uses its full 0-1 range (previously only the
            // 0.5-1.0 half of the slider had any effect).
            float headSmoothSpeed = Mathf.Lerp(12.0f, 5.0f, Mathf.Clamp01(settings.headWeight));

            float posSmoothing = 1f - Mathf.Exp(-headSmoothSpeed * dt);
            currentPosOffset = Vector3.Lerp(currentPosOffset, targetPos, posSmoothing);
            currentRotOffset = Quaternion.Slerp(currentRotOffset, targetRot, posSmoothing);

            // NOTE: the offset is no longer written to the camera transform
            // here. HandlePreCull applies currentPosOffset/currentRotOffset
            // right before the camera renders, and HandlePostRender removes
            // it right after — see the two methods above.

            // ==========================================
            // 8. DYNAMIC FIELD OF VIEW (FOV)
            // ==========================================
            if (!isZooming)
            {
                float extraFovFromBraking = Mathf.Clamp01(-smoothedAccelZ * 0.15f) * 1.5f;
                if (speedAbs > fovSpeedThreshold)
                {
                    float speedFactor = Mathf.InverseLerp(fovSpeedThreshold, 35f, speedAbs);
                    targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, defaultFov + (speedFactor * maxFovAdd) - extraFovFromBraking, dt * 1.8f);
                }
                else
                {
                    targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, defaultFov - extraFovFromBraking, dt * 1.8f);
                }
            }
        }

        // Shown in the UMM window when "Debug Overlay" is enabled in Settings.
        public string GetDebugInfo()
        {
            var sb = new StringBuilder();
            TrainCar car = PlayerManager.Car;

            sb.AppendLine($"Enabled: {Main.Enabled}");
            sb.AppendLine($"Car: {(car != null ? car.name : "none")}   IsLoco: {(car != null && car.IsLoco)}");
            sb.AppendLine($"Rigidbody found: {(car != null && car.GetComponent<Rigidbody>() != null)}");
            sb.AppendLine($"Active camera: {(activeCamera != null ? activeCamera.name : "none")}");
            sb.AppendLine($"Speed abs: {speedAbsCached:0.00} m/s   Accel Z (smoothed): {smoothedAccelZ:0.00}");
            sb.AppendLine($"Offset applied to camera: {offsetApplied}");
            if (!string.IsNullOrEmpty(lastError))
            {
                sb.AppendLine("Last error:");
                sb.AppendLine(lastError);
            }
            return sb.ToString();
        }
    }
}