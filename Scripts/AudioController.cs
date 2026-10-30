using UnityEngine;

// 各通道独立；不更改全局AudioListener，不在恢复旧结果时播放胜负声。
public class AudioController : MonoBehaviour
{
    public static AudioController Current { get; private set; }
    public GameSettings Settings { get; private set; }
    private AudioLibrary library;
    private AudioSource soundSource, musicSource, voiceSource;
    private readonly float[] lastSound = new float[9];
    private float lastVibration = -100f;
    private bool focused = true, paused, initialized, musicPaused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { Current = null; }

    public static AudioController Create(Transform parent, AudioLibrary preferred)
    {
        if (Current != null) return Current;
        var go = new GameObject("GameAudio");
        go.transform.SetParent(parent, false);
        var audio = go.AddComponent<AudioController>();
        audio.library = preferred != null ? preferred : Resources.Load<AudioLibrary>("GameAudioLibrary");
        audio.Settings = new GameSettings();
        audio.soundSource = audio.MakeSource("SoundEffects");
        audio.musicSource = audio.MakeSource("Music");
        audio.voiceSource = audio.MakeSource("Voice");
        audio.musicSource.loop = true;
        audio.musicSource.clip = audio.library != null ? audio.library.musicLoop : null;
        for (int i = 0; i < audio.lastSound.Length; i++) audio.lastSound[i] = -100f;
        audio.focused = Application.isFocused;
        audio.initialized = true;
        Current = audio;
        audio.ApplySettings();
        if (audio.library == null) Debug.LogWarning("AudioController：缺少GameAudioLibrary，静音运行，玩法不受影响。");
        return audio;
    }
    private AudioSource MakeSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }
    private bool MayPlay => initialized && isActiveAndEnabled && focused && !paused && Application.isPlaying;
    public void ApplySettings()
    {
        if (!initialized) return;
        soundSource.volume = library != null ? library.soundVolume : .45f;
        musicSource.volume = library != null ? library.musicVolume : .12f;
        voiceSource.volume = library != null ? library.voiceVolume : .65f;
        if (!Settings.Get(GameSetting.Sound)) soundSource.Stop();
        if (!Settings.Get(GameSetting.Voice)) voiceSource.Stop();
        if (!MayPlay || !Settings.Get(GameSetting.Music))
        {
            if (musicSource.isPlaying) { musicSource.Pause(); musicPaused = true; }
        }
        else if (musicSource.clip != null && !musicSource.isPlaying)
        {
            if (musicPaused) musicSource.UnPause();
            else musicSource.Play();
            musicPaused = false;
        }
    }
    public void PlaySound(GameSound sound)
    {
        if (!MayPlay || library == null || !Settings.Get(GameSetting.Sound)) return;
        int i = (int)sound;
        float now = Time.unscaledTime;
        float interval = sound == GameSound.MarkOn || sound == GameSound.MarkOff ? .045f : .06f;
        if (now - lastSound[i] < interval) return;
        AudioClip clip = library.Sound(sound);
        if (clip == null) return;
        lastSound[i] = now;
        soundSource.PlayOneShot(clip);
    }
    // Extra clips loaded by name from Resources/GameSfx (no AudioLibrary asset edits needed):
    // press = soft 'jelly' pop on every board touch; correct_01..08 = rising success chimes.
    private AudioClip pressClip;
    private AudioClip[] correctSteps;
    private bool extrasLoaded;
    private float lastPress = -100f;
    private const float PressVolume = .75f;   // board-touch pop sits a little under the other effects (~-2.5 dB)
    private void LoadExtras()
    {
        if (extrasLoaded) return;
        extrasLoaded = true;
        pressClip = Resources.Load<AudioClip>("GameSfx/press");
        var steps = new System.Collections.Generic.List<AudioClip>(8);
        for (int i = 1; i <= 8; i++)
        {
            AudioClip clip = Resources.Load<AudioClip>("GameSfx/correct_" + i.ToString("00"));
            if (clip == null) break;
            steps.Add(clip);
        }
        correctSteps = steps.ToArray();
    }
    public void PlayPress()
    {
        if (!MayPlay || Settings == null || !Settings.Get(GameSetting.Sound)) return;
        LoadExtras();
        if (pressClip == null) return;
        float now = Time.unscaledTime;
        if (now - lastPress < .04f) return;
        lastPress = now;
        soundSource.PlayOneShot(pressClip, PressVolume);
    }
    // placedCount = animals placed so far this round (1 = first). Steps climb with
    // each placement and hold on the top step once there are more animals than steps.
    public void PlayCorrectStep(int placedCount)
    {
        LoadExtras();
        if (correctSteps == null || correctSteps.Length == 0) { PlaySound(GameSound.Correct); return; }
        if (!MayPlay || Settings == null || !Settings.Get(GameSetting.Sound)) return;
        int step = Mathf.Clamp(placedCount, 1, correctSteps.Length) - 1;
        soundSource.PlayOneShot(correctSteps[step]);
    }
    // Announcer praise (Resources/GameVoice/tierNN_a|b): tiers 3..10, rising excitement.
    private AudioClip[][] announcer;
    private void LoadAnnouncer()
    {
        if (announcer != null) return;
        announcer = new AudioClip[8][];
        for (int t = 0; t < 8; t++)
        {
            var list = new System.Collections.Generic.List<AudioClip>(2);
            foreach (string slot in new[] { "a", "b" })
            {
                AudioClip clip = Resources.Load<AudioClip>("GameVoice/tier" + (t + 3).ToString("00") + "_" + slot);
                if (clip != null) list.Add(clip);
            }
            announcer[t] = list.ToArray();
        }
    }
    // placed = animals placed so far (1-based), total = animals on this board.
    // No line for the first two. The last animal always gets the top tier; on a
    // 10-animal board this is exactly one tier per animal.
    public void PlayAnnouncer(int placed, int total)
    {
        if (placed < 3 || !MayPlay || Settings == null || !Settings.Get(GameSetting.Voice)) return;
        LoadAnnouncer();
        int tiers = announcer.Length;
        int tier = total <= 3 ? tiers - 1
            : Mathf.Clamp(Mathf.RoundToInt((placed - 3) * (tiers - 1) / (float)(total - 3)), 0, tiers - 1);
        AudioClip[] options = announcer[tier];
        if (options.Length == 0) return;
        voiceSource.Stop();
        voiceSource.clip = options[Random.Range(0, options.Length)];
        voiceSource.Play();
    }
    // One-shot extra effect by Resources path (e.g. "GameSfx/level_start"); false if missing.
    private readonly System.Collections.Generic.Dictionary<string, AudioClip> extraClips =
        new System.Collections.Generic.Dictionary<string, AudioClip>();
    private readonly System.Collections.Generic.Dictionary<string, float> extraLast =
        new System.Collections.Generic.Dictionary<string, float>();
    public bool PlayExtra(string path)
    {
        if (!extraClips.TryGetValue(path, out AudioClip clip))
            extraClips[path] = clip = Resources.Load<AudioClip>(path);
        if (clip == null) return false;
        if (!MayPlay || Settings == null || !Settings.Get(GameSetting.Sound)) return true;
        float now = Time.unscaledTime;
        if (extraLast.TryGetValue(path, out float last) && now - last < .06f) return true;
        extraLast[path] = now;
        soundSource.PlayOneShot(clip);
        return true;
    }
    public void PlayVoice(GameVoice voice)
    {
        if (!MayPlay || library == null || !Settings.Get(GameSetting.Voice)) return;
        AudioClip clip = library.Voice(voice);
        if (clip == null) return;
        voiceSource.Stop(); // 新提示替换旧人声，避免几句话叠加。
        voiceSource.clip = clip;
        voiceSource.Play();
    }
    // Light tick on every board touch (the only haptic in the game). Android: a 30 ms one-shot at
    // firm amplitude through the system Vibrator (API 26+), short enough to stay a tap. Other platforms have
    // no light vibration in Unity, so they stay silent rather than buzz on every tap.
    private float lastTap = -100f;
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject vibrator;
    private bool vibratorTried, vibratorAmplitude;
