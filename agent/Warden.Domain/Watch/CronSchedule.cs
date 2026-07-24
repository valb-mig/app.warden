namespace Warden.Domain.Watch;

/// <summary>
/// Parser de expressão cron de 5 campos: min hour day month weekday. Suporta: wildcard (*),
/// valor literal (5), lista (1,3,5), range (1-5), step (*/5, 1-5/2). Não suporta aliases como
/// @daily — mantém o escopo mínimo suficiente para agendamento de actions.
/// </summary>
public sealed class CronSchedule
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _days;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _weekdays;

    private CronSchedule(
        HashSet<int> minutes,
        HashSet<int> hours,
        HashSet<int> days,
        HashSet<int> months,
        HashSet<int> weekdays)
    {
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekdays = weekdays;
    }

    /// <summary>
    /// Tenta parsear a expressão. Retorna false se a expressão for inválida, sem jogar exceção —
    /// permite que o chamador decida entre ignorar a entrada ou logar aviso.
    /// </summary>
    public static bool TryParse(string expression, out CronSchedule? schedule)
    {
        schedule = null;
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;

        try
        {
            schedule = new CronSchedule(
                ParseField(parts[0], 0, 59),
                ParseField(parts[1], 0, 23),
                ParseField(parts[2], 1, 31),
                ParseField(parts[3], 1, 12),
                ParseField(parts[4], 0, 6));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica se o instante UTC fornecido satisfaz a expressão. DayOfWeek usa a convenção cron:
    /// 0 = domingo, 6 = sábado (igual a <see cref="DayOfWeek"/> cast pra int).
    /// </summary>
    public bool Matches(DateTime utcNow) =>
        _minutes.Contains(utcNow.Minute) &&
        _hours.Contains(utcNow.Hour) &&
        _days.Contains(utcNow.Day) &&
        _months.Contains(utcNow.Month) &&
        _weekdays.Contains((int)utcNow.DayOfWeek);

    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var result = new HashSet<int>();
        foreach (var part in field.Split(','))
            ExpandPart(part.Trim(), min, max, result);
        return result;
    }

    private static void ExpandPart(string part, int min, int max, HashSet<int> result)
    {
        // step: */5 ou 1-5/2
        var stepIndex = part.IndexOf('/');
        int step = 1;
        if (stepIndex >= 0)
        {
            step = int.Parse(part[(stepIndex + 1)..]);
            part = part[..stepIndex];
        }

        int from, to;
        if (part == "*")
        {
            from = min;
            to = max;
        }
        else if (part.Contains('-'))
        {
            var dash = part.IndexOf('-');
            from = int.Parse(part[..dash]);
            to = int.Parse(part[(dash + 1)..]);
        }
        else
        {
            var value = int.Parse(part);
            if (stepIndex < 0)
            {
                result.Add(value);
                return;
            }
            from = value;
            to = max;
        }

        for (var i = from; i <= to; i += step)
            result.Add(i);
    }
}
