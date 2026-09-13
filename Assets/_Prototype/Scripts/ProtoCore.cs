using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Prototype
{
    /// <summary>How well the viewer currently perceives a target.</summary>
    public enum SightState
    {
        Clear,      // in the focal cone - full detail
        Peripheral, // seen, but only shape / colour
        Unknown     // not perceived at all (memory ghost may still show)
    }

    public static class ProtoMat
    {
        public static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material UnlitFade(Color c)
        {
            var m = Unlit(c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }

    /// <summary>
    /// Mouse and keyboard, NOT the FC / FIFA convention any more.
    ///
    /// The old scheme copied FC so that anyone arriving from those games would not have
    /// to relearn anything. It was dropped because this game is not trying to be one:
    /// the thing being built is a player who can only see where he is looking, and on a
    /// pad the direction you look, the direction you run and the direction you pass all
    /// come out of the same two sticks. They fight each other. A mouse separates them -
    /// WASD is where you RUN, the cursor is where you LOOK, and where you look is what
    /// you can see, which is the whole game.
    ///
    ///   move    WASD        / left stick
    ///   look    mouse       / right stick      <- always live, no button. also what you can SEE
    ///   aim     mouse       / right stick      <- the same cursor: you pass where you are looking
    ///   pass    E           / A (south)
    ///   shoot   R           / B (east)
    ///   through F           / Y (north)
    ///   cross   C           / X (west)
    ///   scan    Q                              <- the short look behind (unchanged)
    ///   sprint  LeftShift   / RB
    ///   shield  Space       / LT
    ///   plant   LeftCtrl    / LB
    ///
    /// PLANT is now nearly redundant and is kept only as "stand still". It existed
    /// because one stick had to do both moving and aiming, so you needed a way to say
    /// "this is an aim, not a run". The mouse answers that by construction.
    ///
    /// Works with the new Input System, the legacy manager, or both.
    /// </summary>
    public static class ProtoInput
    {
        public static Vector2 Move()
        {
            Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                // Right hand moves, left hand plays. WASD belongs to the ball now.
                if (kb.wKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed) v.x -= 1f;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 ls = gp.leftStick.ReadValue();
                if (ls.sqrMagnitude > 0.04f) v = ls;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.W)) v.y += 1f;
            if (Input.GetKey(KeyCode.S)) v.y -= 1f;
            if (Input.GetKey(KeyCode.D)) v.x += 1f;
            if (Input.GetKey(KeyCode.A)) v.x -= 1f;
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        public static bool SprintHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.leftShiftKey.isPressed) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.isPressed) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift);
#else
            return false;
#endif
        }

        public static bool PassPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonSouth.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        /// <summary>Call for the ball to be played into the space ahead of your run.</summary>
        public static bool ThroughPassPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F);
#else
            return false;
#endif
        }

        /// <summary>A = cross. Gamepad square/X, as in FC.</summary>
        public static bool CrossPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonWest.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.C);
#else
            return false;
#endif
        }

        /// <summary>D = shoot. Gamepad circle/B, as in FC.</summary>
        public static bool ShootPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonEast.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#else
            return false;
#endif
        }

        /// <summary>Hold to put your body between the defender and the ball.</summary>
        public static bool ShieldHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.isPressed) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.leftTrigger.ReadValue() > 0.35f) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Space);
#else
            return false;
#endif
        }

        /// <summary>Hold to turn your head. Never affects the torso or the ball.</summary>
        public static bool ScanHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.qKey.isPressed) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Q);
#else
            return false;
#endif
        }

        public static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.backspaceKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.selectButton.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Backspace);
#else
            return false;
#endif
        }

        public static bool XrayPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.startButton.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Tab);
#else
            return false;
#endif
        }

        /// <summary>Show the numbers the AI is actually working to - ranges, lanes, cones.</summary>
        public static bool DebugViewPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightStickButton.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.G);
#else
            return false;
#endif
        }

        /// <summary>Plant your feet: the stick designates where you want the ball instead of moving you.</summary>
        public static bool HoldHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.leftShoulder.isPressed) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#else
            return false;
#endif
        }

        public static bool MouseAvailable()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return true;
#else
            return false;
#endif
        }

        public static Vector2 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null) return mouse.position.ReadValue();
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#endif
        }

        /// <summary>Freeze everybody and pass on command - the pass-selection bench (PassLab).</summary>
        public static bool PassLabPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.P);
#else
            return false;
#endif
        }

        /// <summary>Play the pass that is currently top of the bench's list.</summary>
        public static bool PassLabStepPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.enterKey.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return);
#else
            return false;
#endif
        }

        /// <summary>Put the ball back at the centre-back's feet without unfreezing anyone.</summary>
        public static bool PassLabResetPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.B);
#else
            return false;
#endif
        }

        public static Vector2 RightStick()
        {
#if ENABLE_INPUT_SYSTEM
            var gp = Gamepad.current;
            if (gp != null) return gp.rightStick.ReadValue();
            return Vector2.zero;
#else
            return Vector2.zero;
#endif
        }
    }

    /// <summary>
    /// Gameplay-side truth about what the viewer knows. Deliberately separate from
    /// the screen darkening: this is the data the simulation and AI should read.
    /// </summary>
    public static class PerceptionSystem
    {
        public static readonly List<Perceivable> All = new List<Perceivable>();

        public static float focusAngle = 26f;
        public static float peripheralAngle = 55f;
        public static float edgeAngle = 80f;
        public static float maxDistance = 30f;
        public static float memorySeconds = 4.5f;
        public static float motionThreshold = 1.5f;
        public static float freeRadius = 2.0f;
        public static bool xrayDebug = false;

        public static void Register(Perceivable p)
        {
            if (p != null && !All.Contains(p)) All.Add(p);
        }

        public static void Unregister(Perceivable p)
        {
            All.Remove(p);
        }

        public static SightState Evaluate(Vector3 eye, Vector2 fwd, Perceivable p)
        {
            if (xrayDebug) return SightState.Clear;

            Vector3 d3 = p.transform.position - eye;
            Vector2 d = new Vector2(d3.x, d3.z);
            float dist = d.magnitude;

            if (dist <= freeRadius) return SightState.Clear;
            if (dist > maxDistance) return SightState.Unknown;

            float ang = Vector2.Angle(fwd, d / dist);
            if (ang <= focusAngle) return SightState.Clear;
            if (ang <= peripheralAngle) return SightState.Peripheral;

            // At the very edge of vision you only register movement, never a still figure.
            if (ang <= edgeAngle && p.Speed > motionThreshold && dist < 14f) return SightState.Peripheral;

            return SightState.Unknown;
        }
    }
}
