using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using ZhonTai.Common.Extensions;
using ZhonTai.Common.Helpers;
using StatusCodes = ZhonTai.Admin.Core.Enums.StatusCodes;
using ZhonTai.Admin.Core.Exceptions;
using ZhonTai.Admin.Core.Middlewares;

namespace ZhonTai.Admin.Core.Handlers;

/// <summary>
/// 响应认证处理器
/// </summary>
public class ResponseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#if NET8_0_OR_GREATER
    public ResponseAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : base(options, logger, encoder) 
#else
    public ResponseAuthenticationHandler(
       IOptionsMonitor<AuthenticationSchemeOptions> options,
       ILoggerFactory logger,
       UrlEncoder encoder,
       ISystemClock systemClock
   ) : base(options, logger, encoder, systemClock)
#endif
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        throw new NotImplementedException();
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.ContentType = "application/json";
        Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
        await Response.WriteAsync(JsonHelper.Serialize(
            new ResponseStatusData
            {
                Code = P0ErrorCodes.Unauthorized,
                Msg = StatusCodes.Status401Unauthorized.ToDescription(),
                Success = false,
                TraceId = P0RequestContextMiddleware.GetTraceId(Context)
            }
        ));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.ContentType = "application/json";
        Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
        await Response.WriteAsync(JsonHelper.Serialize(
            new ResponseStatusData
            {
                Code = P0ErrorCodes.Forbidden,
                Msg = StatusCodes.Status403Forbidden.ToDescription(),
                Success = false,
                TraceId = P0RequestContextMiddleware.GetTraceId(Context)
            }
        ));
    }
}

public class ResponseStatusData
{
    public string Code { get; set; } = "P0.Ok";
    public string Msg { get; set; }
    public bool Success { get; set; }
    public string TraceId { get; set; }

}
