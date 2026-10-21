using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Ameen.Erp.Application;
using Ameen.Erp.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Ameen.Erp.Api;

public static class ApiSupport
{
    public static Actor Actor(HttpContext context)
    {
        var subject = context.User.FindFirstValue("sub");
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject))
            throw new BusinessException("authenticated_subject_required", "هوية موثقة مطلوبة.", ErrorKind.Forbidden);
        return new Actor(subject, context.User.IsInRole("erp.platform-admin"));
    }
    public static string Key(HttpContext context)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        Check.That(values.Count == 1, "idempotency_key_required", "أرسل مفتاح طلب واحدًا في Idempotency-Key.");
        return Check.Text(values[0], "Idempotency-Key", 100);
    }
    public static DateOnly Date(string value)
    {
        Check.That(DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date), "invalid_date", "استخدم تاريخًا بالصيغة yyyy-MM-dd.");
        return date;
    }
    public static async Task ErrorMiddleware(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (context.Response.HasStarted) throw;
            var status = error switch
            {
                BusinessException business => business.Kind switch
                {
                    ErrorKind.Forbidden => 403, ErrorKind.NotFound => 404, ErrorKind.Conflict => 409, _ => 422
                },
                BadHttpRequestException => 400, SqlException => 503, _ => 500
            };
            var code = error is BusinessException b ? b.Code : status == 400 ? "invalid_request" : status == 503 ? "database_unavailable" : "internal_error";
            var detail = error is BusinessException ex ? ex.Message : status == 400 ? "صيغة الطلب غير صالحة." : "تعذر تنفيذ الطلب؛ استخدم معرف التتبع للمراجعة.";
            if (status >= 500)
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiErrors").LogError(error,
                    "Request failed: {TraceId}", context.TraceIdentifier);
            var problem = new ProblemDetails
            {
                Status = status, Type = "urn:ameen:errors:" + code, Title = code,
                Detail = detail, Instance = context.Request.Path
            };
            problem.Extensions["code"] = code;
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonDefaults.Options, context.RequestAborted);
        }
    }
}
