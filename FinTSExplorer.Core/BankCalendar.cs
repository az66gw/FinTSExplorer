namespace FinTSExplorer.Core;

// Kleiner Bankarbeitstage-Kalender: Wochenenden plus bundeseinheitliche Feiertage (feste und von Ostern
// abgeleitete) und Fronleichnam (Hessen). Regionale Sonderfeiertage anderer Laender sind bewusst nicht dabei.
public static class BankCalendar
{
    public static bool IsBankDay(DateTime date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    // Wievielter Bankarbeitstag des Monats ist dieses Datum (1 = erster Bankarbeitstag)? Zaehlt bis einschliesslich date.
    public static int BankDayIndex(DateTime date)
    {
        var count = 0;
        for (var day = new DateTime(date.Year, date.Month, 1); day <= date.Date; day = day.AddDays(1))
        {
            if (IsBankDay(day))
                count++;
        }

        return count;
    }

    // Datum des n-ten Bankarbeitstags eines Monats; gibt es weniger, den letzten Bankarbeitstag des Monats.
    public static DateTime DateOfBankDay(int year, int month, int index)
    {
        var count = 0;
        var lastBankDay = new DateTime(year, month, 1);

        for (var day = new DateTime(year, month, 1); day.Month == month; day = day.AddDays(1))
        {
            if (!IsBankDay(day))
                continue;

            lastBankDay = day;
            if (++count == index)
                return day;
        }

        return lastBankDay;
    }

    private static bool IsHoliday(DateTime date)
    {
        var d = date.Date;

        if ((d.Month, d.Day) is (1, 1) or (5, 1) or (10, 3) or (12, 25) or (12, 26))
            return true;

        var easter = EasterSunday(d.Year);
        return d == easter.AddDays(-2)   // Karfreitag
            || d == easter.AddDays(1)    // Ostermontag
            || d == easter.AddDays(39)   // Christi Himmelfahrt
            || d == easter.AddDays(50)   // Pfingstmontag
            || d == easter.AddDays(60);  // Fronleichnam (Hessen)
    }

    // Gregorianischer Ostersonntag (anonymer Algorithmus nach Meeus/Jones/Butcher).
    private static DateTime EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }
}
