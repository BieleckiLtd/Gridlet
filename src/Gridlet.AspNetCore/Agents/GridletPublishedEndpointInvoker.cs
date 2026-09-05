using System.Diagnostics;
using System.Text;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Gridlet.AspNetCore.Agents;

/// <summary>
/// Lets an agent turn call this Gridlet's own published endpoints so a person can see the real
/// response. Every restriction here exists because the caller is a language model acting on text it
/// read from a database:
///
/// - Only endpoints published in this installation can be reached, addressed by name. The model
///   never supplies a URL, so it cannot aim this at another host or at an internal service.
/// - Only <c>GET</c> endpoints are eligible. A published endpoint runs whatever SQL was published,
///   and the other verbs are the ones a person would reasonably use for something that changes data.
/// - The call carries the browser's own credentials and is answered by the same authorization the
///   person would meet themselves, so this widens nothing they could not already reach. Those
///   credentials go back to the exact origin the browser just sent them to - the address is built
///   from the live request, never from anything the model supplied - so no credential reaches a
///   host it was not already presented to.
/// - The response is bounded in size and time before any of it is handed back to the model.
/// </summary>
internal sealed class GridletPublishedEndpointInvoker(
    IPublishedEndpointStore store,
    IHttpContextAccessor httpContextAccessor,
    GridletMountPath mountPath,
    IOptionsMonitor<GridletOptions> options,
    IHttpClientFactory httpClientFactory)
    : IGridletPublishedEndpointInvoker
{
    /// <summary>Enough of a response to show a person what the shape is; not a data export.</summary>
    private const int MaxBodyCharacters = 8_192;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<GridletPublishedEndpointInvocation> InvokeAsync(
        string name,
        IReadOnlyDictionary<string, string?> query,
        GridletAgentUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var endpoints = await store.GetAllAsync(cancellationToken);
        var endpoint = endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (endpoint is null)
        {
            return Failure(
                "endpoint_not_found",
                $"No endpoint named '{name}' is published here. Call list_published_api_endpoints " +
                "for the real names.");
        }

        if (!string.Equals(endpoint.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "method_not_invocable",
                $"'{endpoint.Name}' is a {endpoint.Method} endpoint. Only GET endpoints can be " +
                "called this way, because the others may change data. Describe it instead, or " +
                "offer to open it in an API request tab so the person can send it themselves.");
        }

        if (!endpoint.Enabled)
        {
            return Failure("endpoint_disabled", $"'{endpoint.Name}' is published but disabled.");
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Failure(
                "no_request_context",
                "This turn is not running inside a browser request, so the endpoint's address and " +
                "the caller's credentials are both unknown.");
        }

        var unknown = query.Keys
            .Where(key => !endpoint.Parameters.Any(parameter =>
                string.Equals(parameter.Name, key, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (unknown.Length > 0)
        {
            return Failure(
                "unknown_parameter",
                $"'{endpoint.Name}' does not take {string.Join(", ", unknown)}. Its parameters are: " +
                (endpoint.Parameters.Count == 0
                    ? "none."
                    : string.Join(", ", endpoint.Parameters.Select(parameter => parameter.Name)) + "."));
        }

        var missing = endpoint.Parameters
            .Where(parameter => parameter.Required && !query.Keys.Any(key =>
                string.Equals(key, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(parameter => parameter.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            return Failure(
                "missing_parameter",
                $"'{endpoint.Name}' requires {string.Join(", ", missing)}. Look up a real value " +
                "with the query tool rather than inventing one.");
        }

        var url = BuildUrl(httpContext, endpoint.Route, query);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            ForwardCallerCredentials(httpContext, message);

            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var (body, truncated) = await ReadBoundedBodyAsync(response, linked.Token);
            stopwatch.Stop();

            return new GridletPublishedEndpointInvocation(
                Succeeded: true,
                Method: "GET",
                Url: url,
                StatusCode: (int)response.StatusCode,
                ContentType: response.Content.Headers.ContentType?.ToString(),
                Body: body,
                Truncated: truncated,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return Failure(
                "timed_out",
                $"The endpoint did not answer within {Timeout.TotalSeconds:0} seconds.",
                url,
                stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            // The message can name internal addresses, so the model gets the reason without them.
            return Failure(
                "request_failed",
                $"The request to the endpoint failed ({exception.StatusCode?.ToString() ?? "no response"}).",
                url,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Named client so a host can configure the loopback call - proxies, certificates.</summary>
    internal const string HttpClientName = "Gridlet.PublishedEndpointInvoker";

    private string BuildUrl(
        HttpContext httpContext,
        string route,
        IReadOnlyDictionary<string, string?> query)
    {
        var request = httpContext.Request;
        var configuredPath = options.CurrentValue.PublishedApiPath;
        var path = configuredPath is { Length: > 0 }
            ? string.Concat(
                request.PathBase.ToString(), "/", configuredPath.Trim('/'),
                "/", route.TrimStart('/'))
            : string.Concat(
                request.PathBase.ToString(), mountPath.Value,
                "/", options.CurrentValue.PublishedApiSegment, "/", route.TrimStart('/'));
        var url = new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = path,
        };
        if (request.Host.Port is { } port) url.Port = port;

        var address = url.Uri.ToString();
        return query.Count == 0
            ? address
            : QueryHelpers.AddQueryString(
                address,
                query.Select(pair =>
                    new KeyValuePair<string, StringValues>(pair.Key, pair.Value ?? string.Empty)));
    }

    /// <summary>
    /// Sends the call as the person, not as the server. Without this the endpoint would answer an
    /// anonymous caller - which either fails confusingly or, on an installation that allows
    /// anonymous access, shows the agent something the person's own session might not permit.
    /// </summary>
    private static void ForwardCallerCredentials(HttpContext httpContext, HttpRequestMessage message)
    {
        if (httpContext.Request.Headers.TryGetValue("Cookie", out var cookie) &&
            cookie.Count > 0)
        {
            message.Headers.TryAddWithoutValidation("Cookie", cookie.ToArray());
        }

        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authorization) &&
            authorization.Count > 0)
        {
            message.Headers.TryAddWithoutValidation("Authorization", authorization.ToArray());
        }
    }

    private static async Task<(string Body, bool Truncated)> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[MaxBodyCharacters + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (chunk == 0) break;
            read += chunk;
        }

        return read > MaxBodyCharacters
            ? (new string(buffer, 0, MaxBodyCharacters), true)
            : (new string(buffer, 0, read), false);
    }

    private static GridletPublishedEndpointInvocation Failure(
        string code,
        string message,
        string url = "",
        long elapsedMilliseconds = 0)
        => new(
            Succeeded: false,
            Method: "GET",
            Url: url,
            StatusCode: null,
            ContentType: null,
            Body: null,
            Truncated: false,
            ElapsedMilliseconds: elapsedMilliseconds,
            ErrorCode: code,
            ErrorMessage: message);
}

/// <summary>
/// The route prefix <c>MapGridlet</c> was actually called with. It is chosen by the host at mapping
/// time, after services are built, so it is published here rather than through options.
/// </summary>
public sealed class GridletMountPath
{
    /// <summary>The prefix, without a trailing slash. Defaults to the documented default.</summary>
    public string Value { get; private set; } = "/gridlet";

    public void Set(string pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern)) Value = "/" + pattern.Trim('/');
    }
}
