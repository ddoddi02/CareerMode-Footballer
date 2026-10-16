using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace Career.EditorTools
{
    /// <summary>
    /// Builds the character creation scene from code, like every other scene here (PROJECT.md 4).
    /// It lays out only what has to exist before play: a camera to clear the screen, the event
    /// system, the avatar rig and the screen component. The screen builds its own UI when it
    /// starts (CharacterCreationScreen), so the layout lives in one place, in code.
    ///
    /// Menu: Tools > Career > Build Character Creation Scene
    /// </summary>
    public static class CharacterCreationBuilder
    {
        const string SceneDir = "Assets/Career/Scenes";
        const string ScenePath = SceneDir + "/CharacterCreation.unity";

        [MenuItem("Tools/Career/Build Character Creation Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder("Assets/Career", "Scenes");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.60f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            // The UI is an overlay; this camera only clears behind it.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f);
            cam.cullingMask = 0;
            camGo.AddComponent<AudioListener>();

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // Far from the origin so nothing else is ever in its picture.
            var avatarGo = new GameObject("AvatarRig");
            avatarGo.transform.position = new Vector3(500f, 0f, 0f);
            var avatar = avatarGo.AddComponent<AvatarPreview>();

            var screen = new GameObject("CharacterCreation").AddComponent<CharacterCreationScreen>();
            screen.avatar = avatar;

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            Debug.Log("[CharacterCreationBuilder] built " + ScenePath);
        }
    }
}
