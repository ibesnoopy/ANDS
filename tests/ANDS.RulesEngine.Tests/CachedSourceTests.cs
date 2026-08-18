using ANDS.RulesEngine;

namespace ANDS.RulesEngine.Tests;

public sealed class CachedSourceTests
{
    [Fact]
    public async Task First_load_delegates_and_second_load_uses_cache()
    {
        var inner = new CountingSource();
        var source = new CachedRuleSource(inner);
        await source.LoadRulesAsync();
        await source.LoadRulesAsync();
        Assert.Equal(1, inner.LoadCount);
    }

    [Fact]
    public async Task Ttl_expiry_triggers_reload()
    {
        var inner = new CountingSource();
        using var source = new CachedRuleSource(inner, TimeSpan.FromMilliseconds(20));
        await source.LoadRulesAsync();
        await Task.Delay(50);
        await source.LoadRulesAsync();
        Assert.Equal(2, inner.LoadCount);
    }

    [Fact]
    public async Task Invalidate_forces_reload()
    {
        var inner = new CountingSource();
        using var source = new CachedRuleSource(inner);
        await source.LoadRulesAsync();
        source.Invalidate();
        await source.LoadRulesAsync();
        Assert.Equal(2, inner.LoadCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_ttl_is_rejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CachedRuleSource(new CountingSource(), TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task Concurrent_callers_only_load_once()
    {
        var inner = new CountingSource(delay: TimeSpan.FromMilliseconds(20));
        using var source = new CachedRuleSource(inner);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => source.LoadRulesAsync()));
        Assert.Equal(1, inner.LoadCount);
    }
}
