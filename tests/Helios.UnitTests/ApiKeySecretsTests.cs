using Helios.Application.Features.ApiKeys;
using Helios.Contracts.Catalogue;

namespace Helios.UnitTests;

public class ApiKeySecretsTests
{
    [Theory]
    [InlineData(ApiEnvironment.Sandbox, "hk_test_")]
    [InlineData(ApiEnvironment.Live, "hk_live_")]
    public void A_generated_key_parses_back_to_its_environment_and_public_id(ApiEnvironment environment, string prefix)
    {
        var key = ApiKeySecrets.Generate(environment);

        Assert.StartsWith(prefix, key.FullKey);
        Assert.Equal($"{prefix}{key.PublicId}", key.DisplayPrefix);
        Assert.Equal((environment, key.PublicId), ApiKeySecrets.Parse(key.FullKey));
        Assert.True(ApiKeySecrets.Matches(key.FullKey, key.Hash));
    }

    [Fact]
    public void The_stored_hash_does_not_contain_the_secret_and_rejects_any_alteration()
    {
        var key = ApiKeySecrets.Generate(ApiEnvironment.Sandbox);
        var altered = key.FullKey[..^1] + (key.FullKey[^1] == 'a' ? 'b' : 'a');

        Assert.Equal(32, key.Hash.Length);
        Assert.False(ApiKeySecrets.Matches(altered, key.Hash));
    }

    [Fact]
    public void Two_generated_keys_never_share_a_public_id_or_secret()
    {
        var first = ApiKeySecrets.Generate(ApiEnvironment.Sandbox);
        var second = ApiKeySecrets.Generate(ApiEnvironment.Sandbox);

        Assert.NotEqual(first.PublicId, second.PublicId);
        Assert.NotEqual(first.FullKey, second.FullKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sk_test_abc")]
    [InlineData("hk_prod_aaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("hk_test_short_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("hk_test_aaaaaaaaaaaaaaaa_tooshort")]
    public void Malformed_keys_do_not_parse(string? presented)
    {
        Assert.Null(ApiKeySecrets.Parse(presented));
    }
}
