using Demo3D.Runtime.Playback;
using NUnit.Framework;
using PS.Choreography;
using UnityEngine;

namespace Demo3D.Tests
{
    /// <summary>The game camera's heading (trailer.md, "Camera"): it sees the ground the simulation counts as in view.</summary>
    public class GameCameraRigTests
    {
        [Test]
        public void ATurnedCameraSeesTheViewTheSimulationCounts([Values(0f, 45f, 90f, 180f, -90f)] float yaw)
        {
            var rig = new GameCameraRig { YawDeg = yaw };
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            float shadows = urp != null ? urp.shadowDistance : 0; // Frame fits the pipeline's shadows to the view
            try
            {
                rig.Camera.aspect = 16f / 9f;
                var focus = new Vector2(30, 40);
                const float width = 40;
                rig.Frame(focus, width);
                var view = new ViewGeometry(width, ViewGeometry.PitchDeg, yaw);
                bool Sees(float right, float ahead)
                {
                    var g = view.ToWorld(new System.Numerics.Vector2(right, ahead));
                    return rig.OnScreen(new Vector3(focus.x + g.X, 0, focus.y + g.Y), 0);
                }
                // Just inside and well outside each edge of the counted ground, in the view's own axes.
                Assert.That(Sees(0, view.Far - 1), Is.True, "far edge, inside");
                Assert.That(Sees(0, view.Far + 3), Is.False, "far edge, outside");
                Assert.That(Sees(0, -view.Near + 1), Is.True, "near edge, inside");
                Assert.That(Sees(0, -view.Near - 3), Is.False, "near edge, outside");
                Assert.That(Sees(view.HalfWidthAt(0) - 1, 0), Is.True, "right edge, inside");
                Assert.That(Sees(view.HalfWidthAt(0) + 3, 0), Is.False, "right edge, outside");
                Assert.That(Sees(-view.HalfWidthAt(0) + 1, 0), Is.True, "left edge, inside");
                Assert.That(Sees(-view.HalfWidthAt(0) - 3, 0), Is.False, "left edge, outside");
            }
            finally
            {
                Object.DestroyImmediate(rig.Camera.gameObject);
                if (urp != null) urp.shadowDistance = shadows;
            }
        }
    }
}
