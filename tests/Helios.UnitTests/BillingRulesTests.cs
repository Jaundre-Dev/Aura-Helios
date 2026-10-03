using System.Text.Json;
using Helios.Application.Features.Execution;
using Helios.Application.Features.Products;
using Helios.Domain.Billing;
using Helios.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Helios.UnitTests;

public class BillingRulesTests
{
    private static PriceVersion Price(decimal unitPrice, decimal minimum = 0m) => new()
    {
        Currency = "ZAR",
        Unit = "page",
        UnitPrice = unitPrice,
        MinimumCharge = minimum,
        TaxTreatment = "vat_exclusive_standard"
    };

    [Theory]
    [InlineData(0.35, 3, 1.05)]
    [InlineData(0.123456, 7, 0.864192)]
    [InlineData(0.0000005, 1, 0.000001)]   // half away from zero at the sixth place
    [InlineData(2.50, 0, 0)]
    [InlineData(2.50, -1, 0)]
    public void Charges_are_exact_to_six_places(decimal unitPrice, decimal quantity, decimal expected)
    {
        Assert.Equal(expected, Price(unitPrice).ChargeFor(quantity));
    }

    [Fact]
    public void The_minimum_charge_applies_when_usage_is_small()
    {
        Assert.Equal(1.50m, Price(0.20m, minimum: 1.50m).ChargeFor(2));
        Assert.Equal(4.00m, Price(0.20m, minimum: 1.50m).ChargeFor(20));
        Assert.Equal(0m, Price(0.20m, minimum: 1.50m).ChargeFor(0));   // nothing consumed, nothing charged
    }

    [Fact]
    public void Retry_delay_doubles_and_is_capped()
    {
        var policy = new ExecutionPolicy { RetryBaseDelay = TimeSpan.FromSeconds(5) };

        Assert.Equal(TimeSpan.FromSeconds(5), policy.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(40), policy.RetryDelay(4));
        Assert.Equal(policy.RetryDelay(11), policy.RetryDelay(50));
    }

    [Fact]
    public void Production_refuses_test_products_fake_payments_disabled_ssrf_protection_and_no_scanner()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Helios:Payments:Gateway"] = "fake-test",
            ["Helios:Webhooks:AllowPrivateNetworks"] = "true",
        }).Build();

        var problems = ProductionSafetyCheck.FindProblems(configuration, [new NamedExecutor("test.metered"), new NamedExecutor("identity.sa-id-validate")]).ToList();

        Assert.Equal(4, problems.Count);   // test product, fake gateway, SSRF bypass, no scanner
        Assert.Contains(problems, p => p.Contains("test.metered"));
        Assert.DoesNotContain(problems, p => p.Contains("identity.sa-id-validate"));
    }

    [Fact]
    public void A_clean_production_configuration_has_no_problems()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Helios:Uploads:Scanner"] = "clamav",
        }).Build();

        Assert.Empty(ProductionSafetyCheck.FindProblems(configuration, [new NamedExecutor("identity.sa-id-validate")]));
    }

    private sealed class NamedExecutor(string slug) : IProductExecutor
    {
        public string ProductSlug => slug;
        public string Version => "1";
        public ExecutionMode Mode => ExecutionMode.Synchronous;
        public bool SafeToRepeat => true;
        public ParsedProductInput Parse(JsonElement body) => throw new NotSupportedException();
        public decimal EstimateMaxUnits(ParsedProductInput input) => 1m;
        public Task<ProductOutcome> ExecuteAsync(ParsedProductInput input, ProductExecutionContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<ReconcileOutcome> ReconcileAsync(string? reference, ProductExecutionContext context, CancellationToken ct) => throw new NotSupportedException();
    }
}
