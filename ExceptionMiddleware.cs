using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;
using Rinha2024.Dotnet.Exceptions;

namespace Rinha2024.Dotnet;

public class ExceptionMiddleware : IMiddleware
{
    
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException _)
        {
            context.Response.StatusCode = (int) HttpStatusCode.UnprocessableEntity;
            context.Response.Body = new MemoryStream([]);
        }
        catch
        {
            throw;
        }
    }
}