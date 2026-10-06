namespace Hotline.App;

/// <summary>Where Hotline lives and how to support it (one place, so the About page and notes agree).</summary>
internal static class SupportLinks
{
    /// <summary>
    /// Off until the GitHub Sponsors and Ko-fi accounts exist: hides the About page's Support card and the one-time
    /// "Enjoying Hotline?" note. Turn on together with .github/FUNDING.yml and the README section.
    /// </summary>
    public const bool DonationsLive = false;

    public static readonly Uri Repository = new("https://github.com/PMARC14/hotline");
    public static readonly Uri GitHubSponsors = new("https://github.com/sponsors/PMARC14");
    public static readonly Uri KoFi = new("https://ko-fi.com/pmarc14");
    public static readonly Uri Issues = new("https://github.com/PMARC14/hotline/issues");
    public static readonly Uri License = new("https://github.com/PMARC14/hotline/blob/main/LICENSE");
    public static readonly Uri ThirdPartyNotices = new("https://github.com/PMARC14/hotline/blob/main/THIRD-PARTY-NOTICES.md");
}
