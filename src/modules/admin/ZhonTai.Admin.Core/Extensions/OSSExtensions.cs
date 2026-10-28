using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minio;
using Minio.Exceptions;
using OnceMi.AspNetCore.OSS;
using System.Text.Json.Nodes;
using ZhonTai.Admin.Core.Configs;
using OSSOptions = ZhonTai.Admin.Core.Configs.OSSOptions;

namespace ZhonTai.Admin.Core.Extensions;

/// <summary>
/// OSS扩展
/// </summary>
public static class OSSExtensions
{
    /// <summary>
    /// 添加OSS
    /// </summary>
    public static IServiceCollection AddOSS(this IServiceCollection services, OSSConfig config)
    {
        var enabledOptions = config.OSSConfigs?.Where(option => option.Enable).ToArray() ?? [];
        if (enabledOptions.Length == 0)
        {
            services.AddOSSService(option => option.Provider = OSSProvider.Invalid);
            return services;
        }

        foreach (var ossOptions in enabledOptions)
        {
            services.AddOSSService(ossOptions.Provider.ToString(), option =>
            {
                option.Provider = ossOptions.Provider;
                option.Endpoint = ossOptions.Endpoint;
                option.Region = ossOptions.Region;
                option.AccessKey = ossOptions.AccessKey;
                option.SecretKey = ossOptions.SecretKey;
                option.IsEnableHttps = ossOptions.IsEnableHttps;
                option.IsEnableCache = ossOptions.IsEnableCache;
            });
        }

        services.AddSingleton<IHostedService>(provider =>
            new OSSBucketInitializer(provider.GetRequiredService<IOSSServiceFactory>(), enabledOptions));
        return services;
    }
}

internal sealed class OSSBucketInitializer(IOSSServiceFactory factory, IReadOnlyList<OSSOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var option in options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (option.Provider == OSSProvider.Minio)
            {
                await InitializeMinioAsync(option, cancellationToken);
                continue;
            }

            var service = factory.Create(option.Provider.ToString());
            if (!await service.BucketExistsAsync(option.BucketName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await service.CreateBucketAsync(option.BucketName);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task InitializeMinioAsync(OSSOptions option, CancellationToken cancellationToken)
    {
        var builder = new MinioClient()
            .WithEndpoint(option.Endpoint)
            .WithCredentials(option.AccessKey, option.SecretKey)
            .WithSSL(option.IsEnableHttps);
        if (option.Region.NotNull())
            builder.WithRegion(option.Region);

        using var client = builder.Build();
        if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(option.BucketName), cancellationToken))
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(option.BucketName), cancellationToken);

        string currentPolicy = null;
        try
        {
            currentPolicy = await client.GetPolicyAsync(
                new GetPolicyArgs().WithBucket(option.BucketName), cancellationToken);
        }
        catch (MinioException exception) when (exception.Response?.Code == "NoSuchBucketPolicy")
        {
            // Minio 5 may surface this code through different MinioException subtypes.
        }

        var policy = currentPolicy.NotNull()
            ? JsonNode.Parse(currentPolicy).AsObject()
            : new JsonObject { ["Version"] = "2012-10-17", ["Statement"] = new JsonArray() };
        var originalPolicy = policy.DeepClone();
        if (policy["Statement"] is JsonObject singleStatement)
            policy["Statement"] = new JsonArray(singleStatement.DeepClone());
        var statements = policy["Statement"].AsArray();
        foreach (var statement in statements.ToArray())
        {
            if (statement?["Effect"]?.GetValue<string>() != "Allow") continue;
            var principal = statement["Principal"];
            if (IsWildcard(principal))
            {
                statements.Remove(statement);
            }
            else if (principal is JsonObject principals && principals["AWS"] is JsonNode aws)
            {
                if (IsWildcard(aws)) principals.Remove("AWS");
                else if (aws is JsonArray accounts)
                {
                    foreach (var account in accounts.Where(IsWildcard).ToArray()) accounts.Remove(account);
                    if (accounts.Count == 0) principals.Remove("AWS");
                }
                if (principals.Count == 0) statements.Remove(statement);
            }
        }

        if (option.PublicRead)
        {
            statements.Add(new JsonObject
            {
                ["Effect"] = "Allow",
                ["Principal"] = "*",
                ["Action"] = "s3:GetObject",
                ["Resource"] = $"arn:aws:s3:::{option.BucketName}/*"
            });
        }

        if (JsonNode.DeepEquals(originalPolicy, policy)) return;
        if (statements.Count == 0)
            await client.RemovePolicyAsync(new RemovePolicyArgs().WithBucket(option.BucketName), cancellationToken);
        else
            await client.SetPolicyAsync(
                new SetPolicyArgs().WithBucket(option.BucketName).WithPolicy(policy.ToJsonString()), cancellationToken);
    }

    private static bool IsWildcard(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text == "*";
}
