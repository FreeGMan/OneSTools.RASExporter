using Microsoft.Extensions.Options;

namespace OneSTools.Ras.Exporter.Configuration;

public sealed class ClickHouseSettingsValidator : IValidateOptions<ClickHouseSettings>
{
    public ValidateOptionsResult Validate(string? name, ClickHouseSettings options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            errors.Add("ClickHouse:ConnectionString обязателен (appsettings.json рядом с бинарником или ClickHouse__ConnectionString)");
        if (string.IsNullOrWhiteSpace(options.DatabaseTemplate))
            errors.Add("ClickHouse:DatabaseTemplate обязателен");
        if (options.BatchSize is < 1 or > 100_000)
            errors.Add("ClickHouse:BatchSize должен быть в пределах 1..100000");
        if (options.FlushIntervalSeconds is < 1 or > 3600)
            errors.Add("ClickHouse:FlushIntervalSeconds должен быть в пределах 1..3600");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
