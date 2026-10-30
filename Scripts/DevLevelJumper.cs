using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Hidden developer tools. Nothing shows by default: tapping the "Settings" title 10 times opens a
// password keypad, and the right code (12344) unlocks a "DEV" button with level jump, tutorial,
// screenshot and a log viewer ("代码记录"). The unlock is remembered until "关闭开发者功能".
// Works in release builds too; Unity's own development console overlay is turned off in favour
// of the log page.
public sealed class DevLevelJumper : MonoBehaviour
{
    private const string UnlockKey = "Furrydoku.DevUnlocked";
    private const string Password = "12344";
    private const int MaxLogLines = 300;

    private static DevLevelJumper instance;
    private static readonly List<string> logLines = new List<string>();

    private GameController game;
    private bool unlocked, open, askingPassword, showingLog;
    private string input = "1", code = "", notice, codeNotice;
    private Vector2 logScroll;
    private GUIStyle button, label, field, small, logStyle, codeStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { instance = null; logLines.Clear(); }

    public static DevLevelJumper Create(GameController owner, int levelCount)
    {
        if (owner == null) return null;
        var jumper = owner.gameObject.AddComponent<DevLevelJumper>();
        jumper.game = owner;
        jumper.unlocked = PlayerPrefs.GetInt(UnlockKey, 0) == 1;
        instance = jumper;
        Debug.developerConsoleEnabled = false; // errors go to the hidden log page instead
        Application.logMessageReceived += Capture;
        return jumper;
    }

    // Called by the settings card after the 10th quiet tap on its title.
    public static void RequestUnlock()
    {
        if (instance == null) return;
        if (instance.unlocked) { instance.open = true; return; }
        instance.askingPassword = true; instance.code = ""; instance.codeNotice = null;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= Capture;
        if (instance == this) instance = null;
    }

    private static void Capture(string message, string stack, LogType type)
    {
        string tag = type == LogType.Log ? "" : type == LogType.Warning ? "[警告] " : "[错误] ";
        string line = System.DateTime.Now.ToString("HH:mm:ss ") + tag + message;
        if (type == LogType.Exception || type == LogType.Error)
        {
            string first = string.IsNullOrEmpty(stack) ? null : stack.Split('\n')[0];
            if (!string.IsNullOrEmpty(first)) line += "\n    " + first;
        }
        logLines.Add(line);
        if (logLines.Count > MaxLogLines) logLines.RemoveRange(0, logLines.Count - MaxLogLines);
    }

    private void OnGUI()
    {
        if (game == null) return;
        // Work in a 540-wide virtual screen so it is readable on phones and in the Game view.
        float scale = Mathf.Max(.5f, Screen.width / 540f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale, h = Screen.height / scale;
        EnsureStyles();
        GUI.depth = -100;

        if (askingPassword) { DrawKeypad(w, h); return; }
        if (!unlocked) return;
        if (!open)
        {
            if (GUI.Button(new Rect(10f, h - 160f, 84f, 46f), "DEV", button)) open = true;
            return;
        }
        if (showingLog) DrawLog(w, h); else DrawTools(w, h);
    }

    private void DrawKeypad(float w, float h)
    {
        float pw = Mathf.Min(420f, w - 40f), ph = 560f;
        Rect panel = new Rect((w - pw) * .5f, (h - ph) * .5f, pw, ph);
        GUI.Box(panel, GUIContent.none); GUI.Box(panel, GUIContent.none);
        GUI.Label(new Rect(panel.x, panel.y + 18f, pw, 40f), "请输入密码", codeStyle);
        string shown = new string('●', code.Length);
        GUI.Label(new Rect(panel.x, panel.y + 66f, pw, 50f), shown.Length > 0 ? shown : "—", codeStyle);
        if (!string.IsNullOrEmpty(codeNotice))
            GUI.Label(new Rect(panel.x, panel.y + 112f, pw, 30f), codeNotice, small);

        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "删除", "0", "确定" };
        float gap = 12f, bw = (pw - 40f - gap * 2f) / 3f, bh = 70f, top = panel.y + 150f;
        for (int i = 0; i < keys.Length; i++)
        {
            Rect r = new Rect(panel.x + 20f + (i % 3) * (bw + gap), top + (i / 3) * (bh + gap), bw, bh);
            if (!GUI.Button(r, keys[i], button)) continue;
            codeNotice = null;
            if (keys[i] == "删除") { if (code.Length > 0) code = code.Substring(0, code.Length - 1); }
            else if (keys[i] == "确定") CheckCode();
            else if (code.Length < 8) code += keys[i];
        }
        if (GUI.Button(new Rect(panel.x + 20f, panel.yMax - 62f, pw - 40f, 48f), "取消", button))
        { askingPassword = false; code = ""; }
    }

