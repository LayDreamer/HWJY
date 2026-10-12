using System.Data;
using Castle.DynamicProxy;
using FreeSql;
using ZhonTai.Admin.Core.Attributes;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Dto;

namespace ZhonTai.Admin.Core.Db.Transaction;

/// <summary>
/// 事务拦截器
/// </summary>
public class TransactionAsyncInterceptor : IAsyncInterceptor
{
    private readonly UnitOfWorkManagerCloud _unitOfWorkManagerCloud;
    private readonly DbConfig _dbConfig;

    public TransactionAsyncInterceptor(UnitOfWorkManagerCloud unitOfWorkManagerCloud, DbConfig dbConfig)
    {
        _unitOfWorkManagerCloud = unitOfWorkManagerCloud;
        _dbConfig = dbConfig;
    }

    private IUnitOfWork Begin(IInvocation invocation)
    {
        var method = invocation.MethodInvocationTarget ?? invocation.Method;
        var attribute = method.GetCustomAttributes(typeof(TransactionAttribute), false).FirstOrDefault();
        if (attribute is TransactionAttribute transaction)
        {
            IsolationLevel? isolationLevel = transaction.IsolationLevel == 0 ? null : transaction.IsolationLevel;
            var dbKey = transaction.DbKey;
            if (dbKey.IsNull())
            {
                dbKey = _dbConfig.Key;
            }
            return _unitOfWorkManagerCloud.Begin(dbKey, transaction.Propagation, isolationLevel);
        }

        return null;
    }

    private async Task InternalInterceptAsynchronous(IInvocation invocation, IUnitOfWork unitOfWork)
    {
        try
        {
            invocation.Proceed();
            //处理Task返回一个null值的情况会导致空指针
            if (invocation.ReturnValue != null)
            {
                await (Task)invocation.ReturnValue;
            }
            unitOfWork.Commit();
        }
        catch (System.Exception)
        {
            unitOfWork.Rollback();
            throw;
        }
        finally
        {
            unitOfWork.Dispose();
        }
    }

    private async Task<TResult> InternalInterceptAsynchronous<TResult>(IInvocation invocation)
    {
        TResult result;
        var unitOfWork = Begin(invocation);
        if (unitOfWork != null)
        {
            try
            {
                invocation.Proceed();
                result = await (Task<TResult>)invocation.ReturnValue;
                if (result is IResultOutput res && !res.Success)
                {
                    unitOfWork.Rollback();
                }
                else
                {
                    unitOfWork.Commit();
                }
            }
            catch (System.Exception)
            {
                unitOfWork.Rollback();
                throw;
            }
            finally
            {
                unitOfWork.Dispose();
            }
        }
        else
        {
            invocation.Proceed();
            result = await (Task<TResult>)invocation.ReturnValue;
        }
        return result;
    }

    /// <summary>
    /// 拦截同步执行的方法
    /// </summary>
    /// <param name="invocation"></param>
    public void InterceptSynchronous(IInvocation invocation)
    {
        var unitOfWork = Begin(invocation);
        if (unitOfWork != null)
        {
            try
            {
                invocation.Proceed();
                var result = invocation.ReturnValue;
                if (result is IResultOutput res && !res.Success)
                {
                    unitOfWork.Rollback();
                }
                else
                {
                    unitOfWork.Commit();
                }
            }
            catch
            {
                unitOfWork.Rollback();
                throw;
            }
            finally
            {
                unitOfWork.Dispose();
            }
        }
        else
        {
            invocation.Proceed();
        }
    }

    /// <summary>
    /// 拦截返回结果
    /// </summary>
    /// <param name="invocation"></param>
    public void InterceptAsynchronous(IInvocation invocation)
    {
        var unitOfWork = Begin(invocation);
        if (unitOfWork != null)
        {
            invocation.ReturnValue = InternalInterceptAsynchronous(invocation, unitOfWork);
        }
        else
        {
            invocation.Proceed();
        }
    }

    /// <summary>
    /// 拦截返回结果
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    /// <param name="invocation"></param>
    public void InterceptAsynchronous<TResult>(IInvocation invocation)
    {
        invocation.ReturnValue = InternalInterceptAsynchronous<TResult>(invocation);
    }
}
