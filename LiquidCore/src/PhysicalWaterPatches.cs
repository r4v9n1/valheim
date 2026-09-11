using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PhysicalWater
{
    [HarmonyPatch(typeof(Floating), nameof(Floating.GetLiquidLevel))]
    internal static class FloatingGetLiquidLevelPatch
    {
        private static bool Prefix(Vector3 p, float waveFactor, LiquidType type, ref float __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() || !IsWaterQuery(type))
                {
                    return true;
                }

                // While PhysicalWater is enabled, a missing system is DRY, never permission
                // to run Valheim's water query. OverrideWaterQueries is legacy-only.
                if (system == null)
                {
                    __result = -10000f;
                    return false;
                }

                if (PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    __result = -10000f;
                    return false;
                }

                if (!IsAuthoritativeReplacementMode())
                {
                    return true;
                }

                __result = system.GetCharacterSurfaceHeight(p);
                return false;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.GetLiquidLevel authoritative hook failed closed: " + ex.Message);
                __result = -10000f;
                return false;
            }
        }

        private static void Postfix(Vector3 p, float waveFactor, LiquidType type, ref float __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() ||
                    system == null ||
                    !PhysicalWaterPlugin.Settings.OverrideWaterQueries.Value ||
                    !IsWaterQuery(type) ||
                    PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    return;
                }

                float physicalSurface = system.GetCharacterSurfaceHeight(p);
                if (PhysicalWaterPlugin.Settings.SuppressVanillaWaterVolumeFloaters.Value ||
                    PhysicalWaterPlugin.Settings.HideVanillaWaterRenderers.Value)
                {
                    __result = physicalSurface;
                }
                else
                {
                    __result = Mathf.Max(__result, physicalSurface);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.GetLiquidLevel hook failed: " + ex.Message);
            }
        }

        private static bool IsWaterQuery(LiquidType type)
        {
            return type == LiquidType.Water || type == LiquidType.All;
        }

        private static bool IsAuthoritativeReplacementMode()
        {
            return PhysicalWaterSystem.IsEnabled();
        }
    }

    internal static class FloatingWaterMethodTargets
    {
        internal static MethodInfo GetWaterLevelMethod()
        {
            return AccessTools.Method(
                typeof(Floating),
                nameof(Floating.GetWaterLevel),
                new[] { typeof(Vector3), typeof(WaterVolume).MakeByRefType() });
        }

        internal static MethodInfo IsUnderWaterMethod()
        {
            return AccessTools.Method(
                typeof(Floating),
                nameof(Floating.IsUnderWater),
                new[] { typeof(Vector3), typeof(WaterVolume).MakeByRefType() });
        }
    }

    internal static class FloatingGetWaterLevelPatch
    {
        private static MethodBase TargetMethod()
        {
            return FloatingWaterMethodTargets.GetWaterLevelMethod();
        }

        private static bool Prefix(Vector3 p, ref WaterVolume previousAndOut, ref float __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled())
                {
                    return true;
                }
                if (system == null)
                {
                    previousAndOut = null;
                    __result = -10000f;
                    return false;
                }

                if (PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    previousAndOut = null;
                    __result = -10000f;
                    return false;
                }

                if (!IsAuthoritativeReplacementMode())
                {
                    return true;
                }

                previousAndOut = null;
                __result = system.GetCharacterSurfaceHeight(p);
                return false;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.GetWaterLevel authoritative hook failed closed: " + ex.Message);
                previousAndOut = null;
                __result = -10000f;
                return false;
            }
        }

        private static void Postfix(Vector3 p, ref WaterVolume previousAndOut, ref float __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() ||
                    system == null ||
                    !PhysicalWaterPlugin.Settings.OverrideWaterQueries.Value ||
                    PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    return;
                }

                previousAndOut = null;
                __result = system.GetCharacterSurfaceHeight(p);
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.GetWaterLevel hook failed: " + ex.Message);
            }
        }

        private static bool IsAuthoritativeReplacementMode()
        {
            return PhysicalWaterSystem.IsEnabled();
        }
    }

    internal static class FloatingIsUnderWaterPatch
    {
        private static MethodBase TargetMethod()
        {
            return FloatingWaterMethodTargets.IsUnderWaterMethod();
        }

        private static bool Prefix(Vector3 p, ref WaterVolume previousAndOut, ref bool __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled())
                {
                    return true;
                }
                if (system == null)
                {
                    previousAndOut = null;
                    __result = false;
                    return false;
                }

                if (PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    previousAndOut = null;
                    __result = false;
                    return false;
                }

                if (!IsAuthoritativeReplacementMode())
                {
                    return true;
                }

                previousAndOut = null;
                __result = system.GetCharacterSurfaceHeight(p) > p.y;
                return false;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.IsUnderWater authoritative hook failed closed: " + ex.Message);
                previousAndOut = null;
                __result = false;
                return false;
            }
        }

        private static void Postfix(Vector3 p, ref WaterVolume previousAndOut, ref bool __result)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() ||
                    system == null ||
                    !PhysicalWaterPlugin.Settings.OverrideWaterQueries.Value ||
                    PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    return;
                }

                previousAndOut = null;
                __result = system.GetCharacterSurfaceHeight(p) > p.y;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Floating.IsUnderWater hook failed: " + ex.Message);
            }
        }

        private static bool IsAuthoritativeReplacementMode()
        {
            return PhysicalWaterSystem.IsEnabled();
        }
    }

    [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
    internal static class FloatingCustomFixedUpdatePatch
    {
        private static bool Prefix(Floating __instance)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() || __instance == null)
                {
                    return true;
                }
                if (system == null)
                {
                    // Enabled replacement with no system must fail closed, never run vanilla buoyancy.
                    return false;
                }

                // Ships have their own multipoint solver below. Never let Floating stack
                // another buoyancy solver on the same rigidbody.
                if (__instance.GetComponent<Ship>() != null)
                {
                    return false;
                }

                if (PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    __instance.SetLiquidLevel(-10000f, LiquidType.Water, system);
                    return false;
                }

                Rigidbody body = __instance.GetComponent<Rigidbody>();
                if (body == null)
                {
                    return false;
                }

                Vector3 position = body.worldCenterOfMass;
                Collider collider = __instance.GetComponent<Collider>();
                Vector3 probe = collider != null
                    ? new Vector3(collider.bounds.center.x, collider.bounds.min.y + Mathf.Min(0.28f, collider.bounds.extents.y * 0.45f), collider.bounds.center.z)
                    : position;

                float surface = system.GetFloatingProbeSurfaceHeight(probe, 1f, PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value);
                __instance.SetLiquidLevel(surface, LiquidType.Water, system);
                if (surface <= -9990f)
                {
                    return false;
                }

                float depth = surface - probe.y;
                if (depth <= 0f)
                {
                    return false;
                }

                float probeDepth = collider != null ? Mathf.Max(0.35f, collider.bounds.size.y * 0.65f) : 0.8f;
                float submersion = Mathf.Clamp01(depth / probeDepth);
                Vector3 waterVelocity = system.GetWaterVelocity(probe);
                Vector3 relative = body.GetPointVelocity(probe) - waterVelocity;
                Vector3 force = Vector3.up * (-Physics.gravity.y * 1.08f * submersion);
                force += -Vector3.ClampMagnitude(relative, 7f) * (0.42f * submersion);
                body.AddForceAtPosition(force, probe, ForceMode.Acceleration);
                body.AddTorque(-body.angularVelocity * (0.12f * submersion), ForceMode.Acceleration);

                if (PhysicalWaterPlugin.Settings.ReactToFloatingObjects.Value && relative.sqrMagnitude > 0.35f)
                {
                    float strength = Mathf.Clamp(relative.magnitude * 0.0025f * PhysicalWaterPlugin.Settings.ObjectDisturbanceScale.Value, 0.002f, 0.014f);
                    system.Disturb(probe, strength, Mathf.Max(0.55f, PhysicalWaterPlugin.Settings.BuoyancyProbeRadius.Value));
                }

                // PhysicalWater has completely replaced Floating's vanilla water physics.
                return false;
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater authoritative Floating solver failed: " + ex.Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class ShipCustomFixedUpdatePatch
    {
        private static float s_nextShipDiagnosticTime;

        private static bool Prefix(Ship __instance)
        {
            if (!PhysicalWaterSystem.IsEnabled() || __instance == null)
            {
                return true;
            }

            // 0.5.1: PhysicalWater exclusively owns water forces/propulsion while active.
            // Returning false prevents Valheim Ship.CustomFixedUpdate from stacking its
            // WaterVolume/Floating forces with our multipoint solver.
            return false;
        }

        private static void Postfix(Ship __instance)
        {
            try
            {
                ApplyPhysicalWaterShipBuoyancy(__instance);
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater ship buoyancy assist failed: " + ex.Message);
            }
        }

        private static void ApplyPhysicalWaterShipBuoyancy(Ship ship)
        {
            PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
            if (!PhysicalWaterSystem.IsEnabled() || system == null || ship == null ||
                PhysicalWaterSystem.ShouldForceDryWaterQueries())
            {
                return;
            }

            ZNetView view = ship.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner())
            {
                return;
            }

            Rigidbody body = ship.GetComponent<Rigidbody>();
            BoxCollider floatCollider = ship.m_floatCollider;
            if (body == null || floatCollider == null)
            {
                return;
            }

            Transform ft = floatCollider.transform;
            Vector3 size = floatCollider.size;
            Vector3 center = floatCollider.center;
            float halfX = Mathf.Max(0.30f, size.x * 0.42f);
            float halfZ = Mathf.Max(0.45f, size.z * 0.45f);
            float probeY = center.y - size.y * 0.38f;

            // 0.5.1 hydrostatic grid. A 3x3 hull footprint gives bow/stern and
            // port/starboard differential buoyancy without letting any single probe
            // become a catapult. Real righting comes from differential submersion.
            Vector3[] localProbes =
            {
                new Vector3(center.x - halfX, probeY, center.z + halfZ),
                new Vector3(center.x,         probeY, center.z + halfZ),
                new Vector3(center.x + halfX, probeY, center.z + halfZ),
                new Vector3(center.x - halfX, probeY, center.z),
                new Vector3(center.x,         probeY, center.z),
                new Vector3(center.x + halfX, probeY, center.z),
                new Vector3(center.x - halfX, probeY, center.z - halfZ),
                new Vector3(center.x,         probeY, center.z - halfZ),
                new Vector3(center.x + halfX, probeY, center.z - halfZ)
            };

            float probeDepth = Mathf.Max(0.85f, Mathf.Abs(size.y) * 1.05f);
            const float totalBuoyancyGravity = 1.24f;
            const float linearWaterDrag = 0.62f;
            int wetProbes = 0;
            float wetFraction = 0f;
            Vector3 weightedSurfaceNormal = Vector3.zero;

            for (int i = 0; i < localProbes.Length; i++)
            {
                Vector3 point = ft.TransformPoint(localProbes[i]);
                float surface = system.GetFloatingProbeSurfaceHeight(point, 1f, PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value);
                if (surface <= -9990f)
                {
                    continue;
                }

                float depth = surface - point.y;
                if (depth <= 0f)
                {
                    continue;
                }

                float submersion = Mathf.Clamp01(depth / probeDepth);
                float hydro = submersion * submersion * (3f - 2f * submersion);
                wetProbes++;
                wetFraction += hydro;

                Vector3 normal = system.GetSurfaceNormal(point);
                weightedSurfaceNormal += normal * hydro;
                Vector3 forceDirection = Vector3.Slerp(Vector3.up, normal, 0.055f).normalized;
                Vector3 relativeVelocity = body.GetPointVelocity(point) - system.GetWaterVelocity(point);
                relativeVelocity = Vector3.ClampMagnitude(relativeVelocity, 7f);

                float upwardAcceleration = -Physics.gravity.y * totalBuoyancyGravity * hydro / localProbes.Length;
                Vector3 buoyancy = forceDirection * upwardAcceleration;
                Vector3 drag = -relativeVelocity * (linearWaterDrag * hydro / localProbes.Length);
                body.AddForceAtPosition(buoyancy + drag, point, ForceMode.Acceleration);
            }

            if (wetProbes > 0)
            {
                wetFraction = Mathf.Clamp01(wetFraction / localProbes.Length);
                Vector3 targetUp = weightedSurfaceNormal.sqrMagnitude > 0.001f
                    ? Vector3.Slerp(Vector3.up, weightedSurfaceNormal.normalized, 0.28f).normalized
                    : Vector3.up;

                ApplyOceanRightingMoment(ship, body, targetUp, wetFraction);

                // Water damping is bounded and continuous. No velocity-change boosts,
                // rescue impulses, or centre-height teleports are allowed in 0.5.1.
                Vector3 velocity = body.linearVelocity;
                velocity.y = Mathf.Clamp(velocity.y, -3.8f, 3.8f);
                Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
                if (horizontal.magnitude > 16f)
                {
                    horizontal = horizontal.normalized * 16f;
                }
                body.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, 0.95f);
                body.WakeUp();

                ApplyPhysicalWaterShipPropulsion(system, ship, body, floatCollider, body.worldCenterOfMass);

                float speed = body.linearVelocity.magnitude;
                if (speed > 0.35f)
                {
                    Vector3 stern = ft.TransformPoint(new Vector3(center.x, probeY, center.z - halfZ));
                    system.Disturb(stern, Mathf.Clamp(speed * 0.0022f, 0.0025f, 0.014f), Mathf.Clamp(size.x * 0.58f, 1.0f, 3.0f));
                }
            }

            LogShipDiagnosticIfNeeded(system, ship, body, floatCollider, body.worldCenterOfMass);
        }

        private static void ApplyOceanRightingMoment(Ship ship, Rigidbody body, Vector3 targetUp, float wetFraction)
        {
            if (ship == null || body == null || wetFraction <= 0.001f)
            {
                return;
            }

            Vector3 currentUp = ship.transform.up.normalized;
            float alignment = Mathf.Clamp(Vector3.Dot(currentUp, targetUp), -1f, 1f);
            Vector3 axis = Vector3.Cross(currentUp, targetUp);
            if (axis.sqrMagnitude < 0.0005f && alignment < 0f)
            {
                // At almost exactly 180 degrees Cross(up,targetUp) degenerates.
                // Pick the hull's longitudinal axis so an inverted boat has a deterministic
                // way back instead of bouncing forever on the seabed.
                axis = ship.transform.forward;
            }

            if (axis.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Acos(alignment);
                float rightingGain = Mathf.Lerp(2.6f, 6.5f, wetFraction);
                float invertedBoost = alignment < 0f ? 1.55f : 1f;
                Vector3 torque = axis.normalized * Mathf.Clamp(angle * rightingGain * invertedBoost, 0f, 9.5f);
                body.AddTorque(torque, ForceMode.Acceleration);
            }

            body.AddTorque(-body.angularVelocity * Mathf.Lerp(0.55f, 1.35f, wetFraction), ForceMode.Acceleration);
        }

        private static void PrimeShipFloatingLiquidLevel(Ship ship)
        {
            PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
            if (!PhysicalWaterSystem.IsEnabled() ||
                system == null ||
                ship == null ||
                PhysicalWaterSystem.ShouldForceDryWaterQueries())
            {
                return;
            }

            if (!PhysicalWaterPlugin.Settings.FeedFloatingLiquidLevel.Value)
            {
                return;
            }

            Rigidbody body = ship.GetComponent<Rigidbody>();
            Floating floating = ship.GetComponent<Floating>();
            if (body == null || floating == null)
            {
                return;
            }

            float surface = system.GetFloatingProbeSurfaceHeight(
                body.worldCenterOfMass,
                1f,
                PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value);
            floating.SetLiquidLevel(surface, LiquidType.Water, system);
        }

        private static void ApplyStableShipFloatation(PhysicalWaterSystem system, Ship ship, Rigidbody body, BoxCollider floatCollider, Vector3 center)
        {
            float surface = system.GetAnimatedRideSurfaceHeight(
                center,
                PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value,
                0.32f);
            if (surface <= -9990f)
            {
                return;
            }

            float colliderHeight = Mathf.Max(0.35f, GetFloatColliderWorldHeight(floatCollider));
            float hullBottomY = GetFloatColliderHullBottomY(floatCollider);
            float desiredBottomSubmerge = Mathf.Clamp(colliderHeight * 0.12f, 0.08f, 0.34f);
            float targetBottomY = surface - desiredBottomSubmerge;
            float bottomError = targetBottomY - hullBottomY;
            // Drive buoyancy from the float collider's actual hull-bottom depth.
            // Do not target the rigidbody center above sea level: that caused ships
            // to hover out of reach even though the gameplay water level was correct.
            float selectedError = bottomError;
            float maxLiftDepth = Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.ShipMaxLiftDepth.Value);
            float error = Mathf.Clamp(selectedError, -0.12f, maxLiftDepth);
            Vector3 velocity = body.linearVelocity;
            float correction = error * Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipBuoyancyAcceleration.Value);
            float damping = -velocity.y * Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipVerticalDamping.Value);
            float rescue = bottomError > 1.25f ? Mathf.Min(18.0f, (bottomError - 1.25f) * 7.5f) : 0f;
            float verticalAcceleration = Mathf.Clamp(correction + damping + rescue, 0f, bottomError > 1.25f ? 42.0f : 22.0f);

            if (selectedError > 0.12f)
            {
                float targetUpVelocity = Mathf.Clamp(selectedError * 0.82f, 0.10f, selectedError > 2.5f ? 2.2f : 1.25f);
                float moveRate = (selectedError > 2.5f ? 4.5f : 2.8f) * Time.fixedDeltaTime;
                float newY = velocity.y < targetUpVelocity
                    ? Mathf.MoveTowards(velocity.y, targetUpVelocity, moveRate)
                    : velocity.y;
                body.linearVelocity = new Vector3(velocity.x, newY, velocity.z);
                velocity = body.linearVelocity;
            }

            if (selectedError > 2.4f && velocity.y < 0.55f)
            {
                float boost = Mathf.Clamp((selectedError - 1.8f) * 0.035f, 0.035f, 0.16f);
                body.AddForce(Vector3.up * boost, ForceMode.VelocityChange);
                velocity = body.linearVelocity;
            }
            else if (selectedError < -0.20f && velocity.y > 0.30f)
            {
                body.linearVelocity = new Vector3(velocity.x, 0.30f, velocity.z);
                velocity = body.linearVelocity;
            }

            if (verticalAcceleration > 0.01f)
            {
                body.WakeUp();
                body.AddForce(Vector3.up * verticalAcceleration, ForceMode.Acceleration);
            }

            float drag = Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipWaterDrag.Value);
            if (drag > 0f)
            {
                Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
                if (horizontalVelocity.sqrMagnitude > 0.01f)
                {
                    body.AddForce(-horizontalVelocity * drag, ForceMode.Acceleration);
                }
            }
        }

        private static void ApplyPhysicalWaterShipPropulsion(PhysicalWaterSystem system, Ship ship, Rigidbody body, BoxCollider floatCollider, Vector3 center)
        {
            float surface = system.GetAnimatedRideSurfaceHeight(
                center,
                PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value,
                0.28f);
            if (surface <= -9990f)
            {
                return;
            }

            Transform shipTransform = ship.transform;
            Vector3 forward = shipTransform.forward;
            Vector3 right = shipTransform.right;
            Vector3 velocity = body.linearVelocity;
            float forwardSpeed = Vector3.Dot(velocity, forward);
            float rudder = Mathf.Clamp(ship.GetRudderValue(), -1f, 1f);
            Ship.Speed speedSetting = ship.GetSpeedSetting();
            float fixedDeltaTime = Time.fixedDeltaTime;
            Vector3 steeringPoint = shipTransform.position + forward * ship.m_stearForceOffset;
            bool hasDrive = false;

            if (speedSetting == Ship.Speed.Full || speedSetting == Ship.Speed.Half)
            {
                float sailSize = speedSetting == Ship.Speed.Full ? 1f : 0.55f;
                Vector3 windDir = EnvMan.instance != null ? EnvMan.instance.GetWindDir() : forward;
                float windIntensity = EnvMan.instance != null ? EnvMan.instance.GetWindIntensity() : 0.35f;
                float windDot = Vector3.Dot(windDir, -forward);
                float sideWindBonus = Mathf.Lerp(0.85f, 1.15f, 1f - Mathf.Abs(windDot));
                float headWindPenalty = 1f - LerpStep(0.78f, 0.92f, windDot);
                float windPower = Mathf.Lerp(0.35f, 1.35f, Mathf.Clamp01(windIntensity)) * sideWindBonus * headWindPenalty;
                Vector3 sailDirection = Vector3.Normalize(windDir + forward * 1.35f);
                if (sailDirection.sqrMagnitude < 0.01f)
                {
                    sailDirection = forward;
                }

                Vector3 sailForce = sailDirection * (ship.m_sailForceFactor * 0.92f * sailSize * windPower);
                Vector3 sailPoint = center + shipTransform.up * ship.m_sailForceOffset;
                body.AddForceAtPosition(sailForce, sailPoint, ForceMode.Acceleration);
                hasDrive = sailForce.sqrMagnitude > 0.0001f;
            }
            else if (speedSetting == Ship.Speed.Slow || speedSetting == Ship.Speed.Back)
            {
                float direction = speedSetting == Ship.Speed.Back ? -1f : 1f;
                float paddleScale = Mathf.Lerp(1.0f, 0.55f, Mathf.Abs(rudder));
                Vector3 paddleForce = forward * (direction * ship.m_backwardForce * paddleScale * 0.82f);
                body.AddForceAtPosition(paddleForce, steeringPoint, ForceMode.Acceleration);
                hasDrive = true;
            }

            if (Mathf.Abs(rudder) > 0.02f && (hasDrive || Mathf.Abs(forwardSpeed) > 0.10f))
            {
                float movementSign = Mathf.Sign(Mathf.Abs(forwardSpeed) > 0.08f ? forwardSpeed : (speedSetting == Ship.Speed.Back ? -1f : 1f));
                float steeringPower = ship.m_stearForce * 0.72f + Mathf.Abs(forwardSpeed) * ship.m_stearVelForceFactor * 0.85f;
                Vector3 steeringForce = right * (-rudder * movementSign * steeringPower);
                body.AddForceAtPosition(steeringForce, steeringPoint, ForceMode.Acceleration);
            }

            float wakeStrength = Mathf.Clamp(PhysicalWaterPlugin.Settings.ShipWakeStrength.Value, 0f, 0.45f);
            if (wakeStrength > 0.001f && (hasDrive || body.linearVelocity.sqrMagnitude > 0.05f))
            {
                Transform floatTransform = floatCollider.transform;
                Vector3 size = floatCollider.size;
                float speed = Mathf.Clamp(body.linearVelocity.magnitude + (hasDrive ? 0.65f : 0f), 0.25f, 5.5f);
                Vector3 bow = floatTransform.position + floatTransform.forward * size.z * 0.48f;
                Vector3 stern = floatTransform.position - floatTransform.forward * size.z * 0.52f;
                Vector3 port = stern - floatTransform.right * size.x * 0.42f;
                Vector3 starboard = stern + floatTransform.right * size.x * 0.42f;
                system.EmitWaterContact(bow, wakeStrength * speed * 0.55f, 2.8f);
                system.EmitWaterContact(port, wakeStrength * speed * 0.35f, 2.2f);
                system.EmitWaterContact(starboard, wakeStrength * speed * 0.35f, 2.2f);
            }
        }

        private static float GetFloatColliderHullBottomY(BoxCollider floatCollider)
        {
            if (floatCollider == null)
            {
                return -10000f;
            }

            Vector3 localBottom = floatCollider.center + Vector3.down * floatCollider.size.y * 0.5f;
            return floatCollider.transform.TransformPoint(localBottom).y;
        }

        private static float LerpStep(float l, float h, float v)
        {
            return Mathf.Clamp01((v - l) / Mathf.Max(0.0001f, h - l));
        }

        private static float GetFloatColliderWorldHeight(BoxCollider floatCollider)
        {
            if (floatCollider == null)
            {
                return 0f;
            }

            Vector3 localBottom = floatCollider.center + Vector3.down * floatCollider.size.y * 0.5f;
            Vector3 localTop = floatCollider.center + Vector3.up * floatCollider.size.y * 0.5f;
            return Mathf.Abs(floatCollider.transform.TransformPoint(localTop).y -
                             floatCollider.transform.TransformPoint(localBottom).y);
        }

        private static void ApplyShipUprightStabilizer(Ship ship, Rigidbody body)
        {
            if (!PhysicalWaterPlugin.Settings.ShipUprightAssist.Value ||
                ship == null ||
                body == null)
            {
                return;
            }

            Vector3 currentUp = ship.transform.up;
            Vector3 correctionAxis = Vector3.Cross(currentUp, Vector3.up);
            float tilt = correctionAxis.magnitude;
            float torque = Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipUprightTorque.Value);
            if (tilt > 0.001f && torque > 0f)
            {
                float tiltBoost = Mathf.Lerp(0.45f, 1.45f, Mathf.Clamp01(tilt));
                Vector3 torqueVector = correctionAxis.normalized * Mathf.Clamp(tilt, 0f, 1.1f) * torque * tiltBoost;
                body.AddTorque(torqueVector, ForceMode.Acceleration);
            }

            float angularDamping = Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipAngularDamping.Value);
            if (angularDamping > 0f)
            {
                body.AddTorque(-body.angularVelocity * angularDamping, ForceMode.Acceleration);
            }

            float maxAngularVelocity = Mathf.Max(0.1f, PhysicalWaterPlugin.Settings.ShipMaxAngularVelocity.Value);
            if (body.angularVelocity.magnitude > maxAngularVelocity)
            {
                body.angularVelocity = body.angularVelocity.normalized * maxAngularVelocity;
            }
        }

        private static void LogShipDiagnosticIfNeeded(PhysicalWaterSystem system, Ship ship, Rigidbody body, BoxCollider floatCollider, Vector3 center)
        {
            if (!PhysicalWaterPlugin.Settings.Diagnostics.Value || Time.realtimeSinceStartup < s_nextShipDiagnosticTime)
            {
                return;
            }

            s_nextShipDiagnosticTime = Time.realtimeSinceStartup + 8f;
            float surface = system.GetAnimatedRideSurfaceHeight(
                center,
                PhysicalWaterPlugin.Settings.FloatingProbeFallbackRadius.Value,
                0.32f);
            float liftedSurface = surface + Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipSurfaceLift.Value);
            float targetCenterLift = Mathf.Clamp(ship.m_waterLevelOffset + Mathf.Max(0f, PhysicalWaterPlugin.Settings.ShipSurfaceLift.Value), 0.65f, 2.20f);
            float targetCenterY = surface + targetCenterLift;
            float depth = targetCenterY - center.y;
            float hullBottomY = GetFloatColliderHullBottomY(floatCollider);
            float bottomDepth = surface - hullBottomY;
            float tiltDegrees = Vector3.Angle(ship.transform.up, Vector3.up);
            PhysicalWaterPlugin.Log.LogInfo("Physical water ship diagnostic: ship=" + ship.name +
                                            ", surfaceY=" + surface.ToString("F2") +
                                            ", liftedSurfaceY=" + liftedSurface.ToString("F2") +
                                            ", targetCenterY=" + targetCenterY.ToString("F2") +
                                            ", centerY=" + center.y.ToString("F2") +
                                            ", centerError=" + depth.ToString("F2") +
                                            ", floatBottomY=" + hullBottomY.ToString("F2") +
                                            ", bottomDepth=" + bottomDepth.ToString("F2") +
                                            ", speedSetting=" + ship.GetSpeedSetting() +
                                            ", rudder=" + ship.GetRudderValue().ToString("F2") +
                                            ", tilt=" + tiltDegrees.ToString("F1") +
                                            ", angularSpeed=" + body.angularVelocity.magnitude.ToString("F2") +
                                            ", velocityY=" + body.linearVelocity.y.ToString("F2") +
                                            ", speed=" + body.linearVelocity.magnitude.ToString("F2") + ".");
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class CharacterCustomFixedUpdatePatch
    {
        private static readonly Dictionary<int, float> NextFeedByCharacterId = new Dictionary<int, float>();
        private static float NextLocalPlayerAuthorityTrace;
        private static readonly FieldInfo LiquidLevelField = AccessTools.Field(typeof(Character), "m_liquidLevel");
        private static readonly FieldInfo WaterLevelField = AccessTools.Field(typeof(Character), "m_waterLevel");
        private static readonly FieldInfo GroundContactField = AccessTools.Field(typeof(Character), "m_groundContact");
        private static readonly MethodInfo InLiquidSwimDepthMethod = AccessTools.Method(typeof(Character), "InLiquidSwimDepth", Type.EmptyTypes);

        private static void Prefix(Character __instance)
        {
            FeedCharacterLiquidLevel(__instance, false);
        }

        private static void Postfix(Character __instance)
        {
            FeedCharacterLiquidLevel(__instance, true);
        }

        private static void FeedCharacterLiquidLevel(Character __instance, bool force)
        {
            try
            {
                PhysicalWaterDevE1Runtime finite = PhysicalWaterDevE1Runtime.Instance;
                if (PhysicalWaterPlugin.Settings.StageE1Enabled.Value)
                {
                    if (__instance != null && __instance.IsPlayer() && __instance == Player.m_localPlayer && finite != null)
                    {
                        Vector3 finitePosition = __instance.transform.position;
                        bool hasFiniteAuthority = finite.TryGetLatestPlayerWaterSample(finitePosition,
                            out R4V9N1.PhysicalOcean.Volumetric.VolumetricWaterSample sample);
                        float finiteSurface = hasFiniteAuthority ? sample.SurfaceHeight : float.NegativeInfinity;
                        // The host query is a fallback for positions without an
                        // accepted LC column, never a second height evaluated
                        // alongside LC and never a max-composition authority.
                        float hostSurface = hasFiniteAuthority
                            ? float.NegativeInfinity
                            : Floating.GetLiquidLevel(finitePosition, 1f, LiquidType.Water);
                        float authoritativeSurface = R4V9N1.PhysicalOcean.Volumetric.LiquidCorePlayerWaterAuthorityResolver.SelectSurface(
                            hasFiniteAuthority, finiteSurface, hostSurface);
                        __instance.SetLiquidLevel(authoritativeSurface, LiquidType.Water, finite);
                    }
                    // Finite mode coexists with vanilla oceans. Feed their maximum
                    // explicitly so leaving an LC body cannot leave a stale level
                    // and cannot suppress a legitimate vanilla-water level.
                    return;
                }

                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                if (!PhysicalWaterSystem.IsEnabled() ||
                    system == null ||
                    __instance == null)
                {
                    return;
                }

                if (PhysicalWaterSystem.ShouldForceDryWaterQueries())
                {
                    __instance.SetLiquidLevel(-10000f, LiquidType.Water, system);
                    return;
                }

                if (!PhysicalWaterPlugin.Settings.FeedCharactersLiquidLevel.Value)
                {
                    return;
                }

                int id = __instance.GetInstanceID();
                float now = Time.time;
                float nextFeed;
                if (!force && NextFeedByCharacterId.TryGetValue(id, out nextFeed) && now < nextFeed)
                {
                    return;
                }

                if (!force)
                {
                    NextFeedByCharacterId[id] = now + Mathf.Max(0.05f, PhysicalWaterPlugin.Settings.CharacterWaterFeedInterval.Value);
                }

                Vector3 position = __instance.transform.position;
                float surface;
                bool presentedSurface = system.TryGetPresentedCharacterSurface(position, out surface);
                if (presentedSurface && surface > position.y - 1.75f)
                {
                    ApplySmallCreatureSwimAssist(__instance, surface);
                    __instance.SetLiquidLevel(surface, LiquidType.Water, system);
                    float waterDepth = Mathf.Max(0f, surface - position.y);
                    Rigidbody body = __instance.GetComponent<Rigidbody>();
                    float speed = body != null ? body.linearVelocity.magnitude : 0f;
                    float scale = Mathf.Clamp(PhysicalWaterPlugin.Settings.CharacterDisturbanceScale.Value, 0f, 1f);
                    float surfaceFactor = __instance.IsPlayer()
                        ? Mathf.Clamp01(1.25f - waterDepth * 0.18f)
                        : 1f;
                    float strength = Mathf.Clamp((0.018f + speed * 0.010f) * scale * surfaceFactor, 0.004f, 0.060f);
                    float radius = __instance.IsPlayer() ? Mathf.Lerp(1.05f, 2.25f, Mathf.Clamp01(speed / 7f)) : 0.95f;
                    if (waterDepth > 0.05f && waterDepth < 4.5f && speed > 0.18f && strength >= 0.004f)
                    {
                        Vector3 surfacePosition = new Vector3(position.x, surface, position.z);
                        system.Disturb(surfacePosition, strength, radius);
                    }
                }
                else
                {
                    __instance.SetLiquidLevel(-10000f, LiquidType.Water, system);
                }

                if (__instance.IsPlayer() && __instance == Player.m_localPlayer && Time.time >= NextLocalPlayerAuthorityTrace)
                {
                    NextLocalPlayerAuthorityTrace = Time.time + 1f;
                    float publishedLiquid = LiquidLevelField != null ? (float)LiquidLevelField.GetValue(__instance) : float.NaN;
                    float waterLevel = WaterLevelField != null ? (float)WaterLevelField.GetValue(__instance) : float.NaN;
                    bool groundContact = GroundContactField != null && (bool)GroundContactField.GetValue(__instance);
                    bool swimDepthState = InLiquidSwimDepthMethod != null && (bool)InLiquidSwimDepthMethod.Invoke(__instance, null);
                    PhysicalWaterPlugin.Log.LogInfo("LiquidCore player authority trace: " +
                        "pos=" + position.ToString("F2") +
                        ", wetSurface=" + surface.ToString("F2") +
                        ", presented=" + presentedSurface +
                        ", grid=" + system.GetPlayerGridDiagnostic() +
                        ", publishedLiquid=" + publishedLiquid.ToString("F2") +
                        ", waterLevel=" + waterLevel.ToString("F2") +
                        ", swimDepth=" + __instance.m_swimDepth.ToString("F2") +
                        ", groundContact=" + groundContact +
                        ", canSwim=" + __instance.m_canSwim +
                        ", isSwimming=" + __instance.IsSwimming() +
                        ", swimDepthState=" + swimDepthState +
                        ", inLiquid=" + __instance.InLiquid() +
                        ", inWater=" + __instance.InWater() + ".");
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater Character liquid-level feed failed: " + ex.Message);
            }
        }

        private static void ApplySmallCreatureSwimAssist(Character character, float surface)
        {
            if (!PhysicalWaterPlugin.Settings.SmallCreatureSwimAssist.Value ||
                character == null ||
                character.IsPlayer() ||
                character.IsDead() ||
                character.IsFlying() ||
                !character.m_canSwim)
            {
                return;
            }

            ZNetView view = character.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner())
            {
                return;
            }

            float height = Mathf.Max(0.1f, character.GetHeight());
            if (height > Mathf.Max(0.2f, PhysicalWaterPlugin.Settings.SmallCreatureMaxHeight.Value))
            {
                return;
            }

            float waterDepth = surface - character.transform.position.y;
            if (waterDepth <= 0.15f)
            {
                return;
            }

            float assistedSwimDepth = Mathf.Clamp(
                height * Mathf.Clamp(PhysicalWaterPlugin.Settings.SmallCreatureSwimDepthFactor.Value, 0.15f, 0.9f),
                0.22f,
                1.15f);

            if (character.m_swimDepth > assistedSwimDepth)
            {
                character.m_swimDepth = assistedSwimDepth;
            }

            Rigidbody body = character.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            float targetY = surface - character.m_swimDepth;
            if (character.transform.position.y < targetY - 0.15f)
            {
                Vector3 velocity = body.linearVelocity;
                float wantedRise = Mathf.Clamp((targetY - character.transform.position.y) * 2.2f, 0.25f, 2.4f);
                velocity.y = Mathf.MoveTowards(velocity.y, wantedRise, 4.5f * Time.fixedDeltaTime);
                body.linearVelocity = velocity;
            }
        }
    }

    internal static class GameCameraGetCameraPositionPatch
    {
        private static readonly MethodInfo Target = AccessTools.Method(typeof(GameCamera), "GetCameraPosition");
        private static readonly FieldInfo DistanceField = AccessTools.Field(typeof(GameCamera), "m_distance");
        private static readonly FieldInfo WaterClippingField = AccessTools.Field(typeof(GameCamera), "m_waterClipping");

        private static MethodBase TargetMethod()
        {
            return Target;
        }

        private static void Postfix(GameCamera __instance, ref Vector3 pos, ref Quaternion rot)
        {
            try
            {
                PhysicalWaterSystem system = PhysicalWaterSystem.Instance;
                Player player = Player.m_localPlayer;
                if (!PhysicalWaterSystem.IsEnabled() ||
                    system == null ||
                    player == null ||
                    __instance == null ||
                    PhysicalWaterSystem.ShouldForceDryWaterQueries() ||
                    !PhysicalWaterPlugin.Settings.DisableUnderwaterCameraClamp.Value)
                {
                    return;
                }

                Transform eye = player.m_eye;
                if (eye == null)
                {
                    return;
                }

                float surface = system.GetSurfaceHeight(eye.position, 1f);
                float requiredDepth = Mathf.Max(0.05f, PhysicalWaterPlugin.Settings.UnderwaterCameraFreeDepth.Value);
                if (surface <= eye.position.y + requiredDepth)
                {
                    return;
                }

                float distance = Vector3.Distance(pos, eye.position);
                if (DistanceField != null)
                {
                    object value = DistanceField.GetValue(__instance);
                    if (value is float configuredDistance && configuredDistance > 0.1f)
                    {
                        distance = configuredDistance;
                    }
                }

                pos = eye.position - eye.forward * Mathf.Clamp(distance, 0.8f, 16f);
                rot = eye.rotation;
                if (WaterClippingField != null)
                {
                    WaterClippingField.SetValue(__instance, false);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater underwater camera patch failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "Awake")]
    internal static class WaterVolumeAwakePatch
    {
        private static void Postfix(WaterVolume __instance)
        {
            try
            {
                if (__instance == null || !PhysicalWaterSystem.IsEnabled()) return;
                HideWaterVolumeRenderers(__instance);
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater WaterVolume visual suppression hook failed: " + ex.Message);
            }
        }

        private static void HideWaterVolumeRenderers(WaterVolume waterVolume)
        {
            VanillaWaterSuppression.HideRenderers(waterVolume);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.GetWaterSurface))]
    internal static class WaterVolumeGetWaterSurfacePatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!PhysicalWaterSystem.IsEnabled())
            {
                return true;
            }

            __result = -10000f;
            return false;
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "Start")]
    internal static class WaterVolumeStartPatch
    {
        private static void Postfix(WaterVolume __instance)
        {
            TryHide(__instance);
        }

        private static void TryHide(WaterVolume waterVolume)
        {
            if (PhysicalWaterSystem.IsEnabled())
            {
                VanillaWaterSuppression.HideRenderers(waterVolume);
            }
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "OnEnable")]
    internal static class WaterVolumeOnEnablePatch
    {
        private static void Postfix(WaterVolume __instance)
        {
            if (PhysicalWaterSystem.IsEnabled())
            {
                VanillaWaterSuppression.HideRenderers(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "OnTriggerEnter")]
    internal static class WaterVolumeOnTriggerEnterPatch
    {
        private static bool Prefix()
        {
            return !PhysicalWaterSystem.IsEnabled();
        }
    }

    [HarmonyPatch(typeof(WaterVolume), "OnTriggerExit")]
    internal static class WaterVolumeOnTriggerExitPatch
    {
        private static bool Prefix()
        {
            return !PhysicalWaterSystem.IsEnabled();
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters))]
    internal static class WaterVolumeUpdateFloatersPatch
    {
        private static bool Prefix()
        {
            return !PhysicalWaterSystem.IsEnabled();
        }
    }

    [HarmonyPatch(typeof(LiquidSurface), "Awake")]
    internal static class LiquidSurfaceAwakePatch
    {
        private static void Postfix(LiquidSurface __instance)
        {
            if (PhysicalWaterSystem.IsEnabled())
            {
                VanillaWaterSuppression.HideRenderers(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(LiquidSurface), nameof(LiquidSurface.GetSurface))]
    internal static class LiquidSurfaceGetSurfacePatch
    {
        private static bool Prefix(LiquidSurface __instance, ref float __result)
        {
            if (!PhysicalWaterSystem.IsEnabled() ||
                __instance == null)
            {
                return true;
            }

            try
            {
                if (__instance.GetLiquidType() != LiquidType.Water)
                {
                    return true;
                }
            }
            catch
            {
            }

            __result = -10000f;
            return false;
        }
    }

    [HarmonyPatch(typeof(LiquidSurface), "OnTriggerEnter")]
    internal static class LiquidSurfaceOnTriggerEnterPatch
    {
        private static bool Prefix(LiquidSurface __instance)
        {
            if (!PhysicalWaterSystem.IsEnabled() ||
                __instance == null)
            {
                return true;
            }

            try
            {
                return __instance.GetLiquidType() != LiquidType.Water;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(LiquidSurface), "OnTriggerExit")]
    internal static class LiquidSurfaceOnTriggerExitPatch
    {
        private static bool Prefix(LiquidSurface __instance)
        {
            if (!PhysicalWaterSystem.IsEnabled() ||
                __instance == null)
            {
                return true;
            }

            try
            {
                return __instance.GetLiquidType() != LiquidType.Water;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(LiquidSurface), "FixedUpdate")]
    internal static class LiquidSurfaceFixedUpdatePatch
    {
        private static bool Prefix(LiquidSurface __instance)
        {
            if (!PhysicalWaterSystem.IsEnabled() || __instance == null)
            {
                return true;
            }

            try
            {
                return __instance.GetLiquidType() != LiquidType.Water;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class VanillaWaterSuppression
    {
        internal static void HideRenderers(WaterVolume waterVolume)
        {
            if (waterVolume == null)
            {
                return;
            }

            if (waterVolume.m_waterSurface != null)
            {
                waterVolume.m_waterSurface.enabled = false;
            }

            Renderer[] renderers = waterVolume.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            if (PhysicalWaterSystem.ShouldSuppressVanillaWaterSources())
            {
                DisableColliders(waterVolume.gameObject);
            }
        }

        internal static void HideRenderers(LiquidSurface liquidSurface)
        {
            if (liquidSurface == null)
            {
                return;
            }

            try
            {
                if (liquidSurface.GetLiquidType() != LiquidType.Water)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            HideRenderersOn(liquidSurface.gameObject);

            LiquidVolume liquidVolume = liquidSurface.GetComponentInParent<LiquidVolume>();
            if (liquidVolume != null)
            {
                HideRenderersOn(liquidVolume.gameObject);
            }

            if (PhysicalWaterSystem.ShouldSuppressVanillaWaterSources())
            {
                DisableColliders(liquidSurface.gameObject);
                if (liquidVolume != null)
                {
                    DisableColliders(liquidVolume.gameObject);
                }
            }
        }

        private static void HideRenderersOn(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
        }

        private static void DisableColliders(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }
    }
}
