using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ANDS.RulesEngine;

public interface IRuleSource
{
    Task<IReadOnlyList<Rule>> LoadRulesAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonFileRuleSource : IRuleSource
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _serializerOptions;

    public JsonFileRuleSource(string filePath, JsonSerializerOptions? serializerOptions = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _serializerOptions = serializerOptions ?? RuleJsonSerializer.DefaultOptions;
    }

    public async Task<IReadOnlyList<Rule>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(_filePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var rulesElement = root.ValueKind == JsonValueKind.Array
                ? root
                : root.ValueKind == JsonValueKind.Object &&
                  root.EnumerateObject().FirstOrDefault(property =>
                      property.Name.Equals("rules", StringComparison.OrdinalIgnoreCase)).Value
                    is { ValueKind: JsonValueKind.Array } wrapperRules
                    ? wrapperRules
                    : throw new JsonException("Expected a JSON array or an object containing a 'rules' array.");
            var rules = JsonSerializer.Deserialize<List<Rule>>(rulesElement.GetRawText(), _serializerOptions)
                        ?? throw new JsonException("The rules array was null.");
            foreach (var rule in rules)
                rule.Validate();
            return rules;
        }
        catch (RuleValidationException exception)
        {
            throw new InvalidDataException($"Invalid rule in JSON file '{_filePath}': {exception.Message}", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Malformed or invalid rules JSON in file '{_filePath}': {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Unable to read rules file '{_filePath}': {exception.Message}", exception);
        }
    }
}

public sealed class SqlRuleSourceOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string Schema { get; init; } = "dbo";
    public string Table { get; init; } = "Rules";
    public string IdColumn { get; init; } = "Id";
    public string NameColumn { get; init; } = "Name";
    public string DescriptionColumn { get; init; } = "Description";
    public string PriorityColumn { get; init; } = "Priority";
    public string EnabledColumn { get; init; } = "Enabled";
    public string DefinitionColumn { get; init; } = "Definition";

    public void Validate()
    {
        _ = GetValidatedIdentifiers();
    }

    internal SqlRuleIdentifiers GetValidatedIdentifiers()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("A SQL connection string is required.", nameof(ConnectionString));

        var schema = SqlRuleQueryBuilder.QuoteIdentifier(Schema, nameof(Schema));
        var table = SqlRuleQueryBuilder.QuoteIdentifier(Table, nameof(Table));
        return new SqlRuleIdentifiers(
            schema,
            table,
            SqlRuleQueryBuilder.QuoteIdentifier(IdColumn, nameof(IdColumn)),
            SqlRuleQueryBuilder.QuoteIdentifier(NameColumn, nameof(NameColumn)),
            SqlRuleQueryBuilder.QuoteIdentifier(DescriptionColumn, nameof(DescriptionColumn)),
            SqlRuleQueryBuilder.QuoteIdentifier(PriorityColumn, nameof(PriorityColumn)),
            SqlRuleQueryBuilder.QuoteIdentifier(EnabledColumn, nameof(EnabledColumn)),
            SqlRuleQueryBuilder.QuoteIdentifier(DefinitionColumn, nameof(DefinitionColumn)));
    }
}

internal sealed class SqlRuleIdentifiers(
    string schema,
    string table,
    string idColumn,
    string nameColumn,
    string descriptionColumn,
    string priorityColumn,
    string enabledColumn,
    string definitionColumn)
{
    public string Schema { get; } = schema;
    public string Table { get; } = table;
    public string IdColumn { get; } = idColumn;
    public string NameColumn { get; } = nameColumn;
    public string DescriptionColumn { get; } = descriptionColumn;
    public string PriorityColumn { get; } = priorityColumn;
    public string EnabledColumn { get; } = enabledColumn;
    public string DefinitionColumn { get; } = definitionColumn;
    public IReadOnlyList<string> SelectColumns { get; } =
    [
        idColumn,
        nameColumn,
        descriptionColumn,
        priorityColumn,
        enabledColumn,
        definitionColumn
    ];
}

public interface IDbConnectionFactory
{
    DbConnection CreateConnection(string connectionString);
}

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);
}

public static class SqlRuleQueryBuilder
{
    public static string BuildSelect(SqlRuleSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var identifiers = options.GetValidatedIdentifiers();
        return $"SELECT {string.Join(", ", identifiers.SelectColumns)} FROM {identifiers.Schema}.{identifiers.Table} ORDER BY {identifiers.PriorityColumn}, {identifiers.IdColumn};";
    }

