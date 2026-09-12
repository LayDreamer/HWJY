namespace Company.MasterData.Contracts.DTO;

/// <summary>物料分页查询条件。</summary>
public class MaterialQueryInput
{
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string? MaterialType { get; set; }
    public string? Status { get; set; }
}
