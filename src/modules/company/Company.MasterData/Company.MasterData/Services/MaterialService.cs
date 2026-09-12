using Company.MasterData.Contracts.DTO;
using Company.MasterData.Contracts.Entities;
using Company.MasterData.Contracts.Services;
using Company.MasterData.Core.Consts;
using Company.MasterData.Repositories;
using Microsoft.AspNetCore.Mvc;
using ZhonTai.Admin.Core.Dto;
using ZhonTai.Admin.Services;
using ZhonTai.DynamicApi;
using ZhonTai.DynamicApi.Attributes;

namespace Company.MasterData.Services;

/// <summary>物料基础资料服务。</summary>
[DynamicApi(Area = ApiConsts.AreaName)]
public class MaterialService(MaterialRepository materialRepository) : BaseService, IMaterialService, IDynamicApi
{
    [HttpGet]
    public async Task<MaterialDto?> GetAsync(long id) => await materialRepository.GetAsync<MaterialDto>(id);

    [HttpPost]
    public async Task<PageOutput<MaterialDto>> GetPageAsync(PageInput<MaterialQueryInput> input)
    {
        var filter = input.Filter;
        var list = await materialRepository.Select
            .WhereDynamicFilter(input.DynamicFilter)
            .WhereIf(!string.IsNullOrWhiteSpace(filter?.MaterialCode), item => item.MaterialCode.Contains(filter!.MaterialCode!))
            .WhereIf(!string.IsNullOrWhiteSpace(filter?.MaterialName), item => item.MaterialName.Contains(filter!.MaterialName!))
            .WhereIf(!string.IsNullOrWhiteSpace(filter?.MaterialType), item => item.MaterialType == filter!.MaterialType)
            .WhereIf(!string.IsNullOrWhiteSpace(filter?.Status), item => item.Status == filter!.Status)
            .Count(out var total)
            .OrderByDescending(item => item.Id)
            .Page(input.CurrentPage, input.PageSize)
            .ToListAsync<MaterialDto>();
        return new PageOutput<MaterialDto> { List = list, Total = total };
    }

    [HttpPost]
    public async Task<long> AddAsync(MaterialInput input)
    {
        if (await materialRepository.Select.AnyAsync(item => item.MaterialCode == input.MaterialCode))
            throw ResultOutput.Exception("物料编码已存在");
        var entity = Mapper.Map<MaterialEntity>(input);
        return (await materialRepository.InsertAsync(entity)).Id;
    }

    [HttpPut]
    public async Task UpdateAsync(MaterialInput input)
    {
        if (!(input.Id > 0)) throw ResultOutput.Exception("请选择物料");
        var entity = await materialRepository.GetAsync(input.Id.Value);
        if (entity is null) throw ResultOutput.Exception("物料不存在");
        if (await materialRepository.Select.AnyAsync(item => item.Id != entity.Id && item.MaterialCode == input.MaterialCode))
            throw ResultOutput.Exception("物料编码已存在");
        Mapper.Map(input, entity);
        await materialRepository.UpdateAsync(entity);
    }

    [HttpDelete]
    public async Task<bool> DeleteAsync(long id) => await materialRepository.SoftDeleteAsync(id);

    [HttpPut]
    public async Task SetStatusAsync(long id, string status)
    {
        var entity = await materialRepository.GetAsync(id) ?? throw ResultOutput.Exception("物料不存在");
        entity.Status = status;
        await materialRepository.UpdateAsync(entity);
    }
}
