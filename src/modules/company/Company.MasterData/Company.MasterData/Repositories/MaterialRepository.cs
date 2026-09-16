using Company.MasterData.Entities;
using ZhonTai.Admin.Core.Db.Transaction;

namespace Company.MasterData.Repositories;

public class MaterialRepository(UnitOfWorkManagerCloud unitOfWorkManager)
    : MasterDataRepositoryBase<MaterialEntity>(unitOfWorkManager);
