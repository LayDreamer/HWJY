using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FreeSql.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using ZhonTai.Admin.Domain.User.Dto;
using ZhonTai.Admin.Core.Entities;
using ZhonTai.Admin.Repositories;
using ZhonTai.Admin.Services;
using ZhonTai.DynamicApi;
using ZhonTai.DynamicApi.Attributes;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>
/// 仅供 P0 隔离验收库使用的技术实体。它不属于生产业务模型或发布 Manifest。
/// </summary>
[Table(Name = "acc_p0_data_permission_fixture")]
[Index("uk_acc_p0_data_permission_fixture_code", nameof(RecordCode), true)]
public sealed class P0DataPermissionFixtureEntity : EntityData
{
    [Column(StringLength = 64)]
    public string RecordCode { get; set; } = string.Empty;

    [Column(StringLength = 200)]
    public string RecordName { get; set; } = string.Empty;
}

public class P0DataPermissionFixtureAddInput
{
    [Required]
    public string RecordCode { get; set; } = string.Empty;

    [Required]
    public string RecordName { get; set; } = string.Empty;
}

public sealed class P0DataPermissionFixtureUpdateInput : P0DataPermissionFixtureAddInput
{
    public long Id { get; set; }
}

public sealed class P0DataPermissionFixtureOutput
{
    public long Id { get; set; }
    public long? OwnerId { get; set; }
    public long? OwnerOrgId { get; set; }
    public string RecordCode { get; set; } = string.Empty;
    public string RecordName { get; set; } = string.Empty;
}

/// <summary>
/// T3 的真实动态 API。Program.cs 仅在 P0DataPermissionAcceptance + 显式开关同时成立时装配本类型。
/// </summary>
[DynamicApi(Area = "admin")]
public sealed class P0DataPermissionFixtureService : BaseService, IDynamicApi
{
    private readonly AdminRepositoryBase<P0DataPermissionFixtureEntity> _repository;

    public P0DataPermissionFixtureService(AdminRepositoryBase<P0DataPermissionFixtureEntity> repository)
    {
        _repository = repository;
    }

    [HttpPost]
    public async Task<long> AddAsync(P0DataPermissionFixtureAddInput input)
    {
        var entity = new P0DataPermissionFixtureEntity
        {
            RecordCode = input.RecordCode,
            RecordName = input.RecordName,
            OwnerId = User.Id,
            OwnerOrgId = User.DataPermission.OrgId,
            OwnerOrgName = User.DataPermission.OrgName
        };
        await _repository.InsertAsync(entity);
        return entity.Id;
    }

    [HttpPost]
    public Task ThrowUnexpectedAsync() => throw new InvalidOperationException("P0 acceptance fixture unexpected failure.");

    public async Task<P0DataPermissionFixtureOutput> GetAsync(long id)
    {
        return await _repository.GetAsync<P0DataPermissionFixtureOutput>(id);
    }

    [HttpPost]
    public async Task<List<P0DataPermissionFixtureOutput>> GetListAsync()
    {
        return await _repository.Select
            .OrderBy(entity => entity.Id)
            .ToListAsync<P0DataPermissionFixtureOutput>();
    }

    public DataPermissionOutput GetDataPermission()
    {
        return User.DataPermission;
    }

    [HttpPut]
    public async Task<bool> UpdateAsync(P0DataPermissionFixtureUpdateInput input)
    {
        var entity = await _repository.GetAsync(input.Id);
        if (entity == null)
        {
            return false;
        }

        entity.RecordCode = input.RecordCode;
        entity.RecordName = input.RecordName;
        await _repository.UpdateAsync(entity);
        return true;
    }

    [HttpDelete]
    public async Task<bool> DeleteAsync(long id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return false;
        }

        await _repository.DeleteAsync(id);
        return true;
    }
}
