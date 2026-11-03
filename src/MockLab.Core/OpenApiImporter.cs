using System.Text.Json;

namespace MockLab.Core;
public record ImportResult(Project Project, List<string> Warnings);
public static class OpenApiImporter
{
    public static ImportResult Import(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("openapi", out var version) || !version.GetString()!.StartsWith("3.0."))
            throw new Exception("Поддерживается OpenAPI 3.0.x в JSON");
        var p = new Project
        {
            Name = "Импорт OpenAPI"
        };
        var warnings = new List<string>();
        if (json.Contains("\"$ref\""))
            warnings.Add("$ref не разрешаются (включая внутренние); внешние URL не загружаются.");
        if (root.TryGetProperty("info", out var info) && info.TryGetProperty("title", out var title))
            p.Name = title.GetString() ?? p.Name;
        foreach (var path in root.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
            {
                var method = op.Name.ToUpperInvariant();
                if (!new[]
                {
                    "GET",
                    "POST",
                    "PUT",
                    "PATCH",
                    "DELETE"
                }.Contains(method))
                {
                    warnings.Add($"Пропущен {op.Name} {path.Name}");
                    continue;
                }

                var r = new Route
                {
                    Path = path.Name,
                    Method = method
                };
                if (op.Value.TryGetProperty("responses", out var responses))
                    foreach (var a in responses.EnumerateObject())
                    {
                        if (!int.TryParse(a.Name, out var status) || status < 200 || status > 599)
                        {
                            warnings.Add($"Пропущен статус {a.Name}");
                            continue;
                        }

                        var resp = new Response
                        {
                            StatusCode = status,
                            Name = op.Value.TryGetProperty("summary", out var summary) ? summary.GetString() ?? a.Name : a.Name
                        };
                        bool example = false;
                        if (a.Value.TryGetProperty("content", out var content))
                            foreach (var media in content.EnumerateObject())
                            {
                                if (media.Name is not "application/json" and not "text/plain")
                                {
                                    warnings.Add("Пропущен media type " + media.Name);
                                    continue;
                                }

                                resp.ContentType = media.Name;
                                resp.Body = media.Name == "text/plain" ? "" : "{}";
                                JsonElement ex = default;
                                if (media.Value.TryGetProperty("example", out ex))
                                    example = true;
                                else if (media.Value.TryGetProperty("examples", out var examples))
                                    foreach (var item in examples.EnumerateObject())
                                        if (item.Value.TryGetProperty("value", out ex))
                                        {
                                            example = true;
                                            break;
                                        }

                                if (example)
                                    resp.Body = media.Name == "application/json" ? ex.GetRawText() : ex.ValueKind == JsonValueKind.String ? ex.GetString() ?? "" : ex.GetRawText();
                                break;
                            }

                        if (!example)
                            warnings.Add($"{method} {path.Name} {status}: placeholder, схема не генерируется");
                        r.Responses.Add(resp);
                    }

                if (r.Responses.Count == 0)
                    r.Responses.Add(new() { Name = "Placeholder" });
                r.FallbackResponseId = r.Responses[0].Id;
                p.Routes.Add(r);
            }

        Validator.Validate(p);
        return new(p, warnings);
    }
}
