namespace Atc.Claude.Kanban.Tests.Helpers;

/// <summary>
/// A test helper that delegates HTTP requests to a provided function.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

    public MockHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        => this.handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
}