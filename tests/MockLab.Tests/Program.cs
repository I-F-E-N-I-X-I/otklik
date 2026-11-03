using MockLab.Core;
using MockLab.Server;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var passed = 0;
void Assert(bool ok, string name)
{
    if (!ok)
        throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}

void Reject(Action a, string name)
{
    try
    {
        a();
    }
    catch
    {
        Assert(true, name);
        return;
    }

    throw new Exception("FAIL " + name);
}

RequestData Req(string method = "GET", string path = "/api/products", string body = "", Dictionary<string, string>? query = null, Dictionary<string, string>? headers = null) => new(method, path, query ?? [], headers ?? [], body);
var p = ProjectFiles.Demo();
Assert(ProjectFiles.Serialize(ProjectFiles.Parse(ProjectFiles.Serialize(p))) == ProjectFiles.Serialize(p), "T01 roundtrip");
Reject(() => ProjectFiles.Parse("{"), "T01 corrupt");
Reject(() => ProjectFiles.Parse(ProjectFiles.Serialize(p).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")), "T01 version");
var router = new Router(p);
Assert(router.Select(Req()).Status == 200, "T02 GET");
Assert(router.Select(Req("POST")).Status == 405, "T02 405");
Assert(router.Select(Req(path: "/none")).Status == 404, "T02 404");
Assert(router.Select(Req(path: "/api/products/")).Status == 200, "T02 trailing slash");
Assert(router.Select(Req(query: new() { { "category", "books" } })).Response!.Name == "Книги", "T03 query");
Assert(router.Select(Req(query: new() { { "category", "other" } })).Reason.StartsWith("Fallback"), "T03 fallback");
var seq = p.Routes[1];
Assert(new[] { 500, 503, 200, 200 }.SequenceEqual(Enumerable.Range(0, 4).Select(_ => router.Select(Req(path: "/api/retry")).Status)), "T04 holdLast");
router.Reset();
Assert(router.Select(Req(path: "/api/retry")).Status == 500, "T04 reset");
seq.EndBehavior = "loop";
router.Reset();
Assert(new[] { 500, 503, 200, 500 }.SequenceEqual(Enumerable.Range(0, 4).Select(_ => router.Select(Req(path: "/api/retry")).Status)), "T04 loop");
var special = ProjectFiles.Demo();
var param = special.Routes[0];
param.Path = "/users/{id}";
var literal = JsonSerializer.Deserialize<Route>(JsonSerializer.Serialize(param))!;
literal.Id = Guid.NewGuid().ToString();
literal.Path = "/users/current";
special.Routes.Add(literal);
Assert(new Router(special).Select(Req(path: "/users/current")).Route!.Id == literal.Id, "T02 literal priority");
var duplicate = ProjectFiles.Clone(special);
duplicate.Routes.Add(new() { Path = "/users/{name}" });
Reject(() => Validator.Validate(duplicate), "T02 duplicate");
foreach (var source in new[]
{
    "query",
    "header",
    "body",
    "path"
}

)
    foreach (var op in new[]
    {
        "equals",
        "contains",
        "exists"
    }

    )
    {
        var a = param.Responses[0];
        a.Conditions = [new()
        {
            Source = source,
            Key = source == "body" ? "user.role" : source == "path" ? "id" : "role",
            Operator = op,
            Value = op == "contains" ? "dmi" : "admin"
        }

        ];
        Assert(new Router(special).Select(Req(path: "/users/admin", body: "{\"user\":{\"role\":\"admin\"}}", query: new() { { "role", "admin" } }, headers: new() { { "ROLE", "admin" } })).Response!.Id == a.Id, $"T03 {source} {op}");
    }

param.Responses[0].Conditions = [new()
{
    Source = "body",
    Key = "x",
    Operator = "exists"
}

];
Assert(new Router(special).Select(Req(path: "/users/1", body: "{\"x\":null}")).Response!.Id == param.Responses[0].Id, "T03 null exists");
Assert(new Router(special).Select(Req(path: "/users/1", body: "bad")).Reason.Contains("неверный JSON"), "T03 invalid body");
foreach (var edit in new Action<Project>[]
{
    x => x.Port = 1,
    x => x.Routes[0].Responses[0].StatusCode = 100,
    x => x.Routes[0].Responses[0].DelayMs = -1,
    x => x.Routes[0].Responses[0].Body = "{",
    x => x.Routes[0].Responses[0].Headers["X-Test"] = "bad\r\n",
    x => x.Routes[1].Sequence.Clear(),
    x => x.Routes[1].Sequence.Add("missing")
}

)
{
    var bad = ProjectFiles.Demo();
    edit(bad);
    Reject(() => Validator.Validate(bad), "T07 invalid config");
}

p = ProjectFiles.Demo();
p.Port = 18371;
await using var server = new MockServer();
await server.StartAsync(p);
using var client = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:18371")
};
Assert((await client.GetAsync("/api/products?category=books")).StatusCode == HttpStatusCode.OK, "T08 HTTP real");
var pre = await client.SendAsync(new(HttpMethod.Options, "/api/retry"));
Assert(pre.StatusCode == HttpStatusCode.NoContent && pre.Headers.Contains("Access-Control-Allow-Origin"), "T08 preflight");
Assert((int)(await client.GetAsync("/api/retry")).StatusCode == 500, "T08 preflight no step");
var sw = System.Diagnostics.Stopwatch.StartNew();
var slow = client.GetAsync("/api/retry");
await Task.Delay(100);
Assert((await client.GetAsync("/api/products")).IsSuccessStatusCode && sw.ElapsedMilliseconds < 800, "T05 async delay");
await slow;
Assert(sw.ElapsedMilliseconds >= 900, "T05 duration");
await using (var occupied = new MockServer())
{
    try
    {
        await occupied.StartAsync(p);
        throw new Exception("occupied accepted");
    }
    catch (Exception ex)
    {
        Assert(ex.Message.Contains("порт"), "T06 occupied");
    }
}

var secret = new HttpRequestMessage(HttpMethod.Post, "/api/products")
{
    Content = new StringContent(new string ('a', 40000))
};
secret.Headers.Add("Authorization", "secret");
await client.SendAsync(secret);
await Task.Delay(50);
Assert(server.Logs.Last().Headers["Authorization"] == "[скрыто]" && server.Logs.Last().Truncated, "T09 mask truncate");
Assert((int)(await client.PostAsync("/api/products", new StringContent(new string ('a', 1048577)))).StatusCode == 413, "T09 413");
for (int i = 0; i < 505; i++)
    await client.GetAsync("/api/products");
Assert(server.Logs.Count == 500, "T09 bounded");
await server.StopAsync();
await server.StartAsync(p);
Assert((int)(await client.GetAsync("/api/retry")).StatusCode == 500, "T06 restart reset");
await server.StopAsync();
var concurrent = ProjectFiles.Demo();
var cr = concurrent.Routes[1];
cr.Responses = Enumerable.Range(0, 20).Select(i => new Response { Name = i.ToString(), Body = "{}" }).ToList();
cr.Sequence = cr.Responses.Select(x => x.Id).ToList();
cr.FallbackResponseId = cr.Sequence[0];
var rr = new Router(concurrent);
var selected = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => rr.Select(Req(path: "/api/retry")).Response!.Id)));
Assert(selected.Distinct().Count() == 20, "T04 parallel atomic");
var imported = OpenApiImporter.Import("{\"openapi\":\"3.0.3\",\"paths\":{\"/ping\":{\"get\":{\"responses\":{\"200\":{\"content\":{\"application/json\":{\"example\":{\"ok\":true}}}}}}}}}");
Assert(imported.Project.Routes.Count == 1 && imported.Project.Routes[0].Responses[0].Body.Contains("true"), "T10 OpenAPI");
Reject(() => OpenApiImporter.Import("{\"openapi\":\"3.1.0\"}"), "T10 version");
var delayedProject = ProjectFiles.Demo();
delayedProject.Port = 18372;
delayedProject.Routes[0].Responses.ForEach(x => x.DelayMs = 60000);
await using (var stopping = new MockServer())
{
    await stopping.StartAsync(delayedProject);
    using var stopClient = new HttpClient();
    var pending = stopClient.GetAsync("http://127.0.0.1:18372/api/products");
    await Task.Delay(150);
    var stopWatch = System.Diagnostics.Stopwatch.StartNew();
    await stopping.StopAsync();
    Assert(stopWatch.ElapsedMilliseconds < 2500, "T06 stop cancels delay");
    try
    {
        await pending;
    }
    catch (HttpRequestException)
    {
    }
}

Console.WriteLine($"SUCCESS: {passed} checks");
if (args.Contains("--write-samples"))
{
    Directory.CreateDirectory("samples");
    ProjectFiles.Save(ProjectFiles.Demo(), "samples/demo-project.json");
}
