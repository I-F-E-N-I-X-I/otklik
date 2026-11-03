using Condition = MockLab.Core.Condition;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MockLab.Core;
using MockLab.Server;

namespace MockLab.Desktop;
public partial class MainWindow : Window
{
    Project project = ProjectFiles.Demo();
    readonly MockServer server = new();
    Route? route;
    Response? response;
    bool loading = true, dirty = false, closing = false;
    string? file;
    int? activePort;
    readonly ObservableCollection<LogEntry> entries = [];
    public MainWindow()
    {
        InitializeComponent();
        Logs.ItemsSource = entries;
        server.Logged += e => Dispatcher.BeginInvoke(() =>
        {
            entries.Add(e);
            while (entries.Count > 500)
                entries.RemoveAt(0);
        });
        LoadProject();
        Closing += OnClosing;
    }

    static int ParseNumber(string text, string field, int min, int max)
    {
        if (!int.TryParse(text, out var value) || value < min || value > max)
            throw new Exception($"{field}: введите целое число от {min} до {max}");
        return value;
    }

    void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Отклик", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Changed(object? sender, EventArgs e)
    {
        if (!loading)
        {
            dirty = true;
            State.Text = server.Running ? "Есть изменения, перезапустите сервер" : "Несохранённые изменения";
        }
    }

    void LoadProject()
    {
        loading = true;
        route = null;
        response = null;
        ProjectName.Text = project.Name;
        Port.Text = project.Port.ToString();
        Cors.IsChecked = project.Cors;
        Routes.ItemsSource = null;
        Routes.ItemsSource = project.Routes;
        Routes.SelectedIndex = project.Routes.Count > 0 ? 0 : -1;
        loading = false;
        dirty = false;
        State.Text = server.Running ? "Работает предыдущий снимок — перезапустите сервер" : "Остановлен · 127.0.0.1";
    }

