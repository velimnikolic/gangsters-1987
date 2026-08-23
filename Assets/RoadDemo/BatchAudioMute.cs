using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Headless (batchmode) runs never want sound. The soak plays dozens of them back to
    /// back, and the street's ambience blaring for the first second of every one is only
    /// noise on the machine running it. The harness already mutes the listener once its
    /// driver is up (PlayHarness.Spare), but that is a second or two into the scene -
    /// after the beds have started - so each run still opened with a blast.
    ///
    /// This kills the sound from frame zero, before any scene audio is created, and only
    /// ever in batchmode: a real editor Play session is untouched.
    /// </summary>
    static class BatchAudioMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Mute()
        {
            if (!Application.isBatchMode) return;
            AudioListener.volume = 0f;
            AudioListener.pause = true;
        }
    }
}
