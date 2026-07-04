namespace PuckDrop.Domain.Entities;

/// <summary>
/// Represents an EIHL season running August → April.
/// </summary>
public class Season
{
    /// <summary>
    /// Season identifier in format "2025-26".
    /// </summary>
    public required string SeasonId { get; set; }

    /// <summary>
    /// Display name, e.g. "2025/26 Season".
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Season start date (typically August 1st).
    /// </summary>
    public required DateOnly StartDate { get; set; }

    /// <summary>
    /// Season end date (typically April 30th).
    /// </summary>
    public required DateOnly EndDate { get; set; }

    /// <summary>
    /// Derives the season ID from a given game date.
    /// Seasons run Aug–Apr, so dates Aug–Dec belong to the "YYYY-(YY+1)" season,
    /// and dates Jan–Jul belong to the "(YYYY-1)-YY" season.
    /// </summary>
    public static string DeriveSeasonId(DateOnly gameDate)
    {
        int startYear = gameDate.Month >= 8 ? gameDate.Year : gameDate.Year - 1;
        int endYear = startYear + 1;
        return $"{startYear}-{endYear % 100:D2}";
    }

    /// <summary>
    /// Creates a Season instance for the season containing the given game date.
    /// </summary>
    public static Season CreateForDate(DateOnly gameDate)
    {
        string seasonId = DeriveSeasonId(gameDate);
        int startYear = gameDate.Month >= 8 ? gameDate.Year : gameDate.Year - 1;
        int endYear = startYear + 1;

        return new Season
        {
            SeasonId = seasonId,
            Name = $"{startYear}/{endYear % 100:D2} Season",
            StartDate = new DateOnly(startYear, 8, 1),
            EndDate = new DateOnly(endYear, 4, 30)
        };
    }
}
