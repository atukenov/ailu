namespace Ailu.Api.Infrastructure;

// Problem details with a stable machine-readable `code`, and a `detail` in the brand voice the
// app can show as is.
public static class Errors
{
    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, detail: message);

    public static IResult Unauthorized(string message) => Problem(StatusCodes.Status401Unauthorized, "unauthorized", message);
    public static IResult Forbidden(string code, string message) => Problem(StatusCodes.Status403Forbidden, code, message);
    public static IResult NotFound(string code, string message) => Problem(StatusCodes.Status404NotFound, code, message);
    public static IResult Conflict(string code, string message) => Problem(StatusCodes.Status409Conflict, code, message);
    public static IResult TooManyRequests(string message) => Problem(StatusCodes.Status429TooManyRequests, "too_many_requests", message);

    public static IResult NoCouple() => Problem(StatusCodes.Status409Conflict, "no_couple", "Сначала создайте пару.");

    private static IResult Problem(int status, string code, string message) =>
        Results.Problem(statusCode: status, detail: message, extensions: new Dictionary<string, object?> { ["code"] = code });
}
