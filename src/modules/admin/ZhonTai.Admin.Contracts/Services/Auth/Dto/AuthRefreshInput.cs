using System.ComponentModel.DataAnnotations;

namespace ZhonTai.Admin.Services.Auth.Dto;

public class AuthRefreshInput
{
    [Required, StringLength(97, MinimumLength = 97)]
    public string RefreshToken { get; set; }
}
