using UnityEngine;

namespace Prototype
{
    public enum PressStyle
    {
        Tight,  // right on your back
        Loose,  // a couple of metres off
        Drop    // dropped away, you are free to turn
    }

    /// <summary>
    /// Sits on the goal side of the receiver and holds a gap. The gap can CHANGE
    /// mid-round, which is what makes an early scan go stale and forces the player
    /// to time the shoulder check rather than spam it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DefenderAI : MonoBehaviour
    {
        public Transform target;
        public float speed = 5.8f;
        public bool active = true;

        public PressStyle Style { get; private set; }

        float lateral;
        float switchAfter = -1f;
        PressStyle switchTo;
        float t0;
        bool switched;

        CharacterController cc;
        Vector3 vel;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
        }

        public void Warp(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            vel = Vector3.zero;
        }

        public void Begin(PressStyle style, float lateralOffset, float switchDelay, PressStyle to)
        {
            Style = style;
            lateral = lateralOffset;
            switchAfter = switchDelay;
            switchTo = to;
            switched = false;
            t0 = Time.time;
        }

        public float Gap(Vector3 p)
        {
            Vector3 d = transform.position - p;
            d.y = 0f;
            return d.magnitude;
        }

        public static float DesiredGap(PressStyle s)
        {
            switch (s)
            {
                case PressStyle.Tight: return 0.72f;
                case PressStyle.Loose: return 2.9f;
                default: return 6.4f;
            }
        }

        void Update()
        {
            if (!active || target == null) return;

            if (!switched && switchAfter > 0f && Time.time - t0 > switchAfter)
            {
                Style = switchTo;
                switched = true;
            }

            Vector3 anchor = target.position + new Vector3(lateral, 0f, DesiredGap(Style));
            Vector3 d = anchor - transform.position;
            d.y = 0f;

            Vector3 want = Vector3.ClampMagnitude(d * 3.4f, speed);
            vel = Vector3.MoveTowards(vel, want, 32f * Time.deltaTime);
            cc.Move((vel + Vector3.down * 3f) * Time.deltaTime);

            Vector3 look = target.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
        }
    }
}
