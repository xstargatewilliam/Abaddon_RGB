using System.Globalization;

namespace AbaddonRGB.Core.Automation;

/// <summary>Regras de horário: "às 22:00, aplicar o perfil Noite".</summary>
public static class Scheduler
{
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time))
            return false;
        return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    /// <summary>
    /// A regra que "está valendo" num horário: a última que já passou hoje ou,
    /// se nenhuma passou ainda, a última de ontem.
    /// </summary>
    public static ScheduleRule? ActiveAt(IEnumerable<ScheduleRule> rules, TimeSpan timeOfDay)
    {
        var valid = Valid(rules);
        if (valid.Count == 0) return null;

        var passed = valid.Where(x => x.Time <= timeOfDay).ToList();
        return (passed.Count > 0 ? passed[^1] : valid[^1]).Rule;
    }

    /// <summary>A regra mais recente cujo horário caiu entre a última verificação e agora.</summary>
    public static ScheduleRule? DueBetween(IEnumerable<ScheduleRule> rules, DateTime lastCheck, DateTime now)
    {
        if (now <= lastCheck) return null;

        ScheduleRule? best = null;
        var bestMoment = DateTime.MinValue;
        foreach (var (rule, time) in Valid(rules))
        {
            foreach (var day in new[] { now.Date, now.Date.AddDays(-1) })
            {
                var moment = day + time;
                if (moment > lastCheck && moment <= now && moment >= bestMoment)
                {
                    best = rule;
                    bestMoment = moment;
                }
            }
        }

        return best;
    }

    private static List<(ScheduleRule Rule, TimeSpan Time)> Valid(IEnumerable<ScheduleRule> rules) =>
        rules.Select(r => (Rule: r, Ok: TryParseTime(r.Time, out var t), Time: t))
            .Where(x => x.Ok && !string.IsNullOrWhiteSpace(x.Rule.Profile))
            .Select(x => (x.Rule, x.Time))
            .OrderBy(x => x.Time)
            .ToList();
}
