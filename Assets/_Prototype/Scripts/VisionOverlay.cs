using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Drives both halves of the vision system every frame:
    ///
    ///  1. The SCREEN effect - an overlay quad parented to the orthographic camera.
    ///     Because the camera is orthographic every view ray is parallel to the
    ///     camera forward vector, so the shader can recover the ground point under
    ///     each pixel with a single ray/plane intersection. No depth buffer, no
    ///     renderer feature, no shadow casters.
    ///
    ///  2. The GAMEPLAY truth - what the simulation considers the player to know.
    /// </summary>
    [ExecuteAlways]
    public class VisionOverlay : MonoBehaviour
    {
        public Camera cam;
        public FootballerController viewer;
        public Transform quad;

        [Header("Cone (degrees from head forward)")]
        public float focusAngle = 26f;
        public float peripheralAngle = 55f;
        public float edgeAngle = 80f;

        [Header("Range")]
        public float maxDistance = 30f;
        public float freeRadius = 2.0f;
        public float memorySeconds = 4.5f;

        static readonly int ID_ViewerPos = Shader.PropertyToID("_ViewerPos");
        static readonly int ID_ViewerFwd = Shader.PropertyToID("_ViewerFwd");
        static readonly int ID_CamFwd = Shader.PropertyToID("_VisionCamFwd");
        static readonly int ID_Cone = Shader.PropertyToID("_ConeParams");
        static readonly int ID_Cone2 = Shader.PropertyToID("_ConeParams2");

        void Reset()
        {
            cam = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || viewer == null) return;

            PerceptionSystem.focusAngle = focusAngle;
            PerceptionSystem.peripheralAngle = peripheralAngle;
            PerceptionSystem.edgeAngle = edgeAngle;
            PerceptionSystem.maxDistance = maxDistance;
            PerceptionSystem.freeRadius = freeRadius;
            PerceptionSystem.memorySeconds = memorySeconds;

            Vector3 eye = viewer.transform.position;
            Vector2 fwd = viewer.HeadForward;

            Shader.SetGlobalVector(ID_ViewerPos, new Vector4(eye.x, eye.y, eye.z, 0f));
            Shader.SetGlobalVector(ID_ViewerFwd, new Vector4(fwd.x, fwd.y, 0f, 0f));
            Shader.SetGlobalVector(ID_CamFwd, cam.transform.forward);
            Shader.SetGlobalVector(ID_Cone, new Vector4(focusAngle, peripheralAngle, edgeAngle, maxDistance));
            Shader.SetGlobalVector(ID_Cone2,
                new Vector4(freeRadius, 3f, PerceptionSystem.xrayDebug ? 1f : 0f, 0f));

            FitQuad();

            if (Application.isPlaying) UpdatePerception(eye, fwd);
        }

        void FitQuad()
        {
            if (quad == null || !cam.orthographic) return;
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            quad.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.05f);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(w * 1.06f, h * 1.06f, 1f);
        }

        void UpdatePerception(Vector3 eye, Vector2 fwd)
        {
            var all = PerceptionSystem.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                all[i].Apply(PerceptionSystem.Evaluate(eye, fwd, all[i]));
            }
        }
    }
}
