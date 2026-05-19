using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DisplayDeck;

/// <summary>해상도 프리셋 한 개 (가로 × 세로).</summary>
public record Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

/// <summary>
/// 해상도 프리셋 목록을 %AppData%\DisplayDeck\presets.json 에 저장/로드.
/// 추가·삭제한 내용이 그대로 유지되어 다음 실행 시 복원된다.
/// </summary>
public static class PresetStore
{
    /// <summary>첫 실행 시 채워 넣는 기본 프리셋 (POS 테스트 흔한 해상도).</summary>
    private static readonly Resolution[] Defaults =
    {
        new(1920, 1080),
        new(1600, 900),
        new(1366, 768),
        new(1280, 1024),
        new(1280, 720),
        new(1024, 768),
        new(800, 600),
    };

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayDeck");

    private static readonly string FilePath = Path.Combine(Dir, "presets.json");

    /// <summary>저장된 프리셋을 로드. 파일이 없거나 손상됐으면 기본값으로 시작하며 저장한다.</summary>
    public static List<Resolution> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<Resolution>>(json);
                if (list is not null)
                    return list;
            }
        }
        catch
        {
            // 손상된 파일은 무시하고 기본값으로 복구
        }

        var defaults = Defaults.ToList();
        Save(defaults);
        return defaults;
    }

    public static void Save(List<Resolution> presets)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
