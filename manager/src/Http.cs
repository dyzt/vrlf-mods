namespace VrlfMods;

public interface IHttpFetcher
{
    Task<byte[]?> TryGet(string url);
}

public sealed class HttpFetcher : IHttpFetcher
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

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
