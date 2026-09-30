using Microsoft.Extensions.Options;

namespace OneSTools.Ras.Exporter.Configuration;

public sealed class RasExporterSettingsValidator : IValidateOptions<RasExporterSettings>
{
    public ValidateOptionsResult Validate(string? name, RasExporterSettings options)
    {
        var errors = new List<string>();
        var servers = options.Servers ?? [];
        if (servers.Count == 0)
            errors.Add("RasExporter:Servers пуст: добавьте сервер в appsettings.json рядом с бинарником " +
                "или задайте RasExporter__Servers__0__Name и RasExporter__Servers__0__Host");

        for (int i = 0; i < servers.Count; i++)
        {
            var server = servers[i];
            string prefix = $"RasExporter:Servers:{i}";
            if (server is null)
            {
                errors.Add($"{prefix}: настройки отсутствуют");
                continue;
            }

            if (string.IsNullOrWhiteSpace(server.Name) || string.IsNullOrWhiteSpace(server.Host))
                errors.Add($"{prefix}: Name и Host обязательны (оба непустые)");
            if (server.Port is < 1 or > 65535)
                errors.Add($"{prefix}: Port должен быть в пределах 1..65535");
            if (server.ConnectTimeoutMilliseconds is < 100 or > 600_000)
                errors.Add($"{prefix}: ConnectTimeoutMilliseconds должен быть в пределах 100..600000");
            if (server.PollIntervals is null)
                errors.Add($"{prefix}: PollIntervals обязателен");
            else
            {
                int[] intervals = [server.PollIntervals.ClustersSeconds, server.PollIntervals.SessionsSeconds,
                    server.PollIntervals.ProcessesSeconds, server.PollIntervals.ConnectionsSeconds,
                    server.PollIntervals.LocksSeconds, server.PollIntervals.InfoBasesSeconds];
                if (intervals.Any(value => value is < 1 or > 86400))
                    errors.Add($"{prefix}: интервалы опроса должны быть в пределах 1..86400 секунд");
            }

            if (server.AgentAuth is null || (!string.IsNullOrEmpty(server.AgentAuth.Password) && string.IsNullOrEmpty(server.AgentAuth.User)))
                errors.Add($"{prefix}: AgentAuth требует User при заданном Password");

            if (server.Clusters is null)
            {
                errors.Add($"{prefix}: Clusters не может быть null (пустой список включает автообнаружение)");
                continue;
            }

            for (int j = 0; j < server.Clusters.Count; j++)
            {
                var cluster = server.Clusters[j];
                if (cluster is null || (cluster.Enabled && cluster.ClusterId.GetValueOrDefault() == Guid.Empty && string.IsNullOrWhiteSpace(cluster.ClusterName)))
                    errors.Add($"{prefix}:Clusters:{j}: требуется ClusterId или ClusterName; " +
                        "пустое значение из env (…Clusters__" + j + "__ClusterName=) создаёт элемент списка — " +
                        "не задавайте переменную, если нужно автообнаружение");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
