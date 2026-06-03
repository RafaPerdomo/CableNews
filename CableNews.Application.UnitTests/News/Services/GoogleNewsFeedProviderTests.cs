namespace CableNews.Application.UnitTests.News.Services;

using CableNews.Infrastructure.Services;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class GoogleNewsFeedProviderTests
{
    [Test]
    public void DecodeGoogleNewsUrl_ValidBase64_ShouldReturnDecodedUrl()
    {
        var base64 = "CBMiM2h0dHBzOi8vd3d3LmVsZXNwZWN0YWRvci5jb20vZWJvbGEtZW4tdmVuZXp1ZWxhL9IBAA";
        var expected = "https://www.elespectador.com/ebola-en-venezuela/";

        var result = GoogleNewsFeedProvider.DecodeGoogleNewsUrl(base64);

        result.ShouldBe(expected);
    }

    [Test]
    public void DecodeGoogleNewsUrl_InvalidBase64_ShouldReturnNull()
    {
        var base64 = "invalid-base64-string-here!!";

        var result = GoogleNewsFeedProvider.DecodeGoogleNewsUrl(base64);

        result.ShouldBeNull();
    }

    [Test]
    public void DecodeGoogleNewsUrl_EmptyString_ShouldReturnNull()
    {
        var result = GoogleNewsFeedProvider.DecodeGoogleNewsUrl(string.Empty);

        result.ShouldBeNull();
    }
}
