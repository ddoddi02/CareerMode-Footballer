using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Energy in a match (stamina) and the two things that cost speed besides it: carrying the
    /// ball (dribbling) and being tired. Runs only for a match the career asked for. Each body
    /// has an energy 0..1 that starts at the day's remaining energy and drains with how hard he
    /// runs, slower the more stamina he has; below `tiredBelow` he loses speed and aim.
    ///
    /// Speed is applied here, every frame, from the numbers MatchRoster left (base x factor),
    /// so nothing else has to know the body is tired. Aim goes through IForm.FormPercent
    /// (MatchAttributes.accuracy), so it reaches every stat that already reads his form.
    /// Everything is a placeholder the user has not seen (PROJECT.md 3.28).
    /// </summary>
    public class MatchStamina : MonoBehaviour
    {
        [Tooltip("Energy lost per second just being on the pitch, at stamina 50.")]
        public float restDrain = 0.0004f;
        [Tooltip("Extra per second at a full sprint, at stamina 50. A jog (4 m/s) costs nothing extra.")]
        public float sprintDrain = 0.0016f;
        [Tooltip("How fast he tires at stamina 1 and at stamina 100, as a multiple of stamina 50 (a straight line between).")]
        public Vector2 staminaFactor = new Vector2(1.3f, 0.7f);
        [Tooltip("Above this much energy nothing is lost. Below it speed and aim fall away towards the values at empty.")]
        [Range(0.05f, 1f)] public float tiredBelow = 0.6f;
        [Tooltip("Speed and aim kept at zero energy.")]
        public float speedAtEmpty = 0.85f, aimAtEmpty = 0.85f;

        class Entry
        {
            public Transform t;
            public AttackerAI atk;
            public DefenderAI def;
            public FootballerController fc;
            public MatchAttributes attrs;
            public Vector3 last;
            public float atkSpeed, atkSprint, defSpeed, defSprint, defJog, fcWalk, fcSprint;
        }

        readonly List<Entry> entries = new List<Entry>();
        Ball ball;

        /// <summary>The human's energy for the scoreboard, 0..1 (-1 before it has started).</summary>
        public float HumanEnergy { get; private set; }

        void Start()
        {
            HumanEnergy = -1f;
            var d = GetComponent<DrillDirector>();
            if (d == null) return;
            ball = d.ball;
            var all = new List<Transform>(MatchRoster.Bodies(d, Side.Home));
            all.AddRange(MatchRoster.Bodies(d, Side.Away));
            foreach (Transform t in all)
            {
                var e = new Entry { t = t, atk = t.GetComponent<AttackerAI>(), def = t.GetComponent<DefenderAI>(), fc = t.GetComponent<FootballerController>(), last = t.position };
                e.attrs = e.atk != null ? e.atk.attrs : e.fc != null ? e.fc.attrs : null;
                if (e.attrs == null) continue;
                if (e.atk != null) { e.atkSpeed = e.atk.speed; e.atkSprint = e.atk.sprintSpeed; }
                if (e.def != null) { e.defSpeed = e.def.speed; e.defSprint = e.def.sprintSpeed; e.defJog = e.def.jogSpeed; }
                if (e.fc != null) { e.fcWalk = e.fc.walkSpeed; e.fcSprint = e.fc.sprintSpeed; }
                entries.Add(e);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.t == null) continue;
                Vector3 p = e.t.position;
                float v = new Vector2(p.x - e.last.x, p.z - e.last.z).magnitude / dt;
                e.last = p;
                if (v > 12f) v = 0f;   // a warp (restart), not running

                float top = e.fc != null ? e.fcSprint : e.atkSprint;
                float run = Mathf.Clamp01((v - 4f) / Mathf.Max(top - 4f, 0.1f));
                float st = e.attrs.stamina01 < 0f ? 0.5f : e.attrs.stamina01;
                float tire = Mathf.Lerp(staminaFactor.x, staminaFactor.y, st);
                e.attrs.energy = Mathf.Max(0f, e.attrs.energy - (restDrain + sprintDrain * run) * tire * dt);

                float tiredness = 1f - Mathf.Clamp01(e.attrs.energy / tiredBelow);
                float speed = Mathf.Lerp(1f, speedAtEmpty, tiredness);
                e.attrs.accuracy = Mathf.Lerp(1f, aimAtEmpty, tiredness);

                bool carrying = ball != null && ball.Carried && ball.CarrierTransform == e.t;
                if (carrying) speed *= e.attrs.CarryKeep;

                if (e.atk != null) { e.atk.speed = e.atkSpeed * speed; e.atk.sprintSpeed = e.atkSprint * speed; }
                if (e.def != null) { e.def.speed = e.defSpeed * speed; e.def.sprintSpeed = e.defSprint * speed; e.def.jogSpeed = e.defJog * speed; }
                if (e.fc != null) { e.fc.walkSpeed = e.fcWalk * speed; e.fc.sprintSpeed = e.fcSprint * speed; HumanEnergy = e.attrs.energy; }
            }
        }
    }
}
