using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Contracts;

/// <summary>Chave de fonte a partir de um link: o quadro oficial de cada ATS (greenhouse:empresa, gupy:empresa, lever:empresa...).</summary>
public static class SourceKeys
{
    public static string? Board(string url)
    {
        foreach (var (re, platform) in Boards)
            if (re.Match(url) is { Success: true } m && m.Groups[1].Value is { Length: > 1 } slug && !Reserved.Contains(slug.ToLowerInvariant()))
                return $"{platform}:{(platform is "gupy" or "recruitee" or "breezy" or "personio" or "workable" or "teamtailor" ? slug.ToLowerInvariant() : slug)}";
        return null;
    }

    private static readonly HashSet<string> Reserved = ["embed", "jobs", "www", "api", "attachments", "suporte-candidatos", "login", "portal", "help", "blog", "static", "cdn", "assets", "app", "candidates", "talentos", "j", "apply", "resources", "career", "careers", "support", "docs"];
    private static readonly (Regex, string)[] Boards =
    [
        (new(@"(?:boards|job-boards)(?:\.eu)?\.greenhouse\.io/(?:embed/job_board\?for=)?([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase), "greenhouse"),
        (new(@"jobs\.(?:eu\.)?lever\.co/([A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase), "lever"),
        (new(@"jobs\.ashbyhq\.com/([A-Za-z0-9_.%-]+)", RegexOptions.IgnoreCase), "ashby"),
        (new(@"https?://([a-z0-9-]+)\.gupy\.io", RegexOptions.IgnoreCase), "gupy"),
        (new(@"(?:jobs|careers)\.smartrecruiters\.com/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase), "smartrecruiters"),
        (new(@"https?://([a-z0-9-]+)\.recruitee\.com", RegexOptions.IgnoreCase), "recruitee"),
        (new(@"https?://([a-z0-9-]+)\.breezy\.hr", RegexOptions.IgnoreCase), "breezy"),
        (new(@"https?://([a-z0-9-]+)\.jobs\.personio\.(?:de|com)", RegexOptions.IgnoreCase), "personio"),
        (new(@"https?://apply\.workable\.com/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase), "workable"),
        (new(@"https?://([a-z0-9-]+)\.workable\.com", RegexOptions.IgnoreCase), "workable"),
        (new(@"https?://([a-z0-9-]+(?:\.[a-z]{2})?)\.teamtailor\.com", RegexOptions.IgnoreCase), "teamtailor"),
    ];
}
