using UnityEngine;
using UnityEngine.SceneManagement;

namespace Career
{
    /// <summary>
    /// What the career keeps while a scene changes: the season, the daily life and the match that
    /// was handed to the match scene. Static, so the daily map scene can be loaded again and pick
    /// up where it was (a match played "by hand" leaves the scene and comes back).
    /// </summary>
    public static class CareerSession
    {
        public static Season season;
        public static DailyLife life;
        public static bool savedPlayer;
        /// <summary>The fixture waiting for the match scene's result.</summary>
        public static Fixture pendingFixture;

        public static bool Active { get { return life != null && season != null; } }

        public static void Clear()
        {
            season = null;
            life = null;
            pendingFixture = null;
            savedPlayer = false;
        }

        /// <summary>
        /// Loads a scene by its asset path. Scenes outside the build settings load in the editor
        /// only (the build settings are not touched, PROJECT.md 10.14).
        /// </summary>
        public static void Open(string scenePath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            if (Application.CanStreamedLevelBeLoaded(name)) { SceneManager.LoadScene(name); return; }
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            Debug.LogWarning("[CareerSession] scene not in the build settings: " + scenePath);
        }
    }
}
