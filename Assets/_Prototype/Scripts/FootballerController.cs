using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The heart of the prototype: movement, torso orientation and head orientation
    /// are three independent axes.
    ///
    ///   - Mouse / right stick sets the TORSO angle in normal play.
    ///   - Hold RMB / LT / LeftShift to freeze the torso and swing the HEAD freely.
    ///   - Tap Q / RB for an automatic quick sweep behind you.
    ///
    /// The torso angle at the moment of the first touch decides what you can do
    /// with the ball. The head angle decides what you know.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FootballerController : MonoBehaviour
    {
        [Header("Movement")]
        public float maxSpeed = 6.2f;
        public float accel = 34f;
        public float scanSpeedMul = 0.72f;
        public float backpedalMul = 0.62f;

        [Header("Turning (deg/sec)")]
        public float bodyTurn = 460f;
        public float headTurn = 900f;
        public float headReturn = 620f;

        [Header("Quick scan")]
        public float quickScanAngle = 168f;
        public float quickScanHold = 0.34f;

        [Header("Refs")]
        public Camera cam;
        public Transform headMarker;

        public float BodyAngle { get; private set; }
        public float HeadAngle { get; private set; }
        public bool Scanning { get; private set; }
        public float Speed { get { return new Vector2(vel.x, vel.z).magnitude; } }

        public Vector2 BodyForward
        {
            get { float r = BodyAngle * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }
        }

        public Vector2 HeadForward
        {
            get { float r = HeadAngle * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }
        }

        CharacterController cc;
        Vector3 vel;
        float quickScanUntil = -1f;
        float quickScanDir = 1f;
        readonly Plane ground = new Plane(Vector3.up, 0f);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cam == null) cam = Camera.main;
        }

        public void Teleport(Vector3 pos, float bodyAngle)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            BodyAngle = bodyAngle;
            HeadAngle = bodyAngle;
            vel = Vector3.zero;
            quickScanUntil = -1f;
            transform.rotation = Quaternion.Euler(0f, BodyAngle, 0f);
        }

        float AimAngle()
        {
            Vector2 rs = ProtoInput.RightStick();
            if (rs.sqrMagnitude > 0.16f)
                return Mathf.Atan2(rs.x, rs.y) * Mathf.Rad2Deg;

            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(ProtoInput.MousePosition());
                float t;
                if (ground.Raycast(ray, out t))
                {
                    Vector3 hit = ray.GetPoint(t);
                    Vector3 d = hit - transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.04f)
                        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
            }
            return HeadAngle;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            bool quick = Time.time < quickScanUntil;
            if (ProtoInput.QuickScanPressed() && !quick)
            {
                quickScanUntil = Time.time + quickScanHold;
                quickScanDir = Random.value < 0.5f ? 1f : -1f;
                quick = true;
            }

            Scanning = ProtoInput.ScanHeld() || quick;
            float aim = AimAngle();

            if (quick)
            {
                // Automatic sweep to look over the shoulder, then it snaps back.
                HeadAngle = Mathf.MoveTowardsAngle(
                    HeadAngle, BodyAngle + quickScanAngle * quickScanDir, headTurn * 1.7f * dt);
            }
            else if (Scanning)
            {
                // Torso is frozen; the head is free.
                HeadAngle = Mathf.MoveTowardsAngle(HeadAngle, aim, headTurn * dt);
            }
            else
            {
                BodyAngle = Mathf.MoveTowardsAngle(BodyAngle, aim, bodyTurn * dt);
                HeadAngle = Mathf.MoveTowardsAngle(HeadAngle, BodyAngle, headReturn * dt);
            }

            transform.rotation = Quaternion.Euler(0f, BodyAngle, 0f);
            if (headMarker != null)
                headMarker.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(BodyAngle, HeadAngle), 0f);

            Vector2 mv = ProtoInput.Move();
            Vector3 want = new Vector3(mv.x, 0f, mv.y) * maxSpeed;

            if (Scanning) want *= scanSpeedMul;
            if (mv.sqrMagnitude > 0.01f && Vector2.Dot(BodyForward, mv.normalized) < -0.2f)
                want *= backpedalMul;   // running backwards is slower

            vel = Vector3.MoveTowards(vel, want, accel * dt);
            cc.Move((vel + Vector3.down * 3f) * dt);
        }
    }
}
