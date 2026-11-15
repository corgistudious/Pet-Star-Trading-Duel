using PetStar.Client.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace PetStar.Client.Core
{
    /// <summary>Application root, created automatically after the bootstrap scene loads (no hand-authored scene
    /// content is required; the editor build script creates an empty Bootstrap scene).</summary>
    public sealed class App : MonoBehaviour
    {
        public static Session Session { get; private set; } = null!;
        public static ApiClient Api { get; private set; } = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<App>() != null) return;
            var go = new GameObject("PetStarApp");
            DontDestroyOnLoad(go);
            go.AddComponent<App>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
#if UNITY_ANDROID || UNITY_IOS
            // Portrait outside duels; Navigator switches to landscape only for PvE/PvP (official viewport rule).
            Screen.autorotateToPortrait = true; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false; Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
#endif
            L.Init();
            UiKit.LoadFonts();
            GameDataClient.Init();
            Audio.Init(gameObject);
            Session = new Session();
            Api = new ApiClient(Session);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            if (Camera.main == null)
            {
                var cam = new GameObject("MainCamera", typeof(Camera)).GetComponent<Camera>();
                cam.tag = "MainCamera"; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = UiKit.Sky; cam.orthographic = true;
                DontDestroyOnLoad(cam.gameObject);
            }
            var nav = Navigator.Create(transform);
            nav.Replace(new PetStar.Client.Screens.SplashScreen());
        }
    }
}
