using System;
using UnityEngine;

public enum GameSetting { Sound, Music, Voice, Vibration, Clarity }

// 设置与关卡进度分开保存，不升级/覆盖进度JSON。
public sealed class GameSettings
{
    // Clarity (colour-assist centre icons) is opt-in; the rest default on.
    private readonly bool[] enabled = { true, true, true, true, false };
    private const string Prefix = "PetPuzzle.settings.v1.";
    public bool Get(GameSetting setting) => enabled[(int)setting];
    public GameSettings()
    {
        foreach (GameSetting setting in Enum.GetValues(typeof(GameSetting)))
            enabled[(int)setting] = PlayerPrefs.GetInt(Prefix + setting, enabled[(int)setting] ? 1 : 0) != 0;
    }
    public bool Toggle(GameSetting setting)
    {
        enabled[(int)setting] = !Get(setting);
        try
        {
            PlayerPrefs.SetInt(Prefix + setting, Get(setting) ? 1 : 0);
            PlayerPrefs.Save();
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("GameSettings：设置本次生效，但保存失败：" + error.Message);
            return false;
        }
    }
}