    public static string QuoteIdentifier(string identifier, string optionName)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            identifier.Any(character => !(char.IsLetterOrDigit(character) || character == '_')) ||
            char.IsDigit(identifier[0]))
            throw new ArgumentException($"SQL identifier '{identifier}' in {optionName} is invalid.", optionName);
        return $"[{identifier}]";
    }
}

public sealed class SqlRuleSource : IRuleSource
{
    private readonly SqlRuleSourceOptions _options;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly JsonSerializerOptions _serializerOptions;

    public SqlRuleSource(SqlRuleSourceOptions options, IDbConnectionFactory? connectionFactory = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = connectionFactory ?? new SqlConnectionFactory();
        _serializerOptions = serializerOptions ?? RuleJsonSerializer.DefaultOptions;
        _options.Validate();
    }

    public async Task<IReadOnlyList<Rule>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SqlRuleQueryBuilder.BuildSelect(_options);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rules = new List<Rule>();
        while (await reader.ReadAsync(cancellationToken))
            rules.Add(SqlRuleMapper.Map(reader, _options, _serializerOptions));
        return rules;
    }
}

public static class SqlRuleMapper
{
    public static Rule Map(DbDataReader reader, SqlRuleSourceOptions options,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var jsonOptions = serializerOptions ?? RuleJsonSerializer.DefaultOptions;
        var id = ReadRequiredString(reader, options.IdColumn,
            $"The id column '{options.IdColumn}' is null or empty.");
        var name = ReadRequiredString(reader, options.NameColumn,
            $"The name column '{options.NameColumn}' is null or empty.");
        var definition = ReadRequiredString(reader, options.DefinitionColumn,
            $"Definition column '{options.DefinitionColumn}' is null or empty.", id);

        RuleDefinition ruleDefinition;
        try
        {
            ruleDefinition = JsonSerializer.Deserialize<RuleDefinition>(
                                 definition, jsonOptions)
                             ?? throw new JsonException("Definition JSON was null.");
        }
        catch (JsonException exception)
        {
            throw new RuleValidationException(id,
                $"Definition column '{options.DefinitionColumn}' is invalid JSON: {exception.Message}");
        }

        var rule = new Rule
        {
            Id = id,
            Name = name,
            Description = ReadOptionalString(reader, options.DescriptionColumn),
            Priority = Convert.ToInt32(reader[options.PriorityColumn], CultureInfo.InvariantCulture),
            Enabled = Convert.ToBoolean(reader[options.EnabledColumn], CultureInfo.InvariantCulture),
            Condition = ruleDefinition.Condition,
            Actions = ruleDefinition.Actions ?? []
        };
        rule.Validate();
        return rule;
    }

    private static string ReadRequiredString(
        DbDataReader reader,
        string column,
        string message,
        string? ruleId = null)
    {
        var value = ReadOptionalString(reader, column);
        if (string.IsNullOrWhiteSpace(value))
            throw new RuleValidationException(ruleId, message);
        return value;
    }

    private static string? ReadOptionalString(DbDataReader reader, string column)
    {
        var value = reader[column];
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}

public sealed class RuleDefinition
{
    public Condition? Condition { get; init; }
    public IReadOnlyList<RuleAction>? Actions { get; init; }
}

public sealed class CachedRuleSource : IRuleSource, IDisposable
{
    private readonly IRuleSource _inner;
    private readonly TimeSpan? _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<Rule>? _cached;
    private DateTimeOffset _loadedAt;

    public CachedRuleSource(IRuleSource inner, TimeSpan? ttl = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (ttl is { } duration && duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl));
        _ttl = ttl;
    }

    public async Task<IReadOnlyList<Rule>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null && (_ttl is null || DateTimeOffset.UtcNow - _loadedAt < _ttl))
            return _cached;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is null || (_ttl is not null && DateTimeOffset.UtcNow - _loadedAt >= _ttl))
            {
                _cached = await _inner.LoadRulesAsync(cancellationToken);
                _loadedAt = DateTimeOffset.UtcNow;
            }
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _cached = null;
        _loadedAt = default;
    }

    public void Dispose() => _gate.Dispose();
}
