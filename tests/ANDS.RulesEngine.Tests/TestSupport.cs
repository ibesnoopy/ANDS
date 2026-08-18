using System.Collections;
using System.Data;
using System.Data.Common;
using ANDS.RulesEngine;

#pragma warning disable CS8765
namespace ANDS.RulesEngine.Tests;

internal static class TestSupport
{
    public static Rule RuleFor(Condition condition, string id = "rule", int priority = 0,
        IReadOnlyList<RuleAction>? actions = null) =>
        new()
        {
            Id = id,
            Name = id,
            Priority = priority,
            Condition = condition,
            Actions = actions ?? []
        };

    public static ComparisonCondition Comparison(string field, ComparisonOperator op, object? value = null) =>
        new() { Field = field, Operator = op, Value = value };
}

internal sealed class RecordingHandler(string actionType) : IRuleActionHandler
{
    public string ActionType { get; } = actionType;
    public List<RuleAction> Actions { get; } = [];

    public Task HandleAsync(RuleAction action, RuleActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

internal sealed class CountingSource(IReadOnlyList<Rule>? rules = null, TimeSpan? delay = null) : IRuleSource
{
    private readonly IReadOnlyList<Rule> _rules = rules ?? [];
    private readonly TimeSpan _delay = delay ?? TimeSpan.Zero;

    public int LoadCount { get; private set; }

    public async Task<IReadOnlyList<Rule>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        LoadCount++;
        await Task.Delay(_delay, cancellationToken);
        return _rules;
    }
}

internal sealed class TableConnectionFactory(DataTable table) : IDbConnectionFactory
{
    public DataTable Table { get; } = table;
    public FakeDbConnection? LastConnection { get; private set; }

    public DbConnection CreateConnection(string connectionString)
    {
        LastConnection = new FakeDbConnection(Table);
        return LastConnection;
    }
}

internal sealed class FakeDbConnection(DataTable table) : DbConnection
{
    private readonly DataTable _table = table;
    private ConnectionState _state = ConnectionState.Closed;

    public string? LastCommandText { get; private set; }
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "Fake";
    public override string DataSource => "Fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, _table);
    internal void SetCommandText(string text) => LastCommandText = text;
}

internal sealed class FakeDbCommand(FakeDbConnection connection, DataTable table) : DbCommand
{
    private readonly FakeDbConnection _connection = connection;
    private readonly DataTable _table = table;
    private readonly FakeParameterCollection _parameters = new();
    private string _commandText = string.Empty;

    public override string CommandText
    {
        get => _commandText;
        set
        {
            _commandText = value;
            _connection.SetCommandText(value);
        }
    }

    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection
    {
        get => _connection;
        set => throw new NotSupportedException();
    }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => throw new NotSupportedException();
    public override object? ExecuteScalar() => throw new NotSupportedException();
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => _table.CreateDataReader();
}

internal sealed class FakeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();
    public override int Count => _items.Count;
    public override object SyncRoot => ((ICollection)_items).SyncRoot!;
    public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
    public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => _items.Any(parameter => parameter.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) =>
        _items.FindIndex(parameter => parameter.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) =>
        _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
}
#pragma warning restore CS8765
