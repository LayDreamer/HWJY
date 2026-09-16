namespace Company.MasterData.Core.Consts;

public static class DbKeys
{
    /// <summary>
    /// 基础资料与统一平台 API 共用主业务库；不再读取独立 Host 配置或静默回退到其他键。
    /// </summary>
    public static string MasterDataDb => ZhonTai.Admin.Core.Consts.DbKeys.AdminDb;
}
