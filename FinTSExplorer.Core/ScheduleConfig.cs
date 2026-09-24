using System.Text.Json;

namespace FinTSExplorer.Core;

// Zeitplan als JSON, Wochentag als Master, Liste von Uhrzeiten (HH:mm) als Detail, z.B.:
// { "Monday": ["08:00", "14:00", "18:00"], "Saturday": [], "Sunday": [] }
public static class ScheduleConfig
{
    private const string FileName = "Schedule.json";

    public static Dictionary<DayOfWeek, List<TimeOnly>> Load(string baseDirectory, Action<string> log)
    {
        var path = Path.Combine(baseDirectory, FileName);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        if (!File.Exists(path))
        {
            var defaults = CreateDefault();
            File.WriteAllText(path, JsonSerializer.Serialize(ToJsonModel(defaults), options));
            log($"Es wurde ein Standard-Zeitplan angelegt (täglich 08:00): {path}");
            return defaults;
        }

        var model = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path), options) ?? new Dictionary<string, List<string>>();
        return FromJsonModel(model);
    }

    // Nächster fälliger Zeitpunkt nach "after", über alle konfigurierten Wochentage/Uhrzeiten hinweg.
    public static DateTime GetNextRun(Dictionary<DayOfWeek, List<TimeOnly>> schedule, DateTime after)
    {
        for (var dayOffset = 0; dayOffset < 8; dayOffset++)
        {
            var candidateDate = after.Date.AddDays(dayOffset);
            if (!schedule.TryGetValue(candidateDate.DayOfWeek, out var times) || times.Count == 0)
                continue;

            foreach (var time in times.OrderBy(t => t))
            {
                var candidate = candidateDate.Add(time.ToTimeSpan());
                if (candidate > after)
                    return candidate;
            }
        }

        // Sollte praktisch nie eintreten (kein einziger Zeitpunkt in der ganzen Woche konfiguriert).
        return after.AddDays(1);
    }

    private static Dictionary<DayOfWeek, List<TimeOnly>> CreateDefault()
    {
        var defaultTimes = new List<TimeOnly> { new(8, 0) };
        return Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => new List<TimeOnly>(defaultTimes));
    }

    private static Dictionary<string, List<string>> ToJsonModel(Dictionary<DayOfWeek, List<TimeOnly>> schedule) =>
        schedule.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Select(t => t.ToString("HH:mm")).ToList());

    private static Dictionary<DayOfWeek, List<TimeOnly>> FromJsonModel(Dictionary<string, List<string>> model)
    {
        var result = new Dictionary<DayOfWeek, List<TimeOnly>>();

        foreach (var (dayName, times) in model)
        {
            if (!Enum.TryParse<DayOfWeek>(dayName, ignoreCase: true, out var day))
                continue;

            result[day] = times.Select(TimeOnly.Parse).ToList();
        }

        return result;
    }
}
