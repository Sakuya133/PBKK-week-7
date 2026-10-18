using PosBackend;

namespace PosServer;

internal static class ApiReply
{
    public static IResult From<T>(ServiceResult<T> result) => Results.Json(result, statusCode: Status(result.Error));
    public static Task Fail(HttpContext context, ErrorCode code, string message)
    {
        context.Response.StatusCode = Status(code);
        return context.Response.WriteAsJsonAsync(ServiceResult<bool>.Fail(code, message));
    }
    private static int Status(ErrorCode code) => code switch
    {
        ErrorCode.None => 200,
        ErrorCode.Validation => 400,
        ErrorCode.Unauthorized => 401,
        ErrorCode.Forbidden => 403,
        ErrorCode.NotFound => 404,
        ErrorCode.Conflict => 409,
        ErrorCode.RateLimited => 429,
        ErrorCode.DatabaseUnavailable or ErrorCode.ServiceUnavailable => 503,
        _ => 500
    };
}
