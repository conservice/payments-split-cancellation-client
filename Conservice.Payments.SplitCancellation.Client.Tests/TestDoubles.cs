using System.Net;
using System.Text;

namespace Conservice.Payments.SplitCancellation.Client.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }
    public List<string?> RequestBodies { get; } = [];

    public static StubHttpMessageHandler Answering(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        RequestBodies.Add(LastRequestBody);
        return await _respond(request, cancellationToken);
    }
}

internal sealed class StubTokenProvider : IGatekeeperAccessTokenProvider
{
    private readonly Func<CancellationToken, Task<string?>> _getToken;

    public StubTokenProvider(Func<CancellationToken, Task<string?>> getToken)
    {
        _getToken = getToken;
    }

    public static StubTokenProvider Returning(string? token) => new(_ => Task.FromResult(token));

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => _getToken(cancellationToken);
}
