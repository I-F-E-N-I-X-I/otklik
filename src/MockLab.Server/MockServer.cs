using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using MockLab.Core;

namespace MockLab.Server;
public record LogEntry(Guid Id, DateTimeOffset ReceivedAt, string Method, string Url, Dictionary<string, string> Headers, string Body, bool Truncated, int Status, string ResponsePreview, long DurationMs, string? RouteId, string? ResponseId, string Reason, string? Error, int? Step)
{
    public string Summary => $"{Status}  {Method} {Url}  · {DurationMs} мс";
}

public sealed class MockServer : IAsyncDisposable
{
    WebApplication? app;
    Router? router;
    CancellationTokenSource? shutdown;
    readonly ConcurrentQueue<LogEntry> logs = new();
    public event Action<LogEntry>? Logged;
    public bool Running => app != null;
    public IReadOnlyList<LogEntry> Logs => logs.ToArray();

    public void Clear() => logs.Clear();
    public void Reset() => router?.Reset();
    public async Task StartAsync(Project source)
    {
        if (Running)
            throw new Exception("Сервер уже запущен");
        var project = ProjectFiles.Clone(source);
        shutdown = new();
        router = new(project);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{project.Port}");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1048576);
        var host = builder.Build();
        host.Run(ctx => Handle(ctx, project));
        try
        {
            await host.StartAsync();
            app = host;
        }
        catch (Exception ex)
        {
            await host.DisposeAsync();
            throw new Exception("Не удалось запустить сервер. Возможно, порт занят. " + ex.Message, ex);
        }
    }

    async Task Handle(HttpContext ctx, Project p)
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, shutdown!.Token);
        var cancellation = requestCancellation.Token;
        var watch = Stopwatch.StartNew();
        var at = DateTimeOffset.UtcNow;
        var body = "";
        var output = "";
        Selection? s = null;
        string? error = null;
        var status = 200;
        try
        {
            if (p.Cors)
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";
            }

            if (ctx.Request.Method == "OPTIONS" && p.Cors)
            {
                status = 204;
                ctx.Response.StatusCode = status;
                return;
            }

            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(buffer, cancellation)) > 0)
            {
                if (ms.Length + n > 1048576)
                {
                    status = 413;
                    throw new BadHttpRequestException("Тело запроса превышает 1 МиБ", 413);
                }

                ms.Write(buffer, 0, n);
            }

            body = Encoding.UTF8.GetString(ms.ToArray());
            s = router!.Select(new(ctx.Request.Method, ctx.Request.Path.Value ?? "/", ctx.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()), ctx.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase), body));
            status = s.Status;
            var r = s.Response;
            if (r != null && r.DelayMs > 0)
                await Task.Delay(r.DelayMs, cancellation);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = r?.ContentType ?? "application/json";
            if (s.Allow != null)
                ctx.Response.Headers.Allow = s.Allow;
            if (r != null)
                foreach (var h in r.Headers)
                    ctx.Response.Headers[h.Key] = h.Value;
            output = r?.Body ?? JsonSerializer.Serialize(new { error = s.Reason });
            if (status is not 204 and not 304)
                await ctx.Response.WriteAsync(output, cancellation);
        }
        catch (BadHttpRequestException ex)
        {
            status = ex.StatusCode;
            error = ex.Message;
            ctx.Response.StatusCode = status;
        }
        catch (OperationCanceledException)
        {
            error = "Запрос отменён";
        }
        catch (Exception ex)
        {
            error = ex.Message;
            status = 500;
            if (!ctx.Response.HasStarted)
                ctx.Response.StatusCode = 500;
        }
        finally
        {
            var headers = ctx.Request.Headers.ToDictionary(x => x.Key, x => new[] { "Authorization", "Cookie", "Set-Cookie", "X-Api-Key" }.Contains(x.Key, StringComparer.OrdinalIgnoreCase) ? "[скрыто]" : x.Value.ToString());
            var e = new LogEntry(Guid.NewGuid(), at, ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, headers, Preview(body), body.Length > 32768, status, Preview(output), watch.ElapsedMilliseconds, s?.Route?.Id, s?.Response?.Id, s?.Reason ?? (status == 204 ? "CORS preflight" : "Ошибка запроса"), error, s?.Step);
            logs.Enqueue(e);
            while (logs.Count > 500)
                logs.TryDequeue(out _);
            try
            {
                Logged?.Invoke(e);
            }
            catch
            {
            }
        }
    }

    static string Preview(string s) => s.Length > 32768 ? s[..32768] + "… [усечено]" : s;
    public async Task StopAsync()
    {
        var host = app;
        app = null;
        shutdown?.Cancel();
        if (host != null)
        {
            using var c = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await host.StopAsync(c.Token);
            await host.DisposeAsync();
        }

        router?.Reset();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
