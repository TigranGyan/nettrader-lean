using System;
using System.Collections.Generic;
using System.IO;
using NetTrader.Lean.Algorithm.Alpha;
using NetTrader.Lean.Algorithm.Common;
using NetTrader.Lean.Algorithm.PortfolioConstruction;
using QuantConnect;
using QuantConnect.Algorithm.Framework.Alphas;
using Skender.Stock.Indicators;
using Xunit;

namespace NetTrader.Lean.Algorithm.Tests;

public class AlgorithmTests
{
    [Fact]
    public void ComputeFeatures_ReturnsValidMlInput_WhenSufficientQuotesProvided()
    {
        var q30m = CreateMockQuotes(220, TimeSpan.FromMinutes(30));
        var q1h = CreateMockQuotes(30, TimeSpan.FromHours(1));
        var q2h = CreateMockQuotes(20, TimeSpan.FromHours(2));
        var q4h = CreateMockQuotes(25, TimeSpan.FromHours(4));

        var input = MlFeatureBuilder.ComputeFeatures(q30m, q1h, q2h, q4h, 50f, 0.5f, 55f, 5200f);

        Assert.NotNull(input);
        Assert.Equal(50f, input.FearGreedIndex);
        Assert.Equal(0.5f, input.BtcTrend_1h);
        Assert.Equal(55f, input.BtcRsi_1h);
        Assert.Equal(5200f, input.BtcDominance);
        Assert.True(input.Adx_30m >= 0);
    }

    [Fact]
    public void ComputeFeatures_ReturnsNull_WhenInsufficientQuotesProvided()
    {
        var q30m = CreateMockQuotes(100, TimeSpan.FromMinutes(30));
        var q1h = CreateMockQuotes(10, TimeSpan.FromHours(1));
        var q2h = CreateMockQuotes(5, TimeSpan.FromHours(2));
        var q4h = CreateMockQuotes(5, TimeSpan.FromHours(4));

        var input = MlFeatureBuilder.ComputeFeatures(q30m, q1h, q2h, q4h, 50f, 0f, 50f, 5000f);

        Assert.Null(input);
    }

    [Fact]
    public void ModelsZip_FilesExist()
    {
        var repoRoot = FindRepoRoot();
        var longPath = Path.Combine(repoRoot, "models", "ModelLong.zip");
        var shortPath = Path.Combine(repoRoot, "models", "ModelShort.zip");

        Assert.True(File.Exists(longPath), $"Expected {longPath} to exist.");
        Assert.True(File.Exists(shortPath), $"Expected {shortPath} to exist.");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "README.md")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? AppContext.BaseDirectory;
    }

    private static List<Quote> CreateMockQuotes(int count, TimeSpan interval)
    {
        var quotes = new List<Quote>();
        var startTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal price = 100m;

        for (int i = 0; i < count; i++)
        {
            price += (i % 2 == 0 ? 0.5m : -0.3m);
            quotes.Add(new Quote
            {
                Date = startTime.Add(interval * i),
                Open = price - 0.1m,
                High = price + 0.5m,
                Low = price - 0.5m,
                Close = price,
                Volume = 1000m + i
            });
        }

        return quotes;
    }
}
