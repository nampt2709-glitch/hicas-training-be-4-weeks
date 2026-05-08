using Microsoft.AspNetCore.Http;

namespace ApartmentAPI;

// Ngoại lệ có mã HTTP + mã lỗi + thông điệp cho client — xử lý tại GlobalExceptionHandler.
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string ClientMessage { get; }

    public ApiException(int statusCode, string errorCode, string clientMessage, Exception? inner = null)
        : base(clientMessage, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ClientMessage = clientMessage;
    }

    public static ApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    public static ApiException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);
}

// Mã lỗi ổn định cho client.
public static class ApiErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION";
    public const string Conflict = "CONFLICT";
}

public static class ApiMessages
{
    public const string NotFound = "Resource not found.";
    public const string ValidationFailed = "Validation failed.";
    public const string Ok = "Success.";
}
