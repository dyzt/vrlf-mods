namespace VrlfMods;

public interface IHttpFetcher
{
    Task<byte[]?> TryGet(string url);
}

public sealed class HttpFetcher : IHttpFetcher
{
    private static readonly HttpClient Client = NewClient();

    // GitHub's API refuses a request with no User-Agent (the DemulShooter latest-release lookup).
    private static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("vrlf-mods");
        return c;
    }

    public async Task<byte[]?> TryGet(string url)
    {
        try
        {
            using var resp = await Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }
}
