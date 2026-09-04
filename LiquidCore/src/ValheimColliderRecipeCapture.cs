using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalWater
{
    internal static class ValheimColliderRecipeCapture
    {
        internal static ValheimKnowledgeDatabase.ColliderRecipe[] Capture(GameObject root)
        {
            if (root == null) return Array.Empty<ValheimKnowledgeDatabase.ColliderRecipe>();
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            var recipes = new ValheimKnowledgeDatabase.ColliderRecipe[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                Transform transform = collider.transform;
                Collider[] localColliders = transform.GetComponents<Collider>();
                int componentIndex = -1;
                for (int localIndex = 0; localIndex < localColliders.Length; localIndex++)
                {
                    if (!ReferenceEquals(localColliders[localIndex], collider)) continue;
                    componentIndex = localIndex;
                    break;
                }
                var recipe = new ValheimKnowledgeDatabase.ColliderRecipe
                {
                    transformChildIndices = ChildIndexPath(root.transform, transform),
                    colliderComponentIndex = componentIndex,
                    colliderType = collider.GetType().Name,
                    enabled = collider.enabled,
                    isTrigger = collider.isTrigger
                };
                if (collider is MeshCollider meshCollider)
                {
                    Mesh mesh = meshCollider.sharedMesh;
                    recipe.meshName = mesh != null ? mesh.name : string.Empty;
                    recipe.meshVertexCount = mesh != null ? mesh.vertexCount : 0;
                    recipe.meshTriangleCount = mesh != null ? MeshTriangleCount(mesh) : 0;
                    recipe.convex = meshCollider.convex;
                }
                else if (collider is BoxCollider box)
                {
                    recipe.center = Vector(box.center);
                    recipe.size = Vector(box.size);
                }
                else if (collider is SphereCollider sphere)
                {
                    recipe.center = Vector(sphere.center);
                    recipe.radius = sphere.radius;
                }
                else if (collider is CapsuleCollider capsule)
                {
                    recipe.center = Vector(capsule.center);
                    recipe.radius = capsule.radius;
                    recipe.height = capsule.height;
                    recipe.direction = capsule.direction;
                }
                recipes[i] = recipe;
            }
            return recipes;
        }

        private static int[] ChildIndexPath(Transform root, Transform child)
        {
            if (root == null || child == null) return Array.Empty<int>();
            var reversed = new List<int>();
            Transform current = child;
            while (current != null && current != root)
            {
                reversed.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            if (current != root) return Array.Empty<int>();
            reversed.Reverse();
            return reversed.ToArray();
        }

        private static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };

        private static int MeshTriangleCount(Mesh mesh)
        {
            int triangles = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                triangles += (int)(mesh.GetIndexCount(subMesh) / 3u);
            return triangles;
        }
    }
}
