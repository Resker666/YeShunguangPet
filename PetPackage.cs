using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

public sealed class PetManifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SpriteSheet { get; set; } = "spritesheet.png";
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public Dictionary<PetState, AnimationDefinition> Animations { get; set; } = new();
    public List<FrameLocation> LookDirections { get; set; } = new();
}

public sealed class AnimationDefinition
{
    public int Row { get; set; }
    public int StartColumn { get; set; }
    public int[] DurationsMs { get; set; } = Array.Empty<int>();
    public bool Loop { get; set; }
}

public sealed record FrameLocation(int Row, int Column);

public sealed class PetPackage
{
    public const string DefaultId = "ye-shunguang";
    public const int LookDirectionCount = 16;
    internal const int MaxSpriteBytes = 64 * 1024 * 1024;
    internal const int MaxManifestBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<PetState>(JsonNamingPolicy.CamelCase, false) }
    };

    private readonly Dictionary<PetState, PetAnimation> _animations;
    public PetManifest Manifest { get; }
    public BitmapSource SpriteSheet { get; }
    public PetState[] RandomActions { get; }
    public bool CanRoam => Supports(PetState.RunningLeft) && Supports(PetState.RunningRight);
    public bool CanLook => Manifest.LookDirections.Count == LookDirectionCount;

    private PetPackage(PetManifest manifest, BitmapSource bitmap)
    {
        Manifest = manifest;
        SpriteSheet = bitmap;
        _animations = manifest.Animations.ToDictionary(x => x.Key,
            x => new PetAnimation(x.Key, x.Value.Row, x.Value.StartColumn, x.Value.DurationsMs, x.Value.Loop));
        RandomActions = new[] { PetState.Waving, PetState.Jumping }.Where(Supports).ToArray();
    }

    public bool Supports(PetState state) => _animations.ContainsKey(state);
    public PetAnimation GetAnimation(PetState state) => _animations.GetValueOrDefault(state) ?? _animations[PetState.Idle];

    public BitmapSource GetFrame(int row, int column)
    {
        var frame = new CroppedBitmap(SpriteSheet, new Int32Rect(column * Manifest.CellWidth,
            row * Manifest.CellHeight, Manifest.CellWidth, Manifest.CellHeight));
        frame.Freeze();
        return frame;
    }

    public BitmapSource Preview
    {
        get
        {
            var idle = GetAnimation(PetState.Idle);
            return GetFrame(idle.Row, idle.StartColumn);
        }
    }

    public static PetPackage Load(string manifestPath)
    {
        return ReadPackage(manifestPath).Package;
    }

    internal static byte[] SerializeManifest(PetManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest,
        new JsonSerializerOptions(JsonOptions) { WriteIndented = true });

    internal static PetPackage WithManifest(byte[] json, BitmapSource sprite)
    {
        var manifest = ReadManifest(json);
        if (sprite.PixelWidth != manifest.CellWidth * manifest.Columns || sprite.PixelHeight != manifest.CellHeight * manifest.Rows)
            throw new InvalidDataException("PNG 尺寸与单元格、行列数不一致。");
        return new PetPackage(manifest, sprite);
    }

    internal static (PetPackage Package, byte[] Json, byte[] Png) ReadPackage(string manifestPath)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var directory = Path.GetDirectoryName(manifestPath)!;
        if (!string.Equals(Path.GetFileName(manifestPath), "pet.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择皮肤目录中的 pet.json。");
        RejectLink(directory);
        var json = ReadLimitedFile(manifestPath, MaxManifestBytes);
        var manifest = ReadManifest(json);
        var png = ReadLimitedFile(Path.Combine(directory, manifest.SpriteSheet), MaxSpriteBytes);
        return (Decode(manifest, png), json, png);
    }

    internal static PetManifest ReadManifest(byte[] json)
    {
        if (json.Length == 0 || json.Length > MaxManifestBytes)
            throw new InvalidDataException("pet.json 超出大小限制。");
        if (json.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) json = json[3..];
        PetManifest manifest;
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("animations", out var actions) &&
                actions.ValueKind == JsonValueKind.Object)
            {
                foreach (var action in actions.EnumerateObject())
                    if (!Enum.TryParse<PetState>(action.Name, true, out var state) ||
                        !Enum.IsDefined(state) || JsonNamingPolicy.CamelCase.ConvertName(state.ToString()) != action.Name)
                        throw new InvalidDataException($"未知动作键：{action.Name}");
            }
            manifest = JsonSerializer.Deserialize<PetManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("pet.json 不能为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"pet.json 格式错误：{ex.Message}", ex);
        }

        Validate(manifest);
        return manifest;
    }

    internal static PetPackage Decode(PetManifest manifest, byte[] png)
    {
        if (png.Length > MaxSpriteBytes) throw new InvalidDataException("PNG 超出大小限制。");
        // Check dimensions before WIC allocates the decoded image.
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(8, 4)) != 13 ||
            !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("精灵图必须是有效 PNG。");
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width != manifest.CellWidth * manifest.Columns || height != manifest.CellHeight * manifest.Rows)
            throw new InvalidDataException("PNG 尺寸与 pet.json 中的单元格、行列数不一致。");

        try
        {
            using var stream = new MemoryStream(png, writable: false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            if (bitmap.PixelWidth != width || bitmap.PixelHeight != height)
                throw new InvalidDataException("PNG 解码尺寸不一致。");
            bitmap.Freeze();
            return new PetPackage(manifest, bitmap);
        }
        catch (Exception ex) when (ex is not InvalidDataException && ex is not OutOfMemoryException)
        {
            throw new InvalidDataException("PNG 无法解码。", ex);
        }
    }

    private static void Validate(PetManifest m)
    {
        if (m.SchemaVersion != 1) throw new InvalidDataException("不支持的皮肤 schemaVersion，当前支持 1。");
        ValidateId(m.Id);
        ValidateContent(m);
    }

    internal static void ValidateId(string id)
    {
        if (id is null || !Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{0,63}\z") ||
            new[] { "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9" }.Contains(id))
            throw new InvalidDataException("皮肤 id 必须为小写字母开头的 1-64 位字母、数字或连字符，不能是系统保留名称。");
    }

    private static void ValidateContent(PetManifest m)
    {
        if (string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 64 || m.Name.Any(char.IsControl) ||
            m.Description is null || m.Description.Length > 400)
            throw new InvalidDataException("皮肤名称需为 1-64 字符，描述最多 400 字符。");
        if (string.IsNullOrWhiteSpace(m.SpriteSheet) || m.SpriteSheet.Length > 100 ||
            m.SpriteSheet.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            m.SpriteSheet.Contains('/') || m.SpriteSheet.Contains('\\') ||
            !m.SpriteSheet.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("spriteSheet 只能引用同目录中的 PNG 文件名。");
        if (m.CellWidth is < 16 or > 512 || m.CellHeight is < 16 or > 512 ||
            m.Columns is < 1 or > 64 || m.Rows is < 1 or > 64 ||
            (long)m.CellWidth * m.Columns * m.CellHeight * m.Rows > 16_777_216 ||
            m.CellWidth * m.Columns > 8192 || m.CellHeight * m.Rows > 8192)
            throw new InvalidDataException("单元格需为 16-512 像素，行列需为 1-64，PNG 最大 8192 边长、1600 万像素。");
        if (m.Animations is null || !m.Animations.TryGetValue(PetState.Idle, out var idle) || idle is null || !idle.Loop)
            throw new InvalidDataException("皮肤必须定义 loop 为 true 的 idle 动作。");
        foreach (var (state, animation) in m.Animations)
        {
            if (!Enum.IsDefined(state) || animation is null || animation.Row < 0 || animation.Row >= m.Rows ||
                animation.StartColumn < 0 || animation.StartColumn >= m.Columns || animation.DurationsMs is null ||
                animation.DurationsMs.Length < 1 || animation.DurationsMs.Length > m.Columns - animation.StartColumn ||
                animation.DurationsMs.Any(ms => ms is < 20 or > 10_000))
                throw new InvalidDataException($"动作 {state} 的行列或帧时长无效（每帧 20-10000 毫秒）。");
            if (state is PetState.Waving or PetState.Jumping or PetState.Failed && animation.Loop)
                throw new InvalidDataException($"动作 {state} 必须为单次播放（loop: false）。");
        }
        if (m.LookDirections is null || m.LookDirections.Count is not (0 or LookDirectionCount) ||
            m.LookDirections.Any(f => f is null || f.Row < 0 || f.Row >= m.Rows || f.Column < 0 || f.Column >= m.Columns))
            throw new InvalidDataException("lookDirections 必须省略、为空，或包含 16 个有效帧坐标。");
    }

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("皮肤文件和目录不能是符号链接或目录联接。");
    }

    private static byte[] ReadLimitedFile(string path, int maximum)
    {
        RejectLink(path);
        using var stream = File.OpenRead(path);
        if (stream.Length <= 0 || stream.Length > maximum) throw new InvalidDataException("皮肤文件为空或超出大小限制。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"pet.json 属性重复：{property.Name}");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
