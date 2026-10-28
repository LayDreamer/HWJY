using Refit;
using ZhonTai.Admin.Core.Attributes;
using ZhonTai.Admin.Core.Auth;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Services.Auth.Dto;

namespace ZhonTai.Admin.Services.Auth;

/// <summary>
/// 认证授权客户端接口
/// </summary>
[HttpClientContract(AdminConsts.AreaName)]
public interface IAuthClientService
{
    [Post("/api/admin/auth/login")]
    Task<TokenInfo> LoginAsync(AuthLoginInput input);

    [Post("/api/admin/auth/refresh")]
    Task<TokenInfo> Refresh([Body] AuthRefreshInput input);

    [Post("/api/admin/auth/logout")]
    Task Logout([Body] AuthRefreshInput input);
}
