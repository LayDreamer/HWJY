namespace ZhonTai.Admin.Core.Exceptions;

/// <summary>Stable, searchable P0 error codes shared by HTTP error responses and logs.</summary>
public static class P0ErrorCodes
{
    public const string Unauthorized = "P0.Unauthorized";
    public const string Validation = "P0.Validation";
    public const string Forbidden = "P0.Forbidden";
    public const string NotFound = "P0.NotFound";
    public const string Conflict = "P0.Conflict";
    public const string Business = "P0.Business";
    public const string Unexpected = "P0.Unexpected";
}
