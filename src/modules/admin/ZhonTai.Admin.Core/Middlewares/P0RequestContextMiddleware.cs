using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using ZhonTai.Admin.Core.Auth;

namespace ZhonTai.Admin.Core.Middlewares;

/// <summary>Establishes one stable request trace identifier before error handling and logging.</summary>
public sealed class P0RequestContextMiddleware
{
    public const string TraceIdHeader = "X-Trace-Id";
    public const string TraceIdItemKey = "P0.TraceId";

    private readonly RequestDelegate _next;
    private readonly ILogger<P0RequestContextMiddleware> _logger;

    public P0RequestContextMiddleware(RequestDelegate next, ILogger<P0RequestContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = GetTraceId(context);
        context.Items[TraceIdItemKey] = traceId;
        context.TraceIdentifier = traceId;
        context.Response.Headers[TraceIdHeader] = traceId;
        var stopwatch = Stopwatch.StartNew();
        using (_logger.BeginScope(new RequestLogScope(context, traceId)))
        {
            await _next(context);
            stopwatch.Stop();
            _logger.LogInformation("Request completed with {ElapsedMs}ms and {StatusCode}.", stopwatch.ElapsedMilliseconds, context.Response.StatusCode);
        }
    }

    public static string GetTraceId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.TryGetValue(TraceIdItemKey, out var stored) && stored is string existing && IsValidTraceId(existing))
        {
            return existing;
        }

        var candidate = context.Request.Headers[TraceIdHeader].FirstOrDefault();
        var traceId = IsValidTraceId(candidate)
            ? candidate!
            : Activity.Current?.TraceId.ToString() is { } activityTraceId && IsValidTraceId(activityTraceId)
                ? activityTraceId
                : Guid.NewGuid().ToString("N");
        context.Items[TraceIdItemKey] = traceId;
        return traceId;
    }

    private static bool IsValidTraceId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(char.IsLetterOrDigit);

    private sealed class RequestLogScope(HttpContext context, string traceId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private static readonly string[] Keys = ["TraceId", "UserId", "OrgId", "Module", "BusinessKey", "JobId", "ExternalSystem"];

        public int Count => Keys.Length;
        public KeyValuePair<string, object?> this[int index] => new(Keys[index], ValueFor(Keys[index]));
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Keys.Select(key => new KeyValuePair<string, object?>(key, ValueFor(key))).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private object? ValueFor(string key) => key switch
        {
            "TraceId" => traceId,
            "UserId" => context.RequestServices.GetService<IUser>()?.Id is > 0 and var userId ? userId : string.Empty,
            "OrgId" => context.RequestServices.GetService<IUser>()?.DataPermission?.OrgId is > 0 and var orgId ? orgId : string.Empty,
            "Module" => "Admin",
            _ => string.Empty
        };
    }
}
