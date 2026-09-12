using FreeSql;
using ZhonTai.Admin.Core.Consts;

namespace ZhonTai.Admin.Tools.TaskScheduler;

/// <summary>
/// TaskScheduler配置
/// </summary>
public class TaskSchedulerOptions
{
    /// <summary>
    /// 是否执行持久化任务。API 宿主应关闭，独立 Worker 宿主应开启。
    /// </summary>
    public bool StartPersistentTasks { get; set; } = true;

    /// <summary>
    /// 数据库键
    /// </summary>
    public string DbKey { get; set; } = DbKeys.AdminDb;

    /// <summary>
    /// 数据库实例
    /// </summary>
    public IFreeSql FreeSql { get; set; }

    /// <summary>
    /// 多库实例
    /// </summary>
    public FreeSqlCloud FreeSqlCloud { get; set; }

    /// <summary>
    /// 配置FreeSql
    /// </summary>
    public Action<IFreeSql> ConfigureFreeSql { get; set; }

    /// <summary>
    /// 配置FreeSql
    /// </summary>
    public Action<FreeSchedulerBuilder> ConfigureFreeSchedulerBuilder { get; set; }
}
