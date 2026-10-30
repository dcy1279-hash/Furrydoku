using UnityEngine;

public enum GameSound { UiClick, MarkOn, MarkOff, Correct, Error, Hint, Good, Win, Lose }
public enum GameVoice { Correct, Good, Win, Lose }

[CreateAssetMenu(menuName = "PetPuzzle/Audio Library（音频配置）")]
public class AudioLibrary : ScriptableObject
{
    [Header("Sound Effects（音效；可替换 AudioClip 音频片段）")]
    public AudioClip uiClick, markOn, markOff, correct, error, hint, good, win, lose;
    [Header("Music（循环音乐）")]
    public AudioClip musicLoop;
    [Header("Voice（人声；原型为合成人声）")]
    public AudioClip voiceCorrect, voiceGood, voiceWin, voiceLose;
    [Range(0f, 1f)] public float soundVolume = .45f;
    [Range(0f, 1f)] public float musicVolume = .12f;
    [Range(0f, 1f)] public float voiceVolume = .65f;
    public AudioClip Sound(GameSound sound)
    {
        switch (sound)
        {
            case GameSound.UiClick: return uiClick;
            case GameSound.MarkOn: return markOn;
            case GameSound.MarkOff: return markOff;
            case GameSound.Correct: return correct;
            case GameSound.Error: return error;
            case GameSound.Hint: return hint;
            case GameSound.Good: return good;
            case GameSound.Win: return win;
            default: return lose;
        }
    }
    public AudioClip Voice(GameVoice voice)
    {
        switch (voice)
        {
            case GameVoice.Correct: return voiceCorrect;
            case GameVoice.Good: return voiceGood;
            case GameVoice.Win: return voiceWin;
            default: return voiceLose;
        }
    }
}
