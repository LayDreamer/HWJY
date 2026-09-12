using Company.MasterData.Contracts.DTO;
using ZhonTai.Admin.Core.Dto;

namespace Company.MasterData.Contracts.Services;

/// <summary>物料服务契约。</summary>
public interface IMaterialService
{
    Task<MaterialDto?> GetAsync(long id);
    Task<PageOutput<MaterialDto>> GetPageAsync(PageInput<MaterialQueryInput> input);
    Task<long> AddAsync(MaterialInput input);
    Task UpdateAsync(MaterialInput input);
    Task<bool> DeleteAsync(long id);
    Task SetStatusAsync(long id, string status);
}
