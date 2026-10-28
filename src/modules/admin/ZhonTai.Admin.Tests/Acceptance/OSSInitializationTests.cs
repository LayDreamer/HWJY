using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnceMi.AspNetCore.OSS;
using Xunit;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Extensions;
using OSSOptions = ZhonTai.Admin.Core.Configs.OSSOptions;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class OSSInitializationTests
{
    private const string AuthenticatedPolicy = """
        {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":["arn:aws:iam::123456789012:root"]},"Action":["s3:GetObject"],"Resource":["arn:aws:s3:::oss-regression/*"]}]}
        """;

    [Fact]
    public void DisabledOSSRegistersNoBucketInitializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOSS(new OSSConfig { OSSConfigs = [new OSSOptions { Enable = false }] });
        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.NotNull(provider.GetRequiredService<IOSSServiceFactory>());
    }

    [Fact]
    public async Task RegistrationDoesNotContactStorageAndStartupCreatesPrivateBucket()
    {
        await using var storage = await StorageProtocolServer.StartAsync(bucketExists: false);
        using var services = CreateServices(storage);
        Assert.Empty(storage.Requests);
        await Initializer(services).StartAsync(CancellationToken.None);
        Assert.True(storage.BucketExists);
        Assert.Null(storage.Policy);
        Assert.Contains("PUT bucket", storage.Requests);
        Assert.DoesNotContain("PUT policy", storage.Requests);
        Assert.DoesNotContain("DELETE policy", storage.Requests);
    }

    [Fact]
    public async Task PrivateBucketPreservesAuthenticatedPolicy()
    {
        await using var storage = await StorageProtocolServer.StartAsync(policy: AuthenticatedPolicy);
        using var services = CreateServices(storage);
        await Initializer(services).StartAsync(CancellationToken.None);
        Assert.Equal(AuthenticatedPolicy, storage.Policy);
        Assert.DoesNotContain("DELETE policy", storage.Requests);
        Assert.DoesNotContain("PUT policy", storage.Requests);
    }

    [Theory]
    [InlineData("\"*\"")]
    [InlineData("{\"AWS\":\"*\"}")]
    [InlineData("{\"AWS\":[\"*\"]}")]
    public async Task PrivateBucketRevokesLegacyAnonymousPolicy(string principal)
    {
        var policy = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":" + principal + ",\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::oss-regression/*.*\"]}]}";
        await using var storage = await StorageProtocolServer.StartAsync(policy: policy);
        using var services = CreateServices(storage);
        await Initializer(services).StartAsync(CancellationToken.None);
        Assert.Null(storage.Policy);
        Assert.Contains("DELETE policy", storage.Requests);
    }

    [Fact]
    public async Task PrivateBucketRemovesWildcardButPreservesNamedPrincipalAndDeny()
    {
        var policy = JsonNode.Parse(AuthenticatedPolicy);
        policy["Statement"][0]["Principal"]["AWS"].AsArray().Add("*");
        policy["Statement"].AsArray().Add(JsonNode.Parse("""
            {"Effect":"Deny","Principal":"*","Action":"s3:DeleteObject","Resource":"arn:aws:s3:::oss-regression/*"}
            """));
        await using var storage = await StorageProtocolServer.StartAsync(policy: policy.ToJsonString());
        using var services = CreateServices(storage);
        await Initializer(services).StartAsync(CancellationToken.None);
        var statements = JsonNode.Parse(storage.Policy)["Statement"].AsArray();
        Assert.Equal(2, statements.Count);
        Assert.Equal("arn:aws:iam::123456789012:root", Assert.Single(statements[0]["Principal"]["AWS"].AsArray()).GetValue<string>());
        Assert.Equal("Deny", statements[1]["Effect"].GetValue<string>());
        Assert.DoesNotContain("DELETE policy", storage.Requests);
    }

    [Fact]
    public async Task ExplicitPublicReadPreservesOtherGrantsAndIncludesExtensionlessObjects()
    {
        await using var storage = await StorageProtocolServer.StartAsync(policy: AuthenticatedPolicy);
        using var services = CreateServices(storage, publicRead: true);
        await Initializer(services).StartAsync(CancellationToken.None);
        var statements = JsonNode.Parse(storage.Policy)["Statement"].AsArray();
        Assert.Contains(statements, statement => JsonNode.DeepEquals(statement, JsonNode.Parse(AuthenticatedPolicy)["Statement"][0]));
        var publicGrant = Assert.Single(statements, statement => statement["Principal"] is JsonValue value && value.GetValue<string>() == "*");
        Assert.Equal("s3:GetObject", publicGrant["Action"].GetValue<string>());
        Assert.Equal("arn:aws:s3:::oss-regression/*", publicGrant["Resource"].GetValue<string>());
        await Initializer(services).StartAsync(CancellationToken.None);
        Assert.Single(storage.Requests, request => request == "PUT policy");
    }

    [Fact]
    public async Task CancellationStopsPendingBucketCheckWithoutCreatingBucket()
    {
        await using var storage = await StorageProtocolServer.StartAsync(bucketExists: false, pauseBucketCheck: true);
        using var services = CreateServices(storage);
        using var cancellation = new CancellationTokenSource();
        var start = Initializer(services).StartAsync(cancellation.Token);
        await storage.BucketCheckEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(start.IsCompleted);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.DoesNotContain("PUT bucket", storage.Requests);
        }
        finally
        {
            storage.ReleaseBucketCheck.TrySetResult();
            try { await start; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task PolicyAccessFailureFailsStartupWithoutChangingPermissions()
    {
        await using var storage = await StorageProtocolServer.StartAsync(policy: AuthenticatedPolicy, denyPolicyAccess: true);
        using var services = CreateServices(storage);
        var error = await Assert.ThrowsAnyAsync<Minio.Exceptions.MinioException>(() => Initializer(services).StartAsync(CancellationToken.None));
        Assert.Equal("AccessDenied", error.Response.Code);
        Assert.Equal(AuthenticatedPolicy, storage.Policy);
        Assert.DoesNotContain("PUT policy", storage.Requests);
        Assert.DoesNotContain("DELETE policy", storage.Requests);
    }

    private static ServiceProvider CreateServices(StorageProtocolServer storage, bool publicRead = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOSS(new OSSConfig
        {
            OSSConfigs = [new OSSOptions { Enable = false, Provider = OSSProvider.Aliyun, Endpoint = "127.0.0.1:1" }, new OSSOptions
            {
                Enable = true,
                Provider = OSSProvider.Minio,
                Endpoint = storage.Endpoint,
                AccessKey = "protocol-test-access",
                SecretKey = "protocol-test-secret",
                Region = "us-east-1",
                BucketName = "oss-regression",
                PublicRead = publicRead
            }]
        });
        return services.BuildServiceProvider();
    }

    private static IHostedService Initializer(IServiceProvider services) => Assert.Single(services.GetServices<IHostedService>());

    // A loopback S3 protocol boundary exercises the actual installed Minio SDK;
    // it does not claim to replace acceptance against a deployed object-storage service.
    private sealed class StorageProtocolServer : IAsyncDisposable
    {
        private WebApplication _app;
        public string Endpoint { get; private set; }
        public bool BucketExists { get; private set; }
        public string Policy { get; private set; }
        public ConcurrentQueue<string> Requests { get; } = new();
        public TaskCompletionSource BucketCheckEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBucketCheck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<StorageProtocolServer> StartAsync(bool bucketExists = true, string policy = null, bool pauseBucketCheck = false, bool denyPolicyAccess = false)
        {
            var server = new StorageProtocolServer { BucketExists = bucketExists, Policy = policy };
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            server._app = builder.Build();
            server._app.Run(async context =>
            {
                var isPolicy = context.Request.Query.ContainsKey("policy");
                server.Requests.Enqueue(context.Request.Method + (isPolicy ? " policy" : " bucket"));
                context.Response.Headers["x-amz-request-id"] = "oss-protocol-regression";
                if (context.Request.Method == "HEAD")
                {
                    server.BucketCheckEntered.TrySetResult();
                    if (pauseBucketCheck) await server.ReleaseBucketCheck.Task;
                    context.Response.StatusCode = server.BucketExists ? 200 : 404;
                }
                else if (isPolicy && (denyPolicyAccess || (context.Request.Method == "GET" && server.Policy == null)))
                {
                    context.Response.StatusCode = denyPolicyAccess ? 403 : 404;
                    context.Response.ContentType = "application/xml";
                    await context.Response.WriteAsync("<Error><Code>" + (denyPolicyAccess ? "AccessDenied" : "NoSuchBucketPolicy") + "</Code><Message>protocol test</Message><Resource>/oss-regression</Resource><RequestId>oss-protocol-regression</RequestId></Error>");
                }
                else if (isPolicy && context.Request.Method == "GET")
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(server.Policy);
                }
                else if (isPolicy && context.Request.Method == "PUT")
                {
                    using var reader = new System.IO.StreamReader(context.Request.Body);
                    server.Policy = await reader.ReadToEndAsync();
                    context.Response.StatusCode = 204;
                }
                else if (isPolicy && context.Request.Method == "DELETE")
                {
                    server.Policy = null;
                    context.Response.StatusCode = 204;
                }
                else if (!isPolicy && context.Request.Method == "PUT")
                {
                    server.BucketExists = true;
                    context.Response.StatusCode = 200;
                }
                else context.Response.StatusCode = 400;
            });
            await server._app.StartAsync();
            server.Endpoint = new Uri(server._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single()).Authority;
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseBucketCheck.TrySetResult();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
