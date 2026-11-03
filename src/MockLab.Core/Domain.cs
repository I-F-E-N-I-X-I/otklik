using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace MockLab.Core;
public class Project
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новый проект";
    public int Port { get; set; } = 3001;
    public bool Cors { get; set; } = true;
    public List<Route> Routes { get; set; } = [];
}

public class Route
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Enabled { get; set; } = true;
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/api/products";
    public List<Response> Responses { get; set; } = [];
    public string Mode { get; set; } = "rules";
    public string FallbackResponseId { get; set; } = "";
    public List<string> Sequence { get; set; } = [];
    public string EndBehavior { get; set; } = "holdLast";

    public override string ToString() => $"{(Enabled ? "" : "○ ")}{Method}  {Path}";
}

public class Response
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Ответ";
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public Dictionary<string, string> Headers { get; set; } = [];
    public string Body { get; set; } = "{}";
    public int DelayMs { get; set; }
    public bool Enabled { get; set; } = true;
    public List<Condition> Conditions { get; set; } = [];

    public override string ToString() => $"{Name} · {StatusCode}";
}

public class Condition
{
    public string Source { get; set; } = "query";
    public string Key { get; set; } = "category";
    public string Operator { get; set; } = "equals";
    public string Value { get; set; } = "books";
}

public static class ProjectFiles
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    public static string Serialize(Project p) => JsonSerializer.Serialize(p, Json);
    public static Project Parse(string s)
    {
        var p = JsonSerializer.Deserialize<Project>(s, Json) ?? throw new Exception("Пустой проект");
        Validator.Validate(p);
        return p;
    }

    public static Project Clone(Project p) => Parse(Serialize(p));
    public static void Save(Project p, string path)
    {
        Validator.Validate(p);
        var tmp = path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            File.WriteAllText(tmp, Serialize(p));
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public static Project Demo()
    {
        var r = new Route();
        var ok = new Response
        {
            Name = "Товары",
            Body = "[{\"id\":1,\"name\":\"Книга: HTTP на практике\",\"price\":790},{\"id\":2,\"name\":\"Клавиатура\",\"price\":3490}]"
        };
        var books = new Response
        {
            Name = "Книги",
            Body = "[{\"id\":1,\"name\":\"Книга: HTTP на практике\",\"price\":790}]",
            Conditions = [new()]
        };
        r.Responses = [books, ok];
        r.FallbackResponseId = ok.Id;
        var seq = new Route
        {
            Path = "/api/retry",
            Mode = "sequence"
        };
        seq.Responses = [new()
        {
            Name = "Ошибка",
            StatusCode = 500,
            Body = "{\"error\":\"Временный сбой\"}"
        }, new()
        {
            Name = "Медленно",
            StatusCode = 503,
            DelayMs = 1000,
            Body = "{}"
        }, new()
        {
            Name = "Готово",
            Body = "[]"
        }

        ];
        seq.Sequence = seq.Responses.Select(x => x.Id).ToList();
        seq.FallbackResponseId = seq.Responses[0].Id;
        return new()
        {
            Name = "Интернет-магазин",
            Routes = [r, seq]
        };
    }
}

public static class Validator
{
    public static string Normalize(string path) => path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;
    public static string[] Segments(string p) => Normalize(p).Split('/', StringSplitOptions.RemoveEmptyEntries);
    public static bool Parameter(string s) => s.StartsWith('{') && s.EndsWith('}');
    public static void Validate(Project p)
    {
        void Require(bool b, string message)
        {
            if (!b)
                throw new Exception(message);
        }

        Require(p.SchemaVersion == 1, "Поддерживается только schemaVersion 1");
        Require(!string.IsNullOrWhiteSpace(p.Name), "Введите имя проекта");
        Require(p.Port is >= 1024 and <= 65535, "Порт: 1024–65535");
        Require(p.Routes != null, "routes отсутствует");
        var ids = new HashSet<string>();
        var paths = new HashSet<string>();
        foreach (var r in p.Routes!)
        {
            Require(ids.Add(r.Id), "Повтор ID маршрута");
            Require(new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }.Contains(r.Method), "Неподдерживаемый метод");
            Require(r.Path.StartsWith('/') && !r.Path.Contains('?') && !r.Path.Contains('#') && !r.Path.Contains("//"), "Путь должен начинаться / и не содержать query, # или //");
            var seg = Segments(r.Path);
            var names = new HashSet<string>();
            foreach (var s in seg)
            {
                Require(!(s.Contains('{') || s.Contains('}')) || Parameter(s), "Параметр должен занимать весь сегмент");
                if (Parameter(s))
                    Require(s.Length > 2 && names.Add(s[1..^1]), "Пустой или повторный параметр");
            }

            Require(paths.Add(r.Method + "/" + string.Join('/', seg.Select(s => Parameter(s) ? "{}" : s))), "Неоднозначный дубликат маршрута");
            Require(r.Mode is "rules" or "sequence", "Режим rules/sequence");
            Require(r.EndBehavior is "holdLast" or "loop", "Окончание holdLast/loop");
            var rs = new HashSet<string>();
            foreach (var a in r.Responses)
            {
                Require(rs.Add(a.Id), "Повтор ID ответа");
                Require(a.StatusCode is >= 200 and <= 599, "Статус: 200–599");
                Require(a.DelayMs is >= 0 and <= 60000, "Задержка: 0–60000 мс");
                Require(System.Text.Encoding.UTF8.GetByteCount(a.Body) <= 1048576, "Ответ не более 1 МиБ");
                Require(!a.ContentType.Contains('\r') && !a.ContentType.Contains('\n'), "Некорректный Content-Type");
                if (a.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var d = JsonDocument.Parse(a.Body);
                    }
                    catch
                    {
                        throw new Exception("Неверный JSON ответа: " + a.Name);
                    }
                }

                foreach (var h in a.Headers)
                {
                    Require(h.Key.Length > 0 && h.Key.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c)) && !h.Value.Contains('\r') && !h.Value.Contains('\n'), "Некорректный HTTP-заголовок");
                    Require(!new[] { "Content-Length", "Transfer-Encoding", "Connection", "Content-Type" }.Contains(h.Key, StringComparer.OrdinalIgnoreCase), "Заголовок управляется сервером: " + h.Key);
                }

                foreach (var c in a.Conditions)
                {
                    Require(c.Source is "query" or "header" or "body" or "path", "Источник условия");
                    Require(c.Operator is "equals" or "contains" or "exists", "Оператор условия");
                    Require(!string.IsNullOrWhiteSpace(c.Key), "Ключ условия пуст");
                }
            }

            Require(r.Responses.Any(x => x.Id == r.FallbackResponseId && x.Enabled), "Выберите включённый fallback для " + r.Path);
            if (r.Mode == "sequence")
                Require(r.Sequence.Count > 0 && r.Sequence.All(id => r.Responses.Any(x => x.Id == id && x.Enabled)), "Сценарий пуст или содержит отсутствующий/выключенный ответ");
        }
    }
}

