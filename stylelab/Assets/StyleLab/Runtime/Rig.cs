using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>
    /// Builds a type's figure the same way in the editor's bake and in the player: the vendor
    /// prefab with its weapons attached. The bake merges its renderers into one mesh; the player
    /// hides them and draws that.
    /// </summary>
    public static class Rig
    {
        public static GameObject Spawn(GameObject prefab, WeaponData[] weapons, Transform parent)
        {
            var go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = prefab.name;
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            foreach (var w in weapons ?? Array.Empty<WeaponData>())
            {
                var bone = FindBone(go, animator, w.bone);
                if (bone == null || w.prefab == null) continue;
                var weapon = UnityEngine.Object.Instantiate(w.prefab, bone, false);
                weapon.name = w.prefab.name;
            }
            return go;
        }

        /// <summary>The renderers a figure draws with, in a fixed order.</summary>
        public static List<Renderer> Renderers(GameObject go) =>
            go.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled && r.gameObject.activeInHierarchy)
                .Where(r => MeshOf(r) != null)
                .ToList();

        public static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh
            : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;

        public static Transform FindBone(GameObject go, Animator animator, string name)
        {
            if (animator != null && animator.isHuman && Enum.TryParse(name, out HumanBodyBones hb))
                return animator.GetBoneTransform(hb);
            return go.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(x => x.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The figure's height above its root, from its meshes' bounds through their transforms.</summary>
        public static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null || !r.gameObject.activeInHierarchy) continue;
                var b = r.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    var w = r.transform.TransformPoint(c);
                    top = Mathf.Max(top, w.y);
                    bottom = Mathf.Min(bottom, w.y);
                }
            }
            if (top == float.MinValue) return 0;
            return top - Mathf.Min(bottom, go.transform.position.y);
        }
    }
}
