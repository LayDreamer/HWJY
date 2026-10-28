using ZhonTai.Admin.Core.Auth;
using ZhonTai.Admin.Services.Auth.Dto;

namespace ZhonTai.Admin.Services.Auth;

/// <summary>
/// 认证授权接口
/// </summary>
public interface IAuthService
{
    string GetToken(AuthLoginOutput user);

    Task<TokenInfo> GetTokenInfoAsync(AuthLoginOutput user);

    Task<AuthGetUserInfoOutput> GetUserInfoAsync();

    Task<AuthGetPasswordEncryptKeyOutput> GetPasswordEncryptKeyAsync();

    Task<TokenInfo> Refresh(AuthRefreshInput input);

    Task Logout(AuthRefreshInput input);
}
