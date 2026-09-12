using FreeSql.DataAnnotations;
using ZhonTai.Admin.Core.Entities;

namespace Company.MasterData.Contracts.Entities;

/// <summary>物料基础资料。</summary>
[Table(Name = "md_material")]
[Index("idx_md_material_code", nameof(MaterialCode), true)]
[Index("idx_md_material_name", nameof(MaterialName))]
[Index("idx_md_material_type", nameof(MaterialType))]
public class MaterialEntity : EntityTenant
{
    [Column(StringLength = 50)] public string MaterialCode { get; set; } = string.Empty;
    [Column(StringLength = 200)] public string MaterialName { get; set; } = string.Empty;
    [Column(StringLength = 500)] public string? Specification { get; set; }
    [Column(StringLength = 50)] public string MaterialType { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    [Column(StringLength = 50)] public string? Unit { get; set; }
    [Column(StringLength = 20)] public string Status { get; set; } = "Enabled";
    public bool IsPurchase { get; set; }
    public bool IsProduce { get; set; }
    public int LeadTime { get; set; }
    public decimal SafetyStock { get; set; }
    [Column(StringLength = 500)] public string? Remark { get; set; }
}
