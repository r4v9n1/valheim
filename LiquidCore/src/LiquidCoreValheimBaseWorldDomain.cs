using System;
using System.Reflection;
using UnityEngine;

namespace PhysicalWater
{
    /// <summary>
    /// Deterministic base-world spatial contract for the PCE bootstrap scan.
    /// This describes where geometry may be sampled; it is not a water source,
    /// a wet/dry classifier, or an E3 domain.
    /// </summary>
    internal static class LiquidCoreValheimBaseWorldDomain
    {
        private static readonly FieldInfo WorldSizeField = typeof(WorldGenerator).GetField(
            "m_worldSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool TryGetBounds(
            float verticalMin, float verticalMax, out Bounds bounds, out string error)
        {
            bounds = default(Bounds);
            error = string.Empty;
            if (WorldGenerator.instance == null)
            {
                error = "Valheim world generator is not initialized.";
                return false;
            }
            if (!Finite(verticalMin) || !Finite(verticalMax) || verticalMax <= verticalMin)
            {
                error = "Base-world PCE vertical bounds are invalid.";
                return false;
            }
            if (WorldSizeField == null)
            {
                error = "Current Valheim WorldGenerator has no m_worldSize field.";
                return false;
            }
            object rawWorldSize = WorldSizeField.GetValue(WorldGenerator.instance);
            float worldSize;
            try { worldSize = Convert.ToSingle(rawWorldSize); }
            catch (Exception) { worldSize = float.NaN; }
            if (!Finite(worldSize) || worldSize <= 0f)
            {
                error = "Valheim world generator returned an invalid world size.";
                return false;
            }
            // m_worldSize defines the deterministic horizontal world extent.
            // The caller supplies the vertical geometry scan interval; it is
            // never inferred from SeaLevel, biome, or water state.
            bounds = new Bounds(
                new Vector3(0f, (verticalMin + verticalMax) * 0.5f, 0f),
                new Vector3(worldSize, verticalMax - verticalMin, worldSize));
            return true;
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
