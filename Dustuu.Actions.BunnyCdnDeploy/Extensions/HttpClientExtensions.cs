using System.Net.Http.Json;

namespace Dustuu.Actions.BunnyCdnDeploy.Extensions;

public static class HttpClientExtensions
{
    private static async Task<HttpResponseMessage> ValidateResponse
    (Task<HttpResponseMessage> task)
    {
        HttpResponseMessage getResponseMessage = await task;
        if (!getResponseMessage.IsSuccessStatusCode)
        { throw new Exception(await getResponseMessage.Content.ReadAsStringAsync()); }

        return getResponseMessage;
    }

    public static async Task<HttpResponseMessage> PutResponseMessage<TI>
    (this HttpClient http, string uri, TI ti) =>
        await ValidateResponse(http.PutAsJsonAsync(uri, ti));

    public static async Task<HttpResponseMessage> GetResponseMessage
    (this HttpClient http, string uri) =>
        await ValidateResponse(http.GetAsync(uri));

    public static async Task<TO> Get<TO>
    (this HttpClient http, string uri) =>
        ( await ( await http.GetResponseMessage(uri) ).Content.ReadFromJsonAsync<TO>() )!;

    public static async Task<HttpResponseMessage> PostResponseMessage<TI>
    (this HttpClient http, string uri, TI ti) =>
        await ValidateResponse(http.PostAsJsonAsync(uri, ti));

    public static async Task<TO> Post<TI, TO>(this HttpClient http, string uri, TI ti) =>
        ( await (await http.PostResponseMessage(uri, ti)).Content.ReadFromJsonAsync<TO>() )!;
}
