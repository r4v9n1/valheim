using System;
using System.Collections.Generic;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    /// <summary>
    /// Builds a geometry-only provisional PCE descriptor from deterministic
    /// Valheim base terrain. It deliberately emits no catchment ownership,
    /// atoms, water volume, or E3 operation until global boundary closure.
    /// </summary>
    internal static class LiquidCoreValheimBaseTerrainPceBuilder
    {
        internal static bool TryBuildFromWorldGenerator(
            Bounds partitionBounds, Vector2 partitionSize, float cellSize,
            long geometryRevision, string dependencyRevisionHash,
            out VolumetricPceCapacityStorageDescriptor descriptor, out string error)
        {
            descriptor = null;
            error = string.Empty;
            if (WorldGenerator.instance == null)
            {
                error = "Valheim world generator is not initialized.";
                return false;
            }
            return TryBuild(
                partitionBounds, partitionSize, cellSize, geometryRevision,
                dependencyRevisionHash,
                (float x, float z, out float height) =>
                {
                    height = WorldGenerator.instance.GetHeight(x, z);
                    return Finite(height);
                }, out descriptor, out error);
        }

        internal static bool TryBuild(
            Bounds partitionBounds, Vector2 partitionSize, float cellSize,
            long geometryRevision, string dependencyRevisionHash,
            HeightSampler sampler,
            out VolumetricPceCapacityStorageDescriptor descriptor, out string error)
        {
            descriptor = null;
            error = string.Empty;
            if (sampler == null || geometryRevision < 0 ||
                string.IsNullOrWhiteSpace(dependencyRevisionHash) ||
                !Finite(partitionBounds) || partitionBounds.size.x <= 0f ||
                partitionBounds.size.y <= 0f || partitionBounds.size.z <= 0f ||
                !Finite(partitionSize) || partitionSize.x <= 0f || partitionSize.y <= 0f ||
                !Finite(cellSize) || cellSize <= 0f)
            {
                error = "Base terrain PCE builder received invalid geometry inputs.";
                return false;
            }
            int nx = ExactResolution(partitionBounds.size.x, cellSize);
            int ny = ExactResolution(partitionBounds.size.y, cellSize);
            int nz = ExactResolution(partitionBounds.size.z, cellSize);
            if (nx <= 0 || ny <= 0 || nz <= 0)
            {
                error = "Base terrain PCE partition bounds are not aligned to cell size.";
                return false;
            }
            int cellCount = nx * ny * nz;
            var capacity = new float[cellCount];
            var columnTerrainHeights = new float[nx * nz];
            var components = new int[cellCount];
            for (int i = 0; i < components.Length; i++) components[i] = -1;
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float worldX = partitionBounds.min.x + (x + 0.5f) * cellSize;
                float worldZ = partitionBounds.min.z + (z + 0.5f) * cellSize;
                if (!sampler(worldX, worldZ, out float ground))
                {
                    error = "Valheim base terrain sampler returned no height.";
                    return false;
                }
                columnTerrainHeights[x + nx * z] = ground;
                for (int y = 0; y < ny; y++)
                {
                    int cell = Index(x, y, z, nx, ny);
                    float bottom = partitionBounds.min.y + y * cellSize;
                    float top = bottom + cellSize;
                    float openHeight = Mathf.Clamp(top - Mathf.Max(bottom, ground), 0f, cellSize);
                    capacity[cell] = openHeight * cellSize * cellSize;
                }
            }
            AssignLocalComponents(capacity, components, nx, ny, nz, cellSize);
            var exits = BuildBoundaryExits(capacity, components, nx, ny, nz,
                partitionBounds, partitionSize, cellSize);
            descriptor = new VolumetricPceCapacityStorageDescriptor
            {
                CatchmentId = 0UL,
                SourcePartitionId = VolumetricPceSourcePartitionGrid.StablePartitionId(
                    partitionBounds, partitionSize),
                CompleteSourceDomain = false,
                GeometryRevision = geometryRevision,
                DependencyRevisionHash = dependencyRevisionHash,
                WorldBounds = partitionBounds,
                GridWorldOrigin = partitionBounds.min,
                CellSize = cellSize,
                ResolutionX = nx,
                ResolutionY = ny,
                ResolutionZ = nz,
                CellCapacity = capacity,
                CellStorageCurves = Array.Empty<Vector2>(),
                CellStorageKnotCounts = Array.Empty<int>(),
                ColumnTerrainHeights = columnTerrainHeights,
                CellCatchmentIds = new ulong[cellCount],
                CellComponentIds = components,
                Columns = Array.Empty<VolumetricPceStorageColumn>(),
                Exits = exits
            };
            if (!descriptor.Validate(out error))
            {
                descriptor = null;
                return false;
            }
            return true;
        }

        internal delegate bool HeightSampler(float x, float z, out float height);

        private static void AssignLocalComponents(float[] capacity, int[] components,
            int nx, int ny, int nz, float cellSize)
        {
            int nextComponent = 0;
            var queue = new Queue<int>();
            for (int seed = 0; seed < components.Length; seed++)
            {
                if (capacity[seed] <= 1e-6f || components[seed] >= 0) continue;
                components[seed] = nextComponent;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = current % nx;
                    int y = (current / nx) % ny;
                    int z = current / (nx * ny);
                    Visit(x - 1, y, z); Visit(x + 1, y, z);
                    Visit(x, y - 1, z); Visit(x, y + 1, z);
                    Visit(x, y, z - 1); Visit(x, y, z + 1);
                    void Visit(int xx, int yy, int zz)
                    {
                        if (xx < 0 || xx >= nx || yy < 0 || yy >= ny || zz < 0 || zz >= nz) return;
                        int next = Index(xx, yy, zz, nx, ny);
                        if (capacity[next] <= 1e-6f || components[next] >= 0) return;
                        components[next] = nextComponent;
                        queue.Enqueue(next);
                    }
                }
                nextComponent++;
            }
        }

        private static VolumetricPceStorageExit[] BuildBoundaryExits(float[] capacity, int[] components,
            int nx, int ny, int nz, Bounds bounds, Vector2 partitionSize, float cellSize)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var exits = new List<VolumetricPceStorageExit>();
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int cell = Index(x, y, z, nx, ny);
                if (capacity[cell] <= 1e-6f || components[cell] < 0) continue;
                Add(x == 0, components[cell], -1, 0, x, y, z, nx - 1, y, z);
                Add(x == nx - 1, components[cell], 1, 0, x, y, z, 0, y, z);
                Add(z == 0, components[cell], 0, -1, x, y, z, x, y, nz - 1);
                Add(z == nz - 1, components[cell], 0, 1, x, y, z, x, y, 0);
            }
            return exits.ToArray();

            void Add(bool boundary, int component, int dx, int dz, int x, int y, int z,
                int destinationX, int destinationY, int destinationZ)
            {
                if (!boundary) return;
                int tileX = Mathf.FloorToInt(bounds.min.x / partitionSize.x) + dx;
                int tileZ = Mathf.FloorToInt(bounds.min.z / partitionSize.y) + dz;
                string key = component + ":" + tileX + ":" + tileZ + ":" + x + ":" + y + ":" + z;
                if (!seen.Add(key)) return;
                exits.Add(new VolumetricPceStorageExit
                {
                    SourceComponentId = component,
                    SourceBoundaryCellX = x,
                    SourceBoundaryCellY = y,
                    SourceBoundaryCellZ = z,
                    DestinationBoundaryCellX = destinationX,
                    DestinationBoundaryCellY = destinationY,
                    DestinationBoundaryCellZ = destinationZ,
                    DestinationRegionX = tileX,
                    DestinationRegionZ = tileZ,
                    SaddleHeight = bounds.min.y + y * cellSize,
                    PathLength = cellSize,
                    MinimumApertureEquivalent = cellSize
                });
            }
        }

        private static int ExactResolution(float extent, float cellSize)
        {
            int value = Mathf.RoundToInt(extent / cellSize);
            return Mathf.Abs(extent - value * cellSize) <= 1e-4f ? value : -1;
        }

        private static int Index(int x, int y, int z, int nx, int ny) => x + nx * (y + ny * z);
        private static bool Finite(Bounds value) => Finite(value.center) && Finite(value.size);
        private static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
