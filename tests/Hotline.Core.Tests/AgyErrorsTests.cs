using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AgyErrorsTests
{
    [Theory]
    [InlineData("UNAUTHENTICATED: please sign in", BackendErrorKind.NotLoggedIn)]
    [InlineData("You are not logged in", BackendErrorKind.NotLoggedIn)]
    [InlineData("RESOURCE_EXHAUSTED: quota exceeded", BackendErrorKind.RateLimited)]
    [InlineData("workspace is not trusted", BackendErrorKind.NotConfigured)]
    [InlineData("agy wasn't allowed to: read_file", BackendErrorKind.Unsupported)]
    [InlineData("something else broke", BackendErrorKind.Failed)]
    public void Maps_error_text_to_kind(string error, BackendErrorKind kind)
        => Assert.Equal(kind, AgyErrors.Map(error).Kind);

    [Fact]
    public void Messages_are_actionable()
    {
        Assert.Contains("agy", AgyErrors.Map("not logged in").Message);
        Assert.Contains("something else broke", AgyErrors.Map("something else broke").Message);
    }
}
