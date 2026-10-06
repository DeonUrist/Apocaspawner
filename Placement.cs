using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Apocaspawner
{
    internal static class Placement
    {
        internal static bool Ground(Vector3 candidate, out Vector3 surface)
        {
            surface = candidate;
            int ground = LayerMask.NameToLayer("Ground"); int mask = 1 | (ground >= 0 ? 1 << ground : 1 << 14);
            var hits = Physics.RaycastAll(candidate + Vector3.up * 100, Vector3.down, 1200, mask, QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y > .3f && h.collider != null && (h.collider.attachedRigidbody == null || h.collider.attachedRigidbody.isKinematic)).ToArray();
            if (hits.Length == 0) return false;
            surface = hits.OrderBy(h => Mathf.Abs(h.point.y - candidate.y)).First().point;
            return true;
        }
        // Capsule pivots are near the torso, not the feet: Sprokka's body capsule extends 0.95 m below its root.
        // Collider.bounds is empty for inactive prefabs, so calculate the root-relative support bound from collider geometry.
        internal static float Bottom(GameObject prefab)
        {
            float minimum = float.PositiveInfinity;
            foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !collider.enabled || !Parameters.ActiveUnder(collider.transform, prefab.transform)) continue;
                Bounds local;
                if (collider is CapsuleCollider capsule)
                {
                    var extent = Vector3.one * capsule.radius; extent[capsule.direction] = Mathf.Max(capsule.height * .5f, capsule.radius);
                    local = new Bounds(capsule.center, extent * 2);
                }
                else if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
                else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
                else continue;
                var matrix = prefab.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                    minimum = Mathf.Min(minimum, point.y * prefab.transform.localScale.y);
                }
            }
            return float.IsPositiveInfinity(minimum) ? 0 : minimum;
        }
        internal static Vector3 Resolve(Entry entry, Vector3 candidate)
        {
            if (!entry.IsNpc && !entry.IsVehicle) return candidate + Vector3.up * 1.2f;
            if (!Ground(candidate, out var surface)) throw new InvalidOperationException("No loaded ground at the spawn location. Choose a shorter distance or a different direction.");
            return surface + Vector3.up * (Mathf.Max(0, -Bottom(entry.Prefab)) + (entry.IsNpc ? .25f : .6f));
        }
        internal static GameObject Create(Entry entry, Vector3 position, Quaternion rotation, Action<GameObject> register)
        {
            var staging = new GameObject("Apocaspawner.SpawnStaging") { hideFlags = HideFlags.HideAndDontSave }; staging.SetActive(false);
            GameObject clone = null;
            try
            {
                // Keep gameplay callbacks dormant until position, physics and the save registration have been prepared.
                clone = UnityEngine.Object.Instantiate(entry.Prefab, position, rotation, staging.transform);
                Replacements.Apply(entry, clone);
                if (entry.Replacement != null) clone.transform.position += Vector3.up * (Bottom(entry.Prefab) - Bottom(clone));
                if (entry.IsNpc)
                {
                    var body = clone.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.detectCollisions = true;
                        if (!body.isKinematic && body.useGravity) body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    }
                }
                register(clone); clone.SetActive(true); clone.transform.SetParent(null, true);
                Replacements.Complete(entry, clone);
                if (entry.IsNpc)
                {
                    Physics.SyncTransforms();
                    var solid = clone.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                    if (solid.Length > 0)
                    {
                        float low = solid.Min(c => c.bounds.min.y), expected = position.y + Bottom(entry.Prefab);
                        if (low < expected) clone.transform.position += Vector3.up * (expected - low);
                    }
                    Physics.SyncTransforms();
                }
                return clone;
            }
            catch { if (clone != null) UnityEngine.Object.Destroy(clone); throw; }
            finally { UnityEngine.Object.Destroy(staging); }
        }
    }
}
