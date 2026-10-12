using System;
using System.Linq;
using System.Threading.Tasks;
using ProtoBuf.Grpc;
using Xunit;
using ZhonTai.Admin.Core.Attributes;
using ZhonTai.Admin.Core.Auth;
using ZhonTai.Admin.Core.GrpcServices;
using ZhonTai.Admin.Core.GrpcServices.Dtos;
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

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private sealed class PermissionService(UserGetPermissionGrpcOutput permissions) : IUserGrpcService
    {
        public Task<UserGetPermissionGrpcOutput> GetPermissionAsync(CallContext context = default) => Task.FromResult(permissions);
        public Task GetDataPermissionAsync(ProtoString apiPath, CallContext context = default) => Task.CompletedTask;
    }
}
