using Prototype;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// The placeholder figure on the creation screen: a capsule body in the kit colour, a head
    /// in the skin colour, a hair shape, turning slowly. Its own camera draws it into a render
    /// texture that the screen shows, so the screen layout does not depend on where it stands.
    /// Height and build scale it; nothing here feeds back into the numbers.
    /// </summary>
    public class AvatarPreview : MonoBehaviour
    {
        public float turnSpeed = 30f;
        public RenderTexture Texture { get; private set; }

        Transform figure, body, head, hair;
        Material mKit, mSkin, mHair, mShorts;
        Camera cam;

        const float BaseHeight = 1.80f;

        void Awake()
        {
            // The shape of the box the creation screen shows it in (about 612 x 400 at 1920 x 1080).
            Texture = new RenderTexture(1224, 800, 24) { name = "AvatarPreview" };

            mKit = ProtoMat.Lit(Color.white);
            mSkin = ProtoMat.Lit(Color.white);
            mHair = ProtoMat.Lit(Color.black);
            mShorts = ProtoMat.Lit(new Color(0.12f, 0.12f, 0.14f));

            figure = new GameObject("Figure").transform;
            figure.SetParent(transform, false);
            body = Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 1.15f, 0f), new Vector3(0.50f, 0.42f, 0.32f), mKit);
            Part(PrimitiveType.Capsule, "Legs", new Vector3(0f, 0.45f, 0f), new Vector3(0.40f, 0.45f, 0.26f), mShorts);
            head = Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 1.68f, 0f), new Vector3(0.24f, 0.27f, 0.25f), mSkin);
            Part(PrimitiveType.Cube, "Nose", new Vector3(0f, 1.69f, 0.12f), new Vector3(0.04f, 0.05f, 0.05f), mSkin);
            hair = Part(PrimitiveType.Sphere, "Hair", new Vector3(0f, 1.74f, -0.01f), new Vector3(0.26f, 0.20f, 0.27f), mHair);

            var camGo = new GameObject("AvatarCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.15f, 4.2f);
            camGo.transform.LookAt(transform.position + new Vector3(0f, 1.0f, 0f));
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.16f);
            cam.targetTexture = Texture;

            var lightGo = new GameObject("AvatarLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
        }

        Transform Part(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(figure, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        void Update()
        {
            figure.Rotate(0f, turnSpeed * Time.deltaTime, 0f, Space.Self);
        }

        public void Show(CharacterDraft d)
        {
            Appearance a = d.appearance;
            float h = d.heightCm / 100f / BaseHeight;
            // Wider with body-mass index, 1 at the default 15-year-old (60 kg at 170 cm, 20.8).
            float hm = d.heightCm / 100f;
            float w = Mathf.Clamp(Mathf.Sqrt(d.weightKg / (hm * hm) / 20.8f), 0.85f, 1.3f);
            figure.localScale = new Vector3(w, h, w);
            // the head does not get wider with the build
            head.localScale = new Vector3(0.24f / w, 0.27f, 0.25f / w);

            mKit.SetColor("_BaseColor", Appearance.Pick(Appearance.KitColors, a.kitColor));
            mSkin.SetColor("_BaseColor", Appearance.Pick(Appearance.Skins, a.skin));
            mHair.SetColor("_BaseColor", Appearance.Pick(Appearance.HairColors, a.hairColor));

            // 0 shaved, 1 short, 2 medium, 3 long
            hair.gameObject.SetActive(a.hairStyle > 0);
            switch (Mathf.Clamp(a.hairStyle, 0, 3))
            {
                case 1: hair.localPosition = new Vector3(0f, 1.76f, -0.01f); hair.localScale = new Vector3(0.26f / w, 0.16f, 0.27f / w); break;
                case 2: hair.localPosition = new Vector3(0f, 1.73f, -0.03f); hair.localScale = new Vector3(0.28f / w, 0.24f, 0.30f / w); break;
                case 3: hair.localPosition = new Vector3(0f, 1.62f, -0.06f); hair.localScale = new Vector3(0.29f / w, 0.42f, 0.30f / w); break;
            }
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
