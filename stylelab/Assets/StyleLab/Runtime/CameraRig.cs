using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StyleLab.Runtime
{
    /// <summary>The lab's camera shots, cycled with C.</summary>
    public sealed class CameraRig
    {
        public struct Shot
        {
            public string name;
            public float pitch, yaw, distance, fov, orbit;
            public Vector3 lookAt;
        }

        public static readonly Shot[] Shots =
        {
            new Shot { name = "Gameplay", pitch = 58f, yaw = 0f, distance = 25f, fov = 30f, lookAt = new Vector3(0, 0.5f, 1.5f) },
            new Shot { name = "Close", pitch = 40f, yaw = 25f, distance = 11f, fov = 30f, lookAt = new Vector3(0.5f, 0.8f, 2.8f) },
            new Shot { name = "Low", pitch = 22f, yaw = 15f, distance = 13f, fov = 38f, lookAt = new Vector3(0, 1.2f, 1.5f) },
            new Shot { name = "Orbit", pitch = 36f, yaw = 0f, distance = 19f, fov = 30f, orbit = 9f, lookAt = new Vector3(0, 0.8f, 0) },
        };

        public readonly Camera Camera;
        public int Index { get; private set; }
        float _orbit;

        public CameraRig(Transform parent)
        {
            var go = new GameObject("Camera");
            go.transform.SetParent(parent, false);
            go.tag = "MainCamera";
            Camera = go.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.02f, 0.015f, 0.03f);
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 200f;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.renderShadows = true;
            urp.requiresDepthOption = CameraOverrideOption.On;
            urp.requiresColorOption = CameraOverrideOption.On;
            go.AddComponent<AudioListener>();
        }

        public void Set(int index)
        {
            Index = (index % Shots.Length + Shots.Length) % Shots.Length;
            _orbit = 0;
            Update(0);
        }

        public void Update(float dt)
        {
            var s = Shots[Index];
            _orbit += s.orbit * dt;
            var rot = Quaternion.Euler(s.pitch, s.yaw + _orbit, 0);
            Camera.fieldOfView = s.fov;
            Camera.transform.SetPositionAndRotation(s.lookAt + rot * new Vector3(0, 0, -s.distance), rot);
        }
    }
}
