using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using ZhonTai.Admin.Core.Exceptions;
using ZhonTai.Admin.Core.Dto;
using ZhonTai.Common.Helpers;
using MySqlConnector;

namespace ZhonTai.Admin.Core.Middlewares;

/// <summary>
/// 异常中间件
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (AppException ex)
        {
            await HandleAppExceptionAsync(httpContext, ex);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private static Task HandleAppExceptionAsync(HttpContext context, AppException appException)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = appException.StatusCode;

        return context.Response.WriteAsync(JsonHelper.Serialize(new ResultOutput<string>()
        {
            Code = appException.AppCode.NotNull() ? appException.AppCode : P0ErrorCodes.Business,
            TraceId = P0RequestContextMiddleware.GetTraceId(context)
        }.NotOk(appException.AppMessage)));
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        var databaseException = FindMySqlException(exception);
        var isUniqueConstraint = databaseException?.Number == 1062;
        context.Response.StatusCode = isUniqueConstraint
            ? (int)HttpStatusCode.Conflict
            : (int)HttpStatusCode.InternalServerError;

        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();
        _logger.LogError(exception,
        "Error while processing request. TraceId: {TraceId} UserAgent: {UserAgent}",
        P0RequestContextMiddleware.GetTraceId(context),
        userAgent);

        return context.Response.WriteAsync(JsonHelper.Serialize(new ResultOutput<string>
        {
            Code = isUniqueConstraint ? P0ErrorCodes.Conflict : P0ErrorCodes.Unexpected,
            TraceId = P0RequestContextMiddleware.GetTraceId(context)
        }.NotOk(isUniqueConstraint ? "The requested resource conflicts with existing data." : "An unexpected server error occurred.")));
    }

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException mysqlException)
            {
                return mysqlException;
            }
        }

        return null;
    }
}
