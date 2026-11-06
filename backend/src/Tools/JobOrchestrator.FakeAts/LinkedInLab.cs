internal static class LinkedInLab
{
    public static void Map(RouteGroupBuilder lab, CaptchaLab captcha)
    {
        lab.MapGet("/linkedin", () => Listing("/lab/linkedin/image"));
        lab.MapGet("/linkedin/manual", () => Listing("/lab/linkedin/unsupported"));
        Protected(lab, captcha, "image", """
            <script type="application/ld+json">{inválido}</script>
            <div class="base-search-card" data-entity-urn="urn:li:jobPosting:101">
              <a class="base-card__full-link" href="https://linkedin.example/jobs/view/101?trk=duplicate"></a>
              <h3 class="base-search-card__title">Backend .NET</h3>
            </div>
            <div class="base-search-card" data-entity-urn="urn:li:jobPosting:102">
              <a class="base-card__full-link" href="https://linkedin.example/jobs/view/cloud-engineer-102"></a>
              <h3 class="base-search-card__title">Cloud Engineer</h3>
              <h4 class="base-search-card__subtitle">Oeste Fictícia</h4>
              <span class="job-search-card__location">Remoto</span>
            </div>
            <a rel="next" href="/lab/linkedin/math">Próxima página</a>
            """);
        Protected(lab, captcha, "math", """
            <div class="base-search-card" data-entity-urn="urn:li:jobPosting:103">
              <a class="base-card__full-link" href="https://linkedin.example/jobs/view/103"></a>
              <h3 class="base-search-card__title">Software Engineer</h3>
              <h4 class="base-search-card__subtitle">Sul Fictícia</h4>
            </div>
            """);
        lab.MapGet("/linkedin/unsupported", () =>
            captcha.Issue("unsupported", "/lab/linkedin/unsupported", StatusCodes.Status403Forbidden));
    }

    private static IResult Listing(string next) => Page($$$$"""
        <script type="application/ld+json">
        {"@context":"https://schema.org","@graph":[{"@type":"JobPosting","title":"Backend .NET",
        "url":"https://linkedin.example/jobs/view/101?trk=lab","hiringOrganization":{"name":"Norte Fictícia"},
        "jobLocation":{"address":{"addressLocality":"Rio de Janeiro","addressCountry":"BR"}}}]}
        </script>
        <a rel="next" href="{{{{next}}}}">Próxima página</a>
        """);

    private static void Protected(RouteGroupBuilder lab, CaptchaLab captcha, string kind, string content)
    {
        var path = $"/linkedin/{kind}";
        var scope = "/lab" + path;
        lab.MapGet(path, () => captcha.Issue(kind, scope, StatusCodes.Status403Forbidden));
        lab.MapPost(path, async (HttpRequest request, CancellationToken ct) =>
            await captcha.ValidateAsync(request, scope, ct) ? Page(content) : CaptchaLab.Rejected());
    }

    private static IResult Page(string body) => Results.Content(Html.Page(body), "text/html; charset=utf-8");
}