    private void CheckCode()
    {
        if (code == Password)
        {
            unlocked = true; open = true; askingPassword = false; code = "";
            PlayerPrefs.SetInt(UnlockKey, 1); PlayerPrefs.Save();
            notice = "开发者功能已开启";
        }
        else { code = ""; codeNotice = "密码错误"; }
    }

    private void DrawTools(float w, float h)
    {
        float pw = Mathf.Min(400f, w - 20f), ph = 420f;
        Rect panel = new Rect(10f, h - ph - 120f, pw, ph);
        GUI.Box(panel, GUIContent.none); GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 12f, panel.width - 28f, panel.height - 24f));
        GUILayout.BeginHorizontal();
        GUILayout.Label("开发者工具", label);
        if (GUILayout.Button("×", button, GUILayout.Width(56f), GUILayout.Height(44f))) open = false;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("新手关", button, GUILayout.Height(48f)))
        {
            game.DevStartTutorial();
            notice = "已进入新手教学";
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("跳转第", label, GUILayout.Width(80f));
        input = GUILayout.TextField(input, 3, field, GUILayout.Width(80f), GUILayout.Height(48f));
        GUILayout.Label("关", label, GUILayout.Width(34f));
        if (GUILayout.Button("确定", button, GUILayout.Height(48f)))
        {
            int count = game.CatalogCount;
            if (int.TryParse(input, out int number) && number >= 1 && number <= count)
                notice = game.DevJumpToLevel(number) ? $"已跳到第 {number} 关" : "跳关失败，见日志";
            else
                notice = $"请输入 1～{count}";
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        // Plain screenshot of the Game view (superSize does not re-render overlay UI, so keep 1x).
        if (GUILayout.Button("截图", button, GUILayout.Height(48f)))
        {
            string folder = System.IO.Path.Combine(Application.isEditor ? System.IO.Directory.GetCurrentDirectory()
                : Application.persistentDataPath, "Screenshots");
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, "shot_" + System.DateTime.Now.ToString("HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            notice = "截图已保存";
            Debug.Log("DevLevelJumper：截图 → " + file);
        }
        if (GUILayout.Button("日志 (" + logLines.Count + ")", button, GUILayout.Height(48f))) showingLog = true;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("关闭开发者功能", button, GUILayout.Height(48f)))
        {
            unlocked = false; open = false; showingLog = false;
            PlayerPrefs.SetInt(UnlockKey, 0); PlayerPrefs.Save();
        }
        if (!string.IsNullOrEmpty(notice)) GUILayout.Label(notice, label);
        GUILayout.EndArea();
    }

    private void DrawLog(float w, float h)
    {
        Rect panel = new Rect(10f, 70f, w - 20f, h - 200f);
        GUI.Box(panel, GUIContent.none); GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 24f, panel.height - 20f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("返回", button, GUILayout.Height(46f))) showingLog = false;
        if (GUILayout.Button("清空", button, GUILayout.Height(46f))) logLines.Clear();
        if (GUILayout.Button("复制", button, GUILayout.Height(46f)))
        {
            var all = new StringBuilder();
            foreach (string line in logLines) all.AppendLine(line);
            GUIUtility.systemCopyBuffer = all.ToString();
        }
        GUILayout.EndHorizontal();
        logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.ExpandHeight(true));
        if (logLines.Count == 0) GUILayout.Label("暂无记录", logStyle);
        for (int i = logLines.Count - 1; i >= 0; i--) GUILayout.Label(logLines[i], logStyle); // newest first
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (button != null) return;
        button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        label = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        label.normal.textColor = Color.white;
        field = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
        small = new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        small.normal.textColor = new Color(1f, .55f, .55f);
        codeStyle = new GUIStyle(label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
        logStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        logStyle.normal.textColor = new Color(.92f, .92f, .92f);
    }
}
