using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The victory/failure puppy animations are ~330 frames. Loading them all while the first level
// is built froze the very first frames (black screen, loading bar already full when it appeared).
// Now the result views only take each clip's first frame at startup; the rest streams in with
// Resources.LoadAsync while the loading screen plays. Get() returns the cached clip, or loads it
// on the spot if the stream has not reached it yet. Frames are named <clip>/<name>_000, _001 ...
public static class DogFrameLibrary
{
    public static readonly string[] Clips =
    {
        "VictoryDogV5/intro", "VictoryDogV5/loop", "VictoryDogV6/intro", "VictoryDogV6/loop",
        "FailureDogV2/intro", "FailureDogV2/loop",
    };
    private static readonly Dictionary<string, Texture2D[]> cache = new Dictionary<string, Texture2D[]>();
    private static bool preloading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { cache.Clear(); preloading = false; }

    private static string FrameName(string clip, int index)
    {
        string prefix = clip.Substring(clip.LastIndexOf('/') + 1);
        return clip + "/" + prefix + "_" + index.ToString("000");
    }

    public static Texture2D First(string clip) => Resources.Load<Texture2D>(FrameName(clip, 0));

    public static Texture2D[] Get(string clip)
    {
        if (cache.TryGetValue(clip, out Texture2D[] frames) && frames != null) return frames;
        frames = Resources.LoadAll<Texture2D>(clip);
        if (frames == null || frames.Length == 0) return null;
        System.Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));
        cache[clip] = frames;
        return frames;
    }

    // Stream every clip in the background; a few frames per rendered frame keeps the UI moving.
    public static void Preload(MonoBehaviour host)
    {
        if (preloading || host == null) return;
        preloading = true;
        host.StartCoroutine(PreloadAll());
    }

    private static IEnumerator PreloadAll()
    {
        foreach (string clip in Clips)
        {
            if (cache.ContainsKey(clip)) continue;
            var frames = new List<Texture2D>(96);
            for (int i = 0; ; i++)
            {
                ResourceRequest request = Resources.LoadAsync<Texture2D>(FrameName(clip, i));
                yield return request;
                if (cache.ContainsKey(clip)) break;          // Get() loaded it meanwhile
                if (!(request.asset is Texture2D frame)) break;
                frames.Add(frame);
            }
            if (!cache.ContainsKey(clip) && frames.Count > 0) cache[clip] = frames.ToArray();
        }
    }
}
