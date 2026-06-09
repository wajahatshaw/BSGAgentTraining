using UnityEngine;

public class AndroidFullscreen : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Screen.fullScreen = true;
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;

        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
            AndroidJavaObject decorView = window.Call<AndroidJavaObject>("getDecorView");

            int flags =
                0x00000004 | // SYSTEM_UI_FLAG_FULLSCREEN
                0x00000002 | // SYSTEM_UI_FLAG_HIDE_NAVIGATION
                0x00001000 | // SYSTEM_UI_FLAG_IMMERSIVE
                0x00000200 | // SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                0x00000400 | // SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                0x00000100;  // SYSTEM_UI_FLAG_LAYOUT_STABLE

            decorView.Call("setSystemUiVisibility", flags);
        }
#endif
    }


    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            Start();
    }

}
