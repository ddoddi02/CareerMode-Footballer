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
    /// Thin input wrapper so the prototype works whether the project is set to
    /// the new Input System, the legacy manager, or both.
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
            v = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        /// <summary>Hold to free the head from the torso (shoulder check).</summary>
        public static bool ScanHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed) return true;
            var kb = Keyboard.current;
            if (kb != null && kb.leftShiftKey.isPressed) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.leftTrigger.ReadValue() > 0.35f) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1) || Input.GetKey(KeyCode.LeftShift);
#else
            return false;
#endif
        }

        /// <summary>Tap for an automatic quick sweep behind you.</summary>
        public static bool QuickScanPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.qKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.rightShoulder.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Q);
#else
            return false;
#endif
        }

        public static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            if (gp != null && gp.selectButton.wasPressedThisFrame) return true;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#else
            return false;
#endif
        }

        /// <summary>Tab toggles the vision mask off, for comparison / debugging.</summary>
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

        public static float focusAngle = 24f;      // full detail
        public static float peripheralAngle = 62f; // shape only
        public static float edgeAngle = 95f;       // motion only
        public static float maxDistance = 30f;
        public static float memorySeconds = 4.5f;
        public static float motionThreshold = 1.5f;
        public static float freeRadius = 2.0f;     // you always know what is at your feet
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
