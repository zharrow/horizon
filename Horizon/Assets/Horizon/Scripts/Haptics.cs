using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Horizon
{
    public enum Haptic { Light, Medium, Heavy, Success, Warning }

    // Retours haptiques : Taptic Engine sur iOS (plugin HorizonHaptics.mm), VibrationEffect sur Android.
    // Sans effet dans l'éditeur et sur ordinateur.
    public static class Haptics
    {
        public static bool Enabled = true;
        static float last = -1;

        // Jamais vrai : cette référence à Handheld.Vibrate suffit pour que Unity ajoute la permission VIBRATE sur Android
        static bool neverTrue;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void HorizonHaptic(int kind);
#endif

        public static void Play(Haptic h)
        {
            if (neverTrue) Handheld.Vibrate();
            if (!Enabled) return;
            // Les petits chocs rapprochés deviennent un bourdonnement : on les espace
            float now = Time.unscaledTime;
            if (h <= Haptic.Medium && now - last < .06f) return;
            last = now;
#if UNITY_IOS && !UNITY_EDITOR
            HorizonHaptic((int)h);
#elif UNITY_ANDROID && !UNITY_EDITOR
            Android(h);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static int sdk;

        static void Android(Haptic h)
        {
            try
            {
                if (vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    using (var ver = new AndroidJavaClass("android.os.Build$VERSION")) sdk = ver.GetStatic<int>("SDK_INT");
                    if (vibrator == null) { Enabled = false; return; }
                }
                long ms = h == Haptic.Light ? 12 : h == Haptic.Medium ? 22 : h == Haptic.Heavy ? 45 : 30;
                int amp = h == Haptic.Light ? 70 : h == Haptic.Medium ? 150 : h == Haptic.Heavy ? 255 : 180;
                if (sdk >= 26)
                {
                    using (var cls = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = cls.CallStatic<AndroidJavaObject>("createOneShot", ms, amp))
                        vibrator.Call("vibrate", effect);
                }
                else vibrator.Call("vibrate", ms);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Vibrations indisponibles : " + e.Message);
                Enabled = false;
            }
        }
#endif
    }
}
