using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ItsycalForWindows.Tests;

/// <summary>In-memory <see cref="HttpMessageHandler"/> that records requests and answers with a lambda.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : this((request, _) => Task.FromResult(send(request)))
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _send = send;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _send(request, cancellationToken);
    }
}
