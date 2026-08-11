using Appizza.Api;

namespace Appizza.UnitTests;

public sealed class Phase1SecurityTests
{
    private static readonly Phase1SecurityOptions Options = new()
    {
        SigningKey = "unit-test-signing-key-with-at-least-32-bytes",
        CpfEncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
        CpfHmacKey = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA="
    };

    [Fact]
    public void CpfProtectionUsesRandomNonceAndStableHmac()
    {
        var protector = new CpfProtector(Options);
        var first = protector.Protect("529.982.247-25");
        var second = protector.Protect("52998224725");

        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal("***.***.***-25", first.Masked);
        Assert.DoesNotContain("52998224725", first.Ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidCpfIsRejected()
    {
        var protector = new CpfProtector(Options);
        Assert.Throws<ArgumentException>(() => protector.Protect("111.111.111-11"));
    }

    [Fact]
    public void RefreshTokensAreOpaqueAndHashesAreStable()
    {
        var token = Phase1TokenService.NewRefreshToken();
        Assert.NotEqual(token, Phase1TokenService.HashToken(token));
        Assert.Equal(Phase1TokenService.HashToken(token), Phase1TokenService.HashToken(token));
    }
}
