using FreeSql.DataAnnotations;
using ZhonTai.Admin.Core.Entities;

namespace Company.MasterData.Entities;

/// <summary>
/// 物料基础资料的内部持久化实体。
/// <para>表 md_material 的 DDL 与审计字段由 Company.MasterData 独占维护；不得作为跨模块 Contract 使用。</para>
/// </summary>
[Table(Name = "md_material")]
[Index("idx_md_material_code", nameof(MaterialCode), true)]
[Index("idx_md_material_name", nameof(MaterialName))]
[Index("idx_md_material_type", nameof(MaterialType))]
[Index("uk_md_material_source_system_source_id", nameof(SourceSystem) + "," + nameof(SourceId), true)]
public class MaterialEntity : EntityTenantWithData
{
    [Column(StringLength = 32, IsNullable = false)] public string SourceSystem { get; set; } = string.Empty;
    [Column(StringLength = 128, IsNullable = false)] public string SourceId { get; set; } = string.Empty;
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
