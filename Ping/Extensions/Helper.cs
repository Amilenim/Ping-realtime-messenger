using Microsoft.AspNetCore.Http;

public static class HttpRequestExtensions
{
    public static bool IsAjaxRequest(this HttpRequest request)
    {
        if (request == null) return false;
        return request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }
}