using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>
    /// Stand-ins until the trailer's edge look is ported: Unity's primitives drawn with Resources/Champions/StandIn.shader
    /// (a near-black body whose silhouette glows in a role's colour), sized to what they stand for. Each is a root standing
    /// on its feet at its own origin, so a presenter places it by its feet and turns it about the vertical; the body under
    /// it has no collider. The ID code on the material tells the stills' type-ID pass what it is.
    /// </summary>
    public static class StandIn
    {
        public const string ShaderPath = "Champions/StandIn";
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdBody = Shader.PropertyToID("_Body");

        /// <summary>A stand-in material: its silhouette in <paramref name="line"/> over <paramref name="body"/>, coded <paramref name="idCode"/>.</summary>
        public static Material Material(string name, Color line, Color body, int idCode)
        {
            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new InvalidOperationException($"no {ShaderPath} shader");
            var m = new Material(shader) { name = name };
            m.SetColor(IdColor, line);
            m.SetColor(IdBody, body);
            m.SetFloat(IdPass.Id, idCode);
            return m;
        }

        /// <summary>Sets a stand-in material's silhouette colour (a hurt flash, a flare).</summary>
        public static void SetLine(Material m, Color line) => m.SetColor(IdColor, line);

        /// <summary>
        /// A stand-in of <paramref name="shape"/> <paramref name="size"/> metres (across x, tall, across z) standing on its
        /// root's origin, on <paramref name="layer"/>, under <paramref name="parent"/> if given.
        /// </summary>
        public static GameObject Make(string name, PrimitiveType shape, Vector3 size, Material material, int layer = 0, Transform parent = null)
        {
            var root = new GameObject(name) { layer = layer };
            if (parent != null) root.transform.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(shape);
            body.name = name + " body";
            body.layer = layer;
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            // Unity's primitives are 1 m across; its capsule and cylinder 2 m tall.
            float tall = shape == PrimitiveType.Capsule || shape == PrimitiveType.Cylinder ? 2 : 1;
            body.transform.localScale = new Vector3(size.x, size.y / tall, size.z);
            body.transform.localPosition = new Vector3(0, size.y / 2, 0);
            var r = body.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return root;
        }

        /// <summary>Destroys a stand-in or its material, in play mode or in an EditMode test.</summary>
        public static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
