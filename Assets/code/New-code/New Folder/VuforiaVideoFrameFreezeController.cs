using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public static class VuforiaVideoFrameFreezeController
{
    public static IEnumerator FreezeFirstFrame(List<VideoPlayer> mainVideos, float seconds)
    {
        // Wait a frame so VideoPlayer initializes
        yield return null;

        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            // Pause immediately at start
            v.Pause();
        }

        yield return new WaitForSeconds(seconds);

        foreach (var v in mainVideos)
        {
            if (v == null) continue;
            v.Play();
        }
    }

    public static IEnumerator FreezeLastFrameThenStop(VideoPlayer v, float seconds)
    {
        if (v == null) yield break;

        v.Pause();
        yield return new WaitForSeconds(seconds);
        v.Stop();
    }
}
