using System.ComponentModel.DataAnnotations;

namespace Company.MasterData.Contracts.DTO;

/// <summary>物料新增或修改输入。</summary>
public class MaterialInput
{
    public long? Id { get; set; }
    [Required(ErrorMessage = "物料编码不能为空")]
    [StringLength(50)] public string MaterialCode { get; set; } = string.Empty;
    [Required(ErrorMessage = "物料名称不能为空")]
    [StringLength(200)] public string MaterialName { get; set; } = string.Empty;
    [StringLength(500)] public string? Specification { get; set; }
    [Required(ErrorMessage = "物料类型不能为空")]
    [StringLength(50)] public string MaterialType { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    [StringLength(50)] public string? Unit { get; set; }
    [Required] public string Status { get; set; } = "Enabled";
    public bool IsPurchase { get; set; }
    public bool IsProduce { get; set; }
    [Range(0, int.MaxValue)] public int LeadTime { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal SafetyStock { get; set; }
    [StringLength(500)] public string? Remark { get; set; }
}
