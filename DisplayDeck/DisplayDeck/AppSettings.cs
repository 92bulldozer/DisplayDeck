using System.IO;
using System.Text.Json;

namespace DisplayDeck;

/// <summary>저장되는 앱 상태 (창 크기, 항상 위 토글 등).</summary>
public class AppState
{
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool Maximized { get; set; }
    public bool AlwaysOnTop { get; set; }
}

/// <summary>앱 상태를 %AppData%\DisplayDeck\settings.json 에 저장/로드.</summary>
public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayDeck");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath));
                if (state is not null)
                    return state;
            }
        }
        catch
        {
            // 손상된 파일은 무시하고 기본값 사용
        }

        return new AppState();
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 저장 실패는 무시 — 다음 실행에 기본값으로 뜰 뿐
        }
    }
}
