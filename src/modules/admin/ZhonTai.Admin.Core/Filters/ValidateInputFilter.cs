using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using System.Text;
using ZhonTai.Admin.Core.Dto;
using ZhonTai.Admin.Core.Exceptions;
using ZhonTai.Admin.Core.Middlewares;

namespace ZhonTai.Admin.Core.Filters;

/// <summary>
/// 输入模型验证过滤器
/// </summary>
public class ValidateInputFilter : IAsyncActionFilter
{
    public ValidateInputFilter()
    {
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid)
        {
            try
            {
                var logger = (ILogger<ValidateInputFilter>)context.HttpContext.RequestServices.GetService(typeof(ILogger<ValidateInputFilter>));
                var errors = string.Join("|", context.ModelState
                .Where(m => m.Value.ValidationState == ModelValidationState.Invalid)
                .Select(m =>
                {
                    var sb = new StringBuilder();
                    sb.AppendFormat("{0}：", m.Key);
                    sb.Append(string.Join(";", m.Value.Errors.Select(n => n.ErrorMessage)));
                    return sb.ToString();
                }));
                logger.LogError(errors);
                context.Result = new JsonResult(new ResultOutput<string>
                {
                    Code = P0ErrorCodes.Validation,
                    TraceId = P0RequestContextMiddleware.GetTraceId(context.HttpContext)
                }.NotOk(errors));
            }
            catch
            {
                context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            return;
        }
        
        await next();
    }
}
