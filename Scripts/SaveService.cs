using System;
using System.IO;
using System.Text;
using UnityEngine;

public enum SaveLoadStatus { Missing, Loaded, Recovered, Blocked }
public delegate bool SaveValidation(SaveData data, out string error);

// 本地单存档服务，不挂对象、不联网；只操作 persistentDataPath 下自己的 Progress 文件。
public sealed class SaveService
{
    private const long MaximumBytes = 1024 * 1024;
    private readonly string directory;
    public string PrimaryPath { get; }
    private readonly string backupPath;
    private readonly string temporaryPath;
    private string lastPayload;
    private bool preserveInvalidPrimary;
    private string invalidArchivePath;
    private bool needsPromotion;
    private bool writesBlocked;
    private bool writing;

    public SaveService(string persistentPath)
    {
        if (string.IsNullOrWhiteSpace(persistentPath))
            throw new ArgumentException("当前平台没有可用的持久存档目录。");
        directory = Path.Combine(persistentPath, "Progress");
        PrimaryPath = Path.Combine(directory, "save_v1.json");
        backupPath = Path.Combine(directory, "save_v1.backup.json");
        temporaryPath = Path.Combine(directory, "save_v1.tmp");
    }

    public SaveLoadStatus Load(SaveValidation validate, out SaveData data, out string message)
    {
        data = null; message = null;
        bool anyFile = false;
        string lastError = null;
        string[] candidates = { PrimaryPath, backupPath, temporaryPath };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!File.Exists(candidates[i])) continue;
            anyFile = true;
            if (TryRead(candidates[i], validate, out SaveData candidate, out string payload,
                    out string error, out bool unsupported))
            {
                data = candidate;
                lastPayload = payload;
                needsPromotion = i != 0;
                preserveInvalidPrimary = i != 0 && File.Exists(PrimaryPath);
                if (i == 0) return SaveLoadStatus.Loaded;
                message = i == 1 ? "主存档不可用，已读取上一份有效备份。" : "已读取首次写入留下的完整临时存档。";
                return SaveLoadStatus.Recovered;
            }
            lastError = error;
            if (unsupported) break; // 不用旧备份覆盖较新版本存档。
        }
        if (!anyFile) return SaveLoadStatus.Missing;
        writesBlocked = true;
        message = "没有兼容的有效存档，原文件已保留，本次不覆盖保存。原因：" + lastError;
        return SaveLoadStatus.Blocked;
    }

    private static bool TryRead(string path, SaveValidation validate, out SaveData data,
        out string payload, out string error, out bool unsupported)
    {
        data = null; payload = null; error = null; unsupported = false;
        try
        {
            long bytes = new FileInfo(path).Length;
            if (bytes <= 0 || bytes > MaximumBytes)
                throw new InvalidDataException("文件为空或超过存档大小上限。");
            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            SaveEnvelope envelope = JsonUtility.FromJson<SaveEnvelope>(json);
            if (envelope == null) throw new InvalidDataException("文件不是完整的存档。");
            if (envelope.version != 1)
            {
                unsupported = envelope.version > 1;
                throw new InvalidDataException("存档外层版本不支持。");
            }
            if (string.IsNullOrEmpty(envelope.payload) || envelope.sha256 == null ||
                envelope.sha256.Length != 64 ||
                !string.Equals(SaveValidator.Hash(envelope.payload), envelope.sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("存档校验值不符。");
            SaveData candidate = JsonUtility.FromJson<SaveData>(envelope.payload);
            if (candidate == null || candidate.version != 1)
            {
                unsupported = candidate != null && candidate.version > 1;
                throw new InvalidDataException("存档数据版本不支持。");
            }
            if (!validate(candidate, out string validationError))
                throw new InvalidDataException(validationError);
            data = candidate;
            payload = envelope.payload;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool Save(SaveData data, out string error)
    {
        error = null;
        if (writesBlocked || writing || data == null || data.version != 1)
        {
            error = writesBlocked ? "原存档不兼容，已禁止覆盖。" : "存档请求暂不可用。";
            return false;
        }
        writing = true;
        try
        {
            string payload = JsonUtility.ToJson(data);
            if (!needsPromotion && payload == lastPayload && File.Exists(PrimaryPath)) return true;
            string json = JsonUtility.ToJson(new SaveEnvelope {
                version = 1, payload = payload, sha256 = SaveValidator.Hash(payload) }, true);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("存档超过大小上限。");
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(PrimaryPath))
            {
                if (preserveInvalidPrimary)
                {
                    // 保留损坏原文件，修复时不把它覆盖到有效备份上。
                    if (invalidArchivePath == null)
                    {
                        string archived = Path.Combine(directory, "save_v1.invalid-" + Guid.NewGuid().ToString("N") + ".json");
                        File.Copy(PrimaryPath, archived, false);
                        invalidArchivePath = archived;
                    }
                    File.Replace(temporaryPath, PrimaryPath, null);
                }
                else
                    File.Replace(temporaryPath, PrimaryPath, backupPath);
            }
            else
                File.Move(temporaryPath, PrimaryPath);
            lastPayload = payload;
            needsPromotion = false;
            preserveInvalidPrimary = false;
            return true;
        }
        catch (Exception exception)
        {
            // 不使用非原子的直接覆盖降级；原文件/备份保留，下一次可重试。
            error = exception.Message;
            return false;
        }
        finally { writing = false; }
    }
}
