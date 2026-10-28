using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoBuf.Grpc;
using Xunit;
using ZhonTai.Admin.Core.Attributes;
using ZhonTai.Admin.Core.Auth;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.GrpcServices;
using ZhonTai.Admin.Core.GrpcServices.Dtos;
using ZhonTai.Admin.Core.Handlers;
using ZhonTai.Admin.Core.Protos;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class PermissionHandlerTests
{
    [Theory]
    [InlineData(true, "", "read,write", false)]
    [InlineData(true, "read", "read,write", false)]
    [InlineData(true, "read,write", "read,write", true)]
    [InlineData(true, "read,write,extra", "read,write", true)]
    [InlineData(true, "read", "", false)]
    [InlineData(true, "", "", false)]
    [InlineData(false, "read", "read,write", true)]
    [InlineData(false, "other", "read,write", false)]
    [InlineData(false, "read", "", false)]
    public async Task PermissionCodesRespectAllAndAny(bool all, string owned, string required, bool expected)
    {
        var permissions = new UserGetPermissionGrpcOutput { Apis = [], Codes = Split(owned).ToList() };
        var handler = new PermissionHandler(new MutableUser(), new PermissionService(permissions));
        var access = new ApiAccessAttribute(Split(required)) { All = all };

        Assert.Equal(expected, await handler.ValidateAsync("api/test", "GET", access));
    }

    [Fact]
    public async Task ExplicitApiGrantKeepsPriorityOverPermissionCodeFallback()
    {
        var permissions = new UserGetPermissionGrpcOutput
        {
            Apis = [new() { Path = "/API/TEST", HttpMethods = "POST,GET" }],
            Codes = []
        };
        var handler = new PermissionHandler(new MutableUser(), new PermissionService(permissions));

        Assert.True(await handler.ValidateAsync("api/test", "get", new ApiAccessAttribute("missing") { All = true }));
        Assert.False(await handler.ValidateAsync("api/test", "DELETE", null!));
    }

    [Fact]
    public async Task NullPermissionCodeRequirementDoesNotGrantAccess()
    {
        var permissions = new UserGetPermissionGrpcOutput { Apis = [], Codes = [] };
        var handler = new PermissionHandler(new MutableUser(), new PermissionService(permissions));

        Assert.False(await handler.ValidateAsync("api/test", "GET", new ApiAccessAttribute { Codes = null!, All = true }));
    }

    [Fact]
    public async Task CustomPermissionDenialProducesForbidResult()
    {
        var context = CreateAuthorizationContext(new DeniedPermissionHandler());
        using var services = (ServiceProvider)context.HttpContext.RequestServices;

        await new ValidatePermissionAttribute().OnAuthorizationAsync(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task CustomPermissionDenialPreservesHandlerResult()
    {
        var expected = new StatusCodeResult(StatusCodes.Status404NotFound);
        var context = CreateAuthorizationContext(new DeniedPermissionHandler(expected));
        using var services = (ServiceProvider)context.HttpContext.RequestServices;

        await new ValidatePermissionAttribute().OnAuthorizationAsync(context);

        Assert.Same(expected, context.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomPermissionApprovalStillRequiresBuiltInPermission(bool granted)
    {
        var permissions = new UserGetPermissionGrpcOutput
        {
            Apis = granted ? [new() { Path = "/api/test", HttpMethods = "GET" }] : [],
            Codes = []
        };
        var context = CreateAuthorizationContext(new ApprovedPermissionHandler(), services => services
            .AddSingleton(new AppConfig())
            .AddSingleton<IPermissionHandler>(new PermissionHandler(new MutableUser(), new PermissionService(permissions))));
        using var services = (ServiceProvider)context.HttpContext.RequestServices;

        await new ValidatePermissionAttribute().OnAuthorizationAsync(context);

        if (granted) Assert.Null(context.Result);
        else Assert.IsType<ForbidResult>(context.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MvcWaitsForCustomPermissionAndExecutesOnlyApprovedActions(bool approved)
    {
        var handler = new PendingPermissionHandler();
        var executions = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IUser>(new MutableUser { IdValue = 1 });
        builder.Services.AddSingleton<ICustomPermissionHandler>(handler);
        builder.Services.AddSingleton(executions);
        builder.Services.AddSingleton(new AppConfig { Validate = new() { Permission = false } });
        builder.Services.AddAuthentication("permission-regression")
            .AddScheme<AuthenticationSchemeOptions, PermissionTestAuthenticationHandler>("permission-regression", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers(options => options.Filters.Add<ValidatePermissionAttribute>())
            .AddApplicationPart(typeof(PermissionFilterProbeController).Assembly);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var request = client.GetAsync("/__permission-filter-regression");
            try
            {
                await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(request.IsCompleted);
                Assert.Empty(executions);
            }
            finally { handler.Decision.TrySetResult(approved); }

            using var response = await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(approved ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            if (approved) Assert.Single(executions);
            else Assert.Empty(executions);
        }
        finally { await app.StopAsync(); }
    }

    private static AuthorizationFilterContext CreateAuthorizationContext(ICustomPermissionHandler customPermissionHandler,
        Action<IServiceCollection> configure = null)
    {
        var registrations = new ServiceCollection()
            .AddSingleton<IUser>(new MutableUser { IdValue = 1 })
            .AddSingleton(customPermissionHandler);
        configure?.Invoke(registrations);
        var services = registrations.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Method = "GET";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "test"));
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = new List<object>(), AttributeRouteInfo = new() { Template = "api/test" } });
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private sealed class PermissionService(UserGetPermissionGrpcOutput permissions) : IUserGrpcService
    {
        public Task<UserGetPermissionGrpcOutput> GetPermissionAsync(CallContext context = default) => Task.FromResult(permissions);
        public Task GetDataPermissionAsync(ProtoString apiPath, CallContext context = default) => Task.CompletedTask;
    }

    private sealed class DeniedPermissionHandler(IActionResult result = null) : ICustomPermissionHandler
    {
        public Task<bool> ValidateAsync(AuthorizationFilterContext context)
        {
            context.Result = result;
            return Task.FromResult(false);
        }
    }

    private sealed class ApprovedPermissionHandler : ICustomPermissionHandler
    {
        public Task<bool> ValidateAsync(AuthorizationFilterContext context) => Task.FromResult(true);
    }

    private sealed class PendingPermissionHandler : ICustomPermissionHandler
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> ValidateAsync(AuthorizationFilterContext context)
        {
            Entered.TrySetResult();
            return Decision.Task;
        }
    }

    private sealed class PermissionTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

[ApiController]
[Authorize]
[Route("__permission-filter-regression")]
public sealed class PermissionFilterProbeController(ConcurrentQueue<string> executions) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        executions.Enqueue("executed");
        return Ok();
    }
}
