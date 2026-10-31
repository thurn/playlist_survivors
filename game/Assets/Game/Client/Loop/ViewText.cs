using UnityEngine;

namespace Game.Client.Loop
{
    /// <summary>
    /// The debug surfaces' text (<see cref="DebugOverlay"/>, <see cref="DebugMenu"/>): a TextMesh in the camera's
    /// view, in front of everything, so the windowless stills draw it too, kept in a top corner whatever the aspect.
    /// </summary>
    public static class ViewText
    {
        const float Distance = 1f, Margin = 0.03f;

        public static TextMesh Make(Camera camera, string name, bool right, Color colour)
        {
            var go = new GameObject(name);
            if (camera != null) go.transform.SetParent(camera.transform, false);
            var text = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = 48;
            text.characterSize = 0.0055f;
            text.anchor = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            text.color = colour;
            return text;
        }

        /// <summary>Keeps the text at the view's top left (or right), the same size on screen at any aspect (a still's is 16:9) and field of view.</summary>
        public static void Place(Camera camera, TextMesh text)
        {
            if (camera == null) return;
            float aspect = Application.isBatchMode ? (float)Stills.Width / Stills.Height : camera.aspect;
            float h = Distance * Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
            float side = text.anchor == TextAnchor.UpperRight ? 1 : -1;
            var t = text.transform;
            t.localPosition = new Vector3(side * h * aspect * (1 - Margin), h * (1 - Margin), Distance);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (h / 0.58f);
        }
    }
}
