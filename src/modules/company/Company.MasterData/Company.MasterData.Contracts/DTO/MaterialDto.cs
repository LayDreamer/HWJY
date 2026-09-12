namespace Company.MasterData.Contracts.DTO;

/// <summary>物料详情及列表输出。</summary>
public class MaterialDto
{
    public long Id { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string? Specification { get; set; }
    public string MaterialType { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    public string? Unit { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsPurchase { get; set; }
    public bool IsProduce { get; set; }
    public int LeadTime { get; set; }
    public decimal SafetyStock { get; set; }
    public string? Remark { get; set; }
    public long? TenantId { get; set; }
    public DateTime? CreatedTime { get; set; }
    public DateTime? ModifiedTime { get; set; }
}
