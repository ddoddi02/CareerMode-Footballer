using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Fixed-yaw orthographic follow camera. The world never rotates, so the player
    /// keeps their spatial bearings; only the vision cone turns.
    ///
    /// The picture is centred AHEAD of him, along where he is looking (2026-10-22, the
    /// user's call): he stands toward the edge behind him and the cone he can see fills
    /// the rest of the screen. The lead turns slowly with his gaze so sweeping the
    /// cursor does not swing the pitch about. A shoulder check (Q) is a gaze like any
    /// other: the picture follows it round behind him at the same pace (user, 2026-10-22
    /// - it used to hold the way he had been looking, and the user expected it to move).
    /// However the lead lags, he is never let closer to the edge than edgeMargin.
    ///
    /// Holding the right button - the shot's first half - pulls the camera back
    /// (zoomOutSize) and letting go brings it in again. The lead and the margin are
    /// worked out on the size the screen has right now, so they scale with it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PitchCamera : MonoBehaviour
    {
        public Transform target;
        public FootballerController controller;

        [Header("Rig")]
        public float tilt = 58f;
        public float distance = 90f;
        [Tooltip("Half the screen height, in metres of the camera's view. Smaller is closer.")]
        public float orthoSize = 13.6f;
        [Tooltip("The same while the right button is held - pulled back, to see more while lining up a shot.")]
        public float zoomOutSize = 18f;
        [Tooltip("How quickly the camera pulls back and comes in again, per second.")]
        public float zoomSmooth = 6f;

        [Header("Framing - lead the picture toward where he is looking")]
        [Tooltip("How far ahead of him the picture is centred, along his gaze, as a fraction of half the screen height. 0.65: he stands 65% of the way from the centre to the edge behind him.")]
        [Range(0f, 0.85f)] public float lookAheadShare = 0.65f;
        [Tooltip("How quickly the lead turns with his gaze, per second. Low enough that sweeping the cursor round does not swing the pitch.")]
        public float lookSmooth = 3f;
        [Tooltip("He is never closer to the edge of the screen than this, as a fraction of half the screen.")]
        [Range(0f, 0.5f)] public float edgeMargin = 0.12f;
        [Tooltip("How quickly the picture follows him, per second.")]
        public float smooth = 9f;

        Camera cam;
        Vector3 focus;
        Vector2 lead;          // where the picture leads him, screen-space, 1 = lookAheadShare of half the height
        float size;            // the orthographic size right now, on its way to orthoSize or zoomOutSize
        bool inited;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            Vector3 at = target.position;
            at.y = 0f;

            float wantSize = controller != null && controller.AimingShot ? zoomOutSize : orthoSize;
            if (!inited) size = wantSize;
            size = Mathf.Lerp(size, wantSize, 1f - Mathf.Exp(-zoomSmooth * dt));

            float halfH = size;
            float halfW = size * cam.aspect;
            // A metre of pitch toward the top of the screen shows as sin(tilt) of a metre.
            float sinT = Mathf.Max(Mathf.Sin(tilt * Mathf.Deg2Rad), 0.1f);

            // Wherever his head points - the cursor, or behind him on a shoulder check.
            Vector2 wantLead = controller != null ? controller.HeadForward : Vector2.zero;
            lead = Vector2.Lerp(lead, wantLead, 1f - Mathf.Exp(-lookSmooth * dt));

            float ahead = lookAheadShare * halfH;
            Vector3 want = at + new Vector3(lead.x * ahead, 0f, lead.y * ahead / sinT);

            if (!inited) { focus = want; inited = true; }
            focus = Vector3.Lerp(focus, want, 1f - Mathf.Exp(-smooth * dt));

            // Never let him near the edge, however far behind the lead is.
            float limX = halfW * (1f - edgeMargin);
            float limY = halfH * (1f - edgeMargin);
            float sx = at.x - focus.x;
            float sy = (at.z - focus.z) * sinT;
            if (Mathf.Abs(sx) > limX) focus.x = at.x - Mathf.Sign(sx) * limX;
            if (Mathf.Abs(sy) > limY) focus.z = at.z - Mathf.Sign(sy) * limY / sinT;

            Quaternion rot = Quaternion.Euler(tilt, 0f, 0f);
            transform.rotation = rot;
            transform.position = focus - rot * Vector3.forward * distance;

            cam.orthographic = true;
            cam.orthographicSize = size;
        }
    }
}