#endif
    public void TapHaptic()
    {
        if (!MayPlay || !Application.isMobilePlatform || Settings == null || !Settings.Get(GameSetting.Vibration)) return;
        if (Time.unscaledTime - lastTap < .05f) return;
        lastTap = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (!vibratorTried)
            {
                vibratorTried = true;
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                vibratorAmplitude = vibrator != null && vibrator.Call<bool>("hasAmplitudeControl");
            }
            if (vibrator == null) return;
            using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
            using (var tick = effects.CallStatic<AndroidJavaObject>("createOneShot", 30L, vibratorAmplitude ? 180 : -1))
                vibrator.Call("vibrate", tick);
        }
        catch (System.Exception) { vibrator = null; } // never let haptics break input
#endif
    }

    // Unused since taps got the light tick; kept so the build still declares the VIBRATE permission
    // (Unity adds it when Handheld.Vibrate is referenced), which the light tick needs too.
    public void Vibrate()
    {
        if (!MayPlay || !Application.isMobilePlatform || !Settings.Get(GameSetting.Vibration) ||
            Time.unscaledTime - lastVibration < .2f) return;
        lastVibration = Time.unscaledTime;
        Handheld.Vibrate(); // 系统振动；强度/长短依设备，编辑器不震动。
    }
    // Android sometimes does not resume the music after the app comes back from the background
    // (or another app took audio focus). Once a second, restart it if it should be playing.
    private float nextMusicCheck;
    private void Update()
    {
        if (!initialized || Time.unscaledTime < nextMusicCheck) return;
        nextMusicCheck = Time.unscaledTime + 1f;
        if (MayPlay && Settings != null && Settings.Get(GameSetting.Music) && musicSource.clip != null && !musicSource.isPlaying)
        {
            musicSource.Play();
            musicPaused = false;
        }
    }
    private void Suspend()
    {
        if (!initialized) return;
        soundSource.Stop();
        voiceSource.Stop();
        ApplySettings();
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value) Suspend(); else ApplySettings(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) Suspend(); else ApplySettings(); }
    private void OnDisable()
    {
        if (!initialized) return;
        soundSource.Stop(); voiceSource.Stop();
        if (musicSource.isPlaying) { musicSource.Pause(); musicPaused = true; }
    }
    private void OnEnable() { if (initialized) ApplySettings(); }
    private void OnDestroy() { if (Current == this) Current = null; }
}
