# ANDS Rules Engine

ANDS is a .NET 10 rules engine for evaluating declarative JSON rules against
dictionary or POCO facts. Conditions are data, while actions are dispatched
through application-provided handlers.

## Rule format

Property names are case-insensitive and enum values are strings. A rule has a
stable ID, priority (lower values run first), condition tree, and action list:

```json
{
  "rules": [
    {
      "id": "high-value-order",
      "name": "High value order",
      "description": "Notify the sales team",
      "priority": 10,
      "enabled": true,
      "condition": {
        "type": "group",
        "group": "all",
        "conditions": [
          { "type": "comparison", "field": "order.total",
            "operator": "greaterThanOrEqual", "value": 1000 },
          { "type": "comparison", "field": "order.customer.email",
            "operator": "isNotNull" }
        ]
      },
      "actions": [
        { "type": "send-notification",
          "parameters": { "channel": "sales", "template": "high-value" } }
      ]
    }
  ]
}
```

The file source also accepts a bare array instead of the `rules` wrapper.
Supported operators are `equal`, `notEqual`, `greaterThan`,
`greaterThanOrEqual`, `lessThan`, `lessThanOrEqual`, `contains`, `startsWith`,
`endsWith`, `in`, `notIn`, `matches`, `isNull`, and `isNotNull`.

`all` over zero children is true, `any` over zero children is false, and
`none` over zero children is true. Missing paths are not null: `isNull` only
matches a present path whose value is null, while `isNotNull` requires a
present non-null path. Other operators return false for missing paths.
Duplicate rule IDs are allowed by the sources and are preserved in source
order; callers that require unique IDs should validate that policy before
evaluation.

Numbers use invariant-culture numeric conversion, dates use invariant
round-trip parsing, booleans accept booleans (and exact `"true"`/`"false"`
strings), and ordinary strings are never silently converted from arbitrary
objects. String comparisons are case-insensitive by default and can be made
case-sensitive with `RulesEngineOptions`.

For a present value, a type mismatch is an evaluation error rather than a
silent non-match. This applies to ordering, `contains`, `startsWith`,
`endsWith`, `matches`, and invalid `in`/`notIn` operands. Configure
`RuleErrorBehavior.Continue` to record the error and evaluate later rules;
`Abort` stops evaluation after the first rule error.

## Usage

```csharp
var handlers = new[] { new MyActionHandler() };
var engine = new RulesEngine(handlers, new RulesEngineOptions
{
    StopOnFirstMatch = false,
    UnknownActionBehavior = UnknownActionBehavior.RecordError,
    RuleErrorBehavior = RuleErrorBehavior.Continue
});

var rules = await new JsonFileRuleSource("rules.json").LoadRulesAsync();
var result = await engine.EvaluateAsync(rules, facts);
```

Implement `IRuleActionHandler` to register application behavior:

```csharp
public sealed class MyActionHandler : IRuleActionHandler
{
    public string ActionType => "send-notification";

    public Task HandleAsync(RuleAction action, RuleActionContext context,
        CancellationToken cancellationToken)
    {
        // Read action.Parameters and context.Facts, then perform the application action.
        return Task.CompletedTask;
    }
}
```

The default unknown-action behavior is `Throw`; use `RecordError` to return
unknown actions as errors instead. `CachedRuleSource` adds optional TTL caching
and manual `Invalidate()`.

## SQL Server / SQL Express source

The SQL source uses `Microsoft.Data.SqlClient` and requires the caller to
provide a connection string. The default table and columns are:

```sql
CREATE TABLE dbo.Rules
(
    Id          nvarchar(200) NOT NULL PRIMARY KEY,
    Name        nvarchar(300) NOT NULL,
    Description nvarchar(max) NULL,
    Priority    int NOT NULL,
    Enabled     bit NOT NULL,
    Definition  nvarchar(max) NOT NULL
);
```

`Definition` contains JSON with the condition and action fields:

```json
{
  "condition": {
    "type": "comparison",
    "field": "customer.tier",
    "operator": "equal",
    "value": "gold"
  },
  "actions": [{ "type": "award-points", "parameters": { "points": 100 } }]
}
```

```csharp
var source = new SqlRuleSource(new SqlRuleSourceOptions
{
    ConnectionString = configuration.GetConnectionString("Rules")!,
    Schema = "dbo",
    Table = "Rules"
});
var rules = await source.LoadRulesAsync();
```

Schema, table, and column identifiers are validated and bracket-quoted before
being placed in SQL. `IDbConnectionFactory` can be injected for tests or
application-specific connection management; no connection string is embedded
in the library.

## Build and test

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```