    void SelectRoute(object sender, SelectionChangedEventArgs e)
    {
        if (Routes.SelectedItem is not Route r)
        {
            Editor.IsEnabled = false;
            return;
        }

        Editor.IsEnabled = true;
        if (!loading && route != null)
        {
            try
            {
                Commit(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Сначала исправьте текущий маршрут: " + ex.Message);
                loading = true;
                Routes.SelectedItem = route;
                loading = false;
                return;
            }
        }

        route = r;
        response = null;
        loading = true;
        Method.SelectedIndex = Array.IndexOf(new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, r.Method);
        PathBox.Text = r.Path;
        RouteEnabled.IsChecked = r.Enabled;
        Mode.SelectedIndex = r.Mode == "rules" ? 0 : 1;
        EndBehavior.SelectedIndex = r.EndBehavior == "holdLast" ? 0 : 1;
        Sequence.Text = string.Join(',', r.Sequence.Select(id => r.Responses.FindIndex(x => x.Id == id) + 1));
        RefreshResponses();
        loading = false;
    }

    void RefreshResponses()
    {
        var wasLoading = loading;
        loading = true;
        Responses.ItemsSource = null;
        Responses.ItemsSource = route?.Responses;
        Responses.SelectedIndex = route?.Responses.Count > 0 ? 0 : -1;
        ResponseLegend.Text = route == null ? "" : string.Join("\n", route.Responses.Select((r, i) => $"{i + 1}. {r}"));
        loading = wasLoading;
    }

    void SelectResponse(object sender, SelectionChangedEventArgs e)
    {
        if (Responses.SelectedItem is not Response r)
            return;
        if (!loading && response != null && route != null)
        {
            try
            {
                Commit(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Сначала исправьте текущий ответ: " + ex.Message);
                loading = true;
                Responses.SelectedItem = response;
                loading = false;
                return;
            }
        }

        response = r;
        var previousLoading = loading;
        loading = true;
        ResponseName.Text = r.Name;
        StatusCode.Text = r.StatusCode.ToString();
        Delay.Text = r.DelayMs.ToString();
        ContentType.Text = r.ContentType;
        Headers.Text = JsonSerializer.Serialize(r.Headers, ProjectFiles.Json);
        Body.Text = r.Body;
        ResponseEnabled.IsChecked = r.Enabled;
        Fallback.IsChecked = r.Id == route?.FallbackResponseId;
        Conditions.ItemsSource = new ObservableCollection<Condition>(r.Conditions.Select(c => new Condition { Source = c.Source, Key = c.Key, Operator = c.Operator, Value = c.Value }));
        loading = previousLoading;
    }

    void Commit(bool validate = true)
    {
        project.Name = ProjectName.Text;
        project.Port = ParseNumber(Port.Text, "Порт", 1024, 65535);
        project.Cors = Cors.IsChecked == true;
        if (route != null)
        {
            route.Method = ((ComboBoxItem)Method.SelectedItem).Content.ToString()!;
            route.Path = PathBox.Text;
            route.Enabled = RouteEnabled.IsChecked == true;
            route.Mode = Mode.SelectedIndex == 0 ? "rules" : "sequence";
            route.EndBehavior = EndBehavior.SelectedIndex == 0 ? "holdLast" : "loop";
            if (response != null)
            {
                Conditions.CommitEdit(DataGridEditingUnit.Cell, true);
                Conditions.CommitEdit(DataGridEditingUnit.Row, true);
                response.Name = ResponseName.Text;
                response.StatusCode = ParseNumber(StatusCode.Text, "HTTP-статус", 200, 599);
                response.DelayMs = ParseNumber(Delay.Text, "Задержка", 0, 60000);
                response.ContentType = ContentType.Text;
                response.Body = Body.Text;
                response.Enabled = ResponseEnabled.IsChecked == true;
                response.Headers = JsonSerializer.Deserialize<Dictionary<string, string>>(Headers.Text) ?? [];
                response.Conditions = ((ObservableCollection<Condition>)Conditions.ItemsSource).ToList();
                if (Fallback.IsChecked == true)
                    route.FallbackResponseId = response.Id;
                else if (route.FallbackResponseId == response.Id)
                    route.FallbackResponseId = "";
            }

            route.Sequence = Sequence.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => route.Responses[ParseNumber(s, "Номер ответа в сценарии", 1, route.Responses.Count) - 1].Id).ToList();
        }

        if (validate)
            Validator.Validate(project);
        Routes.Items.Refresh();
        Responses.Items.Refresh();
    }

    void Apply(object s, RoutedEventArgs e) => Guard(() =>
    {
        Commit();
        dirty = true;
        State.Text = server.Running ? "Есть изменения, перезапустите сервер" : "Маршрут применён · не сохранён";
    });
    bool Confirm()
    {
        if (!dirty)
            return true;
        var result = MessageBox.Show(this, "Сохранить изменения перед заменой/закрытием?", "Отклик", MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Cancel)
            return false;
        if (result == MessageBoxResult.No)
            return true;
        try
        {
            return SaveFile(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return false;
        }
    }

    bool SaveFile(bool export)
    {
        Commit();
        var path = file;
        if (export || path == null)
        {
            var d = new SaveFileDialog
            {
                Filter = "MockLab JSON|*.json",
                FileName = project.Name + ".json"
            };
            if (d.ShowDialog() != true)
                return false;
            path = d.FileName;
        }

        ProjectFiles.Save(project, path);
        if (!export)
        {
            file = path;
            dirty = false;
            State.Text = "Сохранено: " + Path.GetFileName(path);
        }

        return true;
    }

    void Save(object s, RoutedEventArgs e) => Guard(() => SaveFile(false));
    void Export(object s, RoutedEventArgs e) => Guard(() => SaveFile(true));
    void New(object s, RoutedEventArgs e)
    {
        if (Confirm())
        {
            project = new();
            file = null;
            LoadProject();
        }
    }

    void Open(object s, RoutedEventArgs e) => Guard(() =>
    {
        var d = new OpenFileDialog
        {
            Filter = "MockLab JSON|*.json"
        };
        if (d.ShowDialog() != true)
            return;
        var next = ProjectFiles.Parse(File.ReadAllText(d.FileName));
        if (!Confirm())
            return;
        project = next;
        file = d.FileName;
        LoadProject();
    });
    void Import(object s, RoutedEventArgs e) => Guard(() =>
    {
        var d = new OpenFileDialog
        {
            Filter = "OpenAPI JSON|*.json"
        };
        if (d.ShowDialog() != true)
            return;
        var result = OpenApiImporter.Import(File.ReadAllText(d.FileName));
        if (!Confirm())
            return;
        project = result.Project;
        file = null;
        LoadProject();
        dirty = true;
        MessageBox.Show(result.Warnings.Count == 0 ? "Импорт завершён" : string.Join('\n', result.Warnings), "Импорт OpenAPI");
    });
    void AddRoute(object s, RoutedEventArgs e) => Guard(() =>
    {
        Commit(false);
        var a = new Response();
        var r = new Route
        {
            Path = "/api/new" + project.Routes.Count,
            Responses = [a],
            FallbackResponseId = a.Id
        };
        while (project.Routes.Any(x => x.Method == r.Method && x.Path == r.Path))
            r.Path += "-new";
        project.Routes.Add(r);
        Routes.Items.Refresh();
        Routes.SelectedItem = r;
        dirty = true;
    });
    void DuplicateRoute(object s, RoutedEventArgs e) => Guard(() =>
    {
        Commit();
        if (route == null)
            return;
        var r = JsonSerializer.Deserialize<Route>(JsonSerializer.Serialize(route, ProjectFiles.Json), ProjectFiles.Json)!;
        r.Id = Guid.NewGuid().ToString();
        var basePath = r.Path + "-copy";
        r.Path = basePath;
        for (var suffix = 2; project.Routes.Any(x => x.Method == r.Method && x.Path == r.Path); suffix++)
            r.Path = basePath + suffix;
        project.Routes.Add(r);
        Routes.Items.Refresh();
        Routes.SelectedItem = r;
        dirty = true;
    });
    void DeleteRoute(object s, RoutedEventArgs e)
    {
        if (route == null)
            return;
        project.Routes.Remove(route);
        route = null;
        response = null;
        Routes.Items.Refresh();
        Routes.SelectedIndex = 0;
        dirty = true;
    }

    void AddResponse(object s, RoutedEventArgs e) => Guard(() =>
    {
        if (route == null)
            return;
        Commit(false);
        var a = new Response
        {
            Name = "Ответ " + (route.Responses.Count + 1)
        };
        route.Responses.Add(a);
        RefreshResponses();
        Responses.SelectedItem = a;
        dirty = true;
    });
    void DeleteResponse(object s, RoutedEventArgs e)
    {
        if (route == null || response == null)
            return;
        if (route.Responses.Count == 1)
        {
            MessageBox.Show("Нужен хотя бы один ответ");
            return;
        }

        route.Responses.Remove(response);
        route.Sequence.RemoveAll(x => x == response.Id);
        if (route.FallbackResponseId == response.Id)
            route.FallbackResponseId = route.Responses[0].Id;
        Sequence.Text = string.Join(',', route.Sequence.Select(id => route.Responses.FindIndex(x => x.Id == id) + 1));
        RefreshResponses();
        dirty = true;
    }

    void Move(int delta)
    {
        if (route == null || response == null)
            return;
        try
        {
            Commit(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

        var index = route.Responses.IndexOf(response);
        var to = index + delta;
        if (to < 0 || to >= route.Responses.Count)
            return;
        var a = response;
        route.Responses.RemoveAt(index);
        route.Responses.Insert(to, a);
        Sequence.Text = string.Join(',', route.Sequence.Select(id => route.Responses.FindIndex(x => x.Id == id) + 1));
        RefreshResponses();
        Responses.SelectedItem = a;
        dirty = true;
    }

    void MoveUp(object s, RoutedEventArgs e) => Move(-1);
    void MoveDown(object s, RoutedEventArgs e) => Move(1);
    async void StartStop(object s, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            if (server.Running)
            {
                await server.StopAsync();
                activePort = null;
                State.Text = "Остановлен";
                StartButton.Content = "▶ Запустить";
            }
            else
            {
                Commit();
                await server.StartAsync(project);
                activePort = project.Port;
                State.Text = $"Работает http://127.0.0.1:{project.Port}";
                StartButton.Content = "■ Остановить";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    void CopyUrl(object s, RoutedEventArgs e) => Clipboard.SetText($"http://127.0.0.1:{activePort?.ToString() ?? Port.Text}");
    void Reset(object s, RoutedEventArgs e)
    {
        server.Reset();
        State.Text = "Сценарии сброшены";
    }

    async void TestRequest(object s, RoutedEventArgs e)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var result = await client.GetAsync($"http://127.0.0.1:{activePort?.ToString() ?? Port.Text}{PathBox.Text}");
            MessageBox.Show($"{(int)result.StatusCode}\n" + await result.Content.ReadAsStringAsync(), "Тест GET");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
    }

    void FormatJson(object s, RoutedEventArgs e) => Guard(() =>
    {
        using var d = JsonDocument.Parse(Body.Text);
        Body.Text = JsonSerializer.Serialize(d.RootElement, ProjectFiles.Json);
    });
    void ClearLog(object s, RoutedEventArgs e)
    {
        server.Clear();
        entries.Clear();
        LogDetails.Clear();
    }

    void SelectLog(object s, SelectionChangedEventArgs e)
    {
        if (Logs.SelectedItem is LogEntry entry)
            LogDetails.Text = JsonSerializer.Serialize(entry, ProjectFiles.Json);
    }

    void ExportLog(object s, RoutedEventArgs e) => Guard(() =>
    {
        var d = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = "mocklab-log.json"
        };
        if (d.ShowDialog() == true)
            File.WriteAllText(d.FileName, JsonSerializer.Serialize(entries, ProjectFiles.Json));
    });
    async void OnClosing(object? s, CancelEventArgs e)
    {
        if (closing)
            return;
        e.Cancel = true;
        if (!Confirm())
            return;
        closing = true;
        try
        {
            await server.StopAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Остановка Отклик");
        }
        finally
        {
            Close();
        }
    }
}
