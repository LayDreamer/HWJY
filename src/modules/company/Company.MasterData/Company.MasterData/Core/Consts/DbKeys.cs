using ZhonTai.Admin.Core;
using ZhonTai.Admin.Core.Configs;

namespace Company.MasterData.Core.Consts;

public static class DbKeys
{
    public static string MasterDataDb { get; set; } = AppInfo.GetOptions<DbConfig>()?.Key ?? "masterdatadb";
}
