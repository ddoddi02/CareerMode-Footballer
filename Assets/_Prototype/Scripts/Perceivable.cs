using UnityEngine;
using UnityEngine.Rendering;

namespace Prototype
{
    /// <summary>
    /// Anything the player can perceive. Hides itself when unseen and leaves a
    /// fading "memory ghost" at the last known position.
    /// </summary>
    [DisallowMultipleComponent]
    public class Perceivable : MonoBehaviour
    {
        public Color tint = Color.white;
        public bool alwaysVisible = false;
        public Vector3 ghostScale = new Vector3(0.62f, 0.9f, 0.62f);

        public SightState State { get; private set; }
        public Vector3 LastSeenPos { get; private set; }
        public float LastSeenTime { get; private set; }
        public float Speed { get; private set; }

        /// <summary>Seconds since this target was last actually perceived.</summary>
        public float InfoAge { get { return Time.time - LastSeenTime; } }

        Renderer[] rends;
        MaterialPropertyBlock mpb;
        MaterialPropertyBlock ghostMpb;
        GameObject ghost;
        Renderer ghostRend;
        Vector3 prevPos;
        static Material sGhostMat;

        void Awake()
        {
            State = SightState.Unknown;
            LastSeenTime = -999f;
            rends = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();
            ghostMpb = new MaterialPropertyBlock();
            prevPos = transform.position;
            PerceptionSystem.Register(this);
        }

        void OnDestroy()
        {
            PerceptionSystem.Unregister(this);
            if (ghost != null) Destroy(ghost);
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Speed = Vector3.Distance(transform.position, prevPos) / dt;
            prevPos = transform.position;
        }

        void EnsureGhost()
        {
            if (ghost != null) return;
            if (sGhostMat == null) sGhostMat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.25f));

            ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ghost.name = name + " (Memory)";
            var col = ghost.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ghost.transform.localScale = ghostScale;
            ghostRend = ghost.GetComponent<Renderer>();
            ghostRend.sharedMaterial = sGhostMat;
            ghostRend.shadowCastingMode = ShadowCastingMode.Off;
            ghostRend.receiveShadows = false;
            ghost.SetActive(false);
        }

        public void Apply(SightState s)
        {
            if (alwaysVisible) s = SightState.Clear;
            State = s;
            float now = Time.time;

            bool showReal = s != SightState.Unknown;
            if (showReal)
            {
                LastSeenPos = transform.position;
                LastSeenTime = now;
            }

            // Peripheral targets read as dark silhouettes: you know something is
            // there, not who it is or what they are doing.
            Color c = s == SightState.Clear
                ? tint
                : new Color(tint.r * 0.28f, tint.g * 0.28f, tint.b * 0.28f, 1f);

            mpb.SetColor("_BaseColor", c);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                rends[i].enabled = showReal;
                if (showReal) rends[i].SetPropertyBlock(mpb);
            }

            EnsureGhost();
            float age = now - LastSeenTime;
            if (!showReal && LastSeenTime > 0f && age < PerceptionSystem.memorySeconds)
            {
                float a = 1f - age / PerceptionSystem.memorySeconds;
                ghost.SetActive(true);
                ghost.transform.position = LastSeenPos + Vector3.up * 0.9f;
                Color gc = tint;
                gc.a = 0.32f * a * a;
                ghostMpb.SetColor("_BaseColor", gc);
                ghostRend.SetPropertyBlock(ghostMpb);
            }
            else if (ghost.activeSelf)
            {
                ghost.SetActive(false);
            }
        }
    }
}
