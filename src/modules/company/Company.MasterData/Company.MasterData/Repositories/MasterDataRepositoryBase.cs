using ZhonTai.Admin.Core.Db.Transaction;
using ZhonTai.Admin.Core.Repositories;
using Company.MasterData.Core.Consts;

namespace Company.MasterData.Repositories;

public class MasterDataRepositoryBase<TEntity>(UnitOfWorkManagerCloud unitOfWorkManager)
    : RepositoryBase<TEntity>(DbKeys.MasterDataDb, unitOfWorkManager) where TEntity : class;
