using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YeShunguangPet;

public sealed record FocusCompletion(string Id, DateTimeOffset CompletedAt, int DurationSeconds)
{
    [JsonIgnore] public DateOnly Day => DateOnly.FromDateTime(CompletedAt.DateTime);
    [JsonIgnore] public int Minutes => DurationSeconds / 60;
}
public sealed record StudyDay(DateOnly Date, int Minutes, int Sessions);

public sealed class StudyHistory
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private const int MaxRecords = 20000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string? _path;
    private Document _document = new();
    private bool _loadFailed, _dirty;
    public string? LastError { get; private set; }
    public int DailyGoalMinutes => _document.DailyGoalMinutes;
    public bool HasPendingChanges => _dirty;
    public IReadOnlyList<FocusCompletion> Records => _document.Records.ToArray();
    public event Action? Changed;

    public StudyHistory(string? path = null)
    {
        _path = path is null ? null : Path.GetFullPath(path);
        try { _document = Read(); }
        catch (Exception ex) { _loadFailed = true; LastError = "学习记录无法读取，原文件未修改：" + ex.Message; AppLogger.Error("Could not load study history.", ex); }
    }

    public bool Record(FocusCompletion completion)
    {
        try { ValidateRecord(completion); }
        catch (InvalidDataException ex)
        {
            LastError = "本次专注无法记录：" + ex.Message;
            AppLogger.Error("Could not record focus completion.", ex);
            Changed?.Invoke();
            return false;
        }
        if (_document.Records.Any(r => r.Id == completion.Id)) return false;
        if (_document.Records.Count >= MaxRecords)
        {
            LastError = "学习记录已达到容量上限，本次记录未加入。";
            Changed?.Invoke();
            return false;
        }
        _document.Records.Add(completion);
        _dirty = true;
        Flush();
        Changed?.Invoke();
        return true;
    }

    public void SetGoal(int minutes)
    {
        if (minutes is < 1 or > 720) throw new InvalidDataException("每日目标需为 1-720 分钟。");
        if (_loadFailed) throw new InvalidDataException("请先恢复学习记录文件，再修改目标。");
        var previous = _document.DailyGoalMinutes;
        _document.DailyGoalMinutes = minutes;
        _dirty = true;
        if (!Flush())
        {
            _document.DailyGoalMinutes = previous;
            Changed?.Invoke();
            throw new IOException(LastError);
        }
        Changed?.Invoke();
    }

    public bool RetrySave()
    {
        if (_loadFailed)
        {
            try
            {
                var restored = Read();
                var pending = _document.Records;
                var existing = restored.Records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                restored.Records.AddRange(pending.Where(r => existing.Add(r.Id)));
                Validate(restored);
                _document = restored;
                _loadFailed = false;
                _dirty = pending.Count > 0;
                LastError = null;
            }
            catch (Exception ex) { LastError = "学习记录仍无法读取：" + ex.Message; Changed?.Invoke(); return false; }
        }
        var success = Flush();
        Changed?.Invoke();
        return success;
    }

    public bool Flush()
    {
        if (_loadFailed) return false;
        if (!_dirty) return LastError is null;
        if (_path is null) { _dirty = false; LastError = null; return true; }
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Validate(_document);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_document, JsonOptions);
            if (bytes.Length > MaxBytes) throw new InvalidDataException("学习记录超出容量限制。");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
            else File.Move(temporary, _path);
            _dirty = false;
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = "学习记录未保存，新增记录仅保留在本次运行中：" + ex.Message; AppLogger.Error("Could not persist study history.", ex); return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) { AppLogger.Error("Could not clean study history staging file.", ex); } }
    }

    public StudyDay Totals(DateOnly day)
    {
        var minutes = 0;
        var sessions = 0;
        foreach (var record in _document.Records)
            if (record.Day == day) { minutes += record.Minutes; sessions++; }
        return new StudyDay(day, minutes, sessions);
    }
    public StudyDay[] Week(DateOnly through) => Enumerable.Range(0, 7).Select(i => Totals(through.AddDays(i - 6))).ToArray();

    private Document Read()
    {
        if (_path is null || !File.Exists(_path)) return new Document();
        using var stream = File.OpenRead(_path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("学习记录文件超出容量限制。");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) bytes = bytes[3..];
        using var json = JsonDocument.Parse(bytes);
        if (!json.RootElement.TryGetProperty("SchemaVersion", out var version) || !version.TryGetInt32(out var n) || n != 1)
            throw new InvalidDataException("学习记录版本不受支持。");
        var document = JsonSerializer.Deserialize<Document>(bytes, JsonOptions) ?? throw new InvalidDataException("学习记录为空。");
        Validate(document);
        return document;
    }
    private static void Validate(Document document)
    {
        if (document.SchemaVersion != 1 || document.DailyGoalMinutes is < 1 or > 720 || document.Records is null || document.Records.Count > MaxRecords)
            throw new InvalidDataException("学习记录内容无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in document.Records) { ValidateRecord(record); if (!ids.Add(record.Id)) throw new InvalidDataException("学习记录编号重复。"); }
    }
    private static void ValidateRecord(FocusCompletion record)
    {
        if (record is null || !Guid.TryParseExact(record.Id, "N", out _) || record.DurationSeconds is < 60 or > 7200 || record.DurationSeconds % 60 != 0 || record.CompletedAt.Year is < 2000 or > 9998)
            throw new InvalidDataException("专注记录无效。");
    }
    private sealed class Document
    {
        public int SchemaVersion { get; set; } = 1;
        public int DailyGoalMinutes { get; set; } = 60;
        public List<FocusCompletion> Records { get; set; } = new();
    }
}
