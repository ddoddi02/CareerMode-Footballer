using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Fixed-yaw orthographic follow camera. The world never rotates, so the player
    /// keeps their spatial bearings; only the vision cone turns. The focus point is
    /// pushed toward where the player is LOOKING, so a shoulder check actually
    /// reveals screen space instead of pointing the cone off the edge.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PitchCamera : MonoBehaviour
    {
        public Transform target;
        public FootballerController controller;

        [Header("Rig")]
        public float tilt = 58f;
        public float distance = 90f;
        public float orthoSize = 16f;

        [Header("Framing")]
        public float lookAhead = 3.4f;
        public float smooth = 9f;

        Camera cam;
        Vector3 focus;
        bool inited;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 want = target.position;
            want.y = 0f;

            if (controller != null)
            {
                Vector2 hf = controller.HeadForward;
                want += new Vector3(hf.x, 0f, hf.y) * lookAhead;
            }

            if (!inited) { focus = want; inited = true; }
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-smooth * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(tilt, 0f, 0f);
            transform.rotation = rot;
            transform.position = focus - rot * Vector3.forward * distance;

            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
        }
    }
}