public record RequestData(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, string Body);
public record Selection(int Status, Route? Route, Response? Response, string Reason, int? Step = null, string? Allow = null);
public sealed class Router(Project project)
{
    readonly object gate = new();
    readonly Dictionary<string, long> counters = [];
    public void Reset()
    {
        lock (gate)
            counters.Clear();
    }

    public Selection Select(RequestData req)
    {
        var matches = project.Routes.Where(r => r.Enabled).Select(r => (r, values: Match(r.Path, req.Path))).Where(x => x.values != null).OrderByDescending(x => Validator.Segments(x.r.Path).Count(s => !Validator.Parameter(s))).ToList();
        var chosen = matches.FirstOrDefault(x => x.r.Method == req.Method);
        if (chosen.r == null)
            return matches.Count == 0 ? new(404, null, null, "Маршрут не найден") : new(405, null, null, "Метод не разрешён", Allow: string.Join(", ", matches.Select(x => x.r.Method).Distinct()));
        var r = chosen.r;
        if (r.Mode == "sequence")
        {
            lock (gate)
            {
                long n = counters.GetValueOrDefault(r.Id);
                counters[r.Id] = n + 1;
                int i = r.EndBehavior == "loop" ? (int)(n % r.Sequence.Count) : (int)Math.Min(n, r.Sequence.Count - 1);
                var a = r.Responses.Single(x => x.Id == r.Sequence[i]);
                return new(a.StatusCode, r, a, "Шаг сценария", i + 1);
            }
        }

        bool invalid = false;
        foreach (var a in r.Responses.Where(a => a.Enabled && a.Conditions.Count > 0))
        {
            if (a.Conditions.All(c => Check(c, req, chosen.values!, ref invalid)))
                return new(a.StatusCode, r, a, "Все условия совпали");
        }

        var fallback = r.Responses.Single(a => a.Id == r.FallbackResponseId);
        return new(fallback.StatusCode, r, fallback, invalid ? "Fallback: неверный JSON запроса" : "Fallback: условия не совпали");
    }

    public static Dictionary<string, string>? Match(string pattern, string path)
    {
        var a = Validator.Segments(pattern);
        var b = Validator.Segments(path);
        if (a.Length != b.Length)
            return null;
        var values = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (Validator.Parameter(a[i]))
                values[a[i][1..^1]] = Uri.UnescapeDataString(b[i]);
            else if (a[i] != b[i])
                return null;
        }

        return values;
    }

    static bool Check(Condition c, RequestData r, Dictionary<string, string> path, ref bool invalid)
    {
        string? value = null;
        bool exists = false;
        var dict = c.Source switch
        {
            "query" => r.Query,
            "header" => r.Headers,
            "path" => path,
            _ => null
        };
        if (dict != null)
        {
            if (c.Source == "header")
            {
                var pair = dict.FirstOrDefault(x => x.Key.Equals(c.Key, StringComparison.OrdinalIgnoreCase));
                exists = pair.Key != null;
                value = pair.Value;
            }
            else
                exists = dict.TryGetValue(c.Key, out value);
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(r.Body);
                var e = doc.RootElement;
                exists = true;
                foreach (var key in c.Key.Split('.'))
                {
                    if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e))
                    {
                        exists = false;
                        break;
                    }
                }

                if (exists)
                    value = e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString(),
                        JsonValueKind.Null => "null",
                        JsonValueKind.Number => e.TryGetDecimal(out var n) ? n.ToString("G29", CultureInfo.InvariantCulture) : e.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null
                    };
            }
            catch (JsonException)
            {
                invalid = true;
            }
        }

        return c.Operator == "exists" ? exists : exists && value != null && (c.Operator == "equals" ? value == c.Value : value.Contains(c.Value, StringComparison.Ordinal));
    }
}
