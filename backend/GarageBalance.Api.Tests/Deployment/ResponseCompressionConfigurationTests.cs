using System.IO.Compression;
using GarageBalance.Api.Infrastructure.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class ResponseCompressionConfigurationTests
{
    [Theory]
    [InlineData("application/json", "br, gzip", "br")]
    [InlineData("application/problem+json", "gzip", "gzip")]
    public void Configuration_CompressesDynamicJsonOverHttps(
        string contentType,
        string acceptEncoding,
        string expectedEncoding)
    {
        using var services = CreateServices();
        var provider = services.GetRequiredService<IResponseCompressionProvider>();
        var context = new DefaultHttpContext
        {
            Request = { Scheme = "https" },
            Response = { ContentType = contentType }
        };
        context.Request.Headers.AcceptEncoding = acceptEncoding;

        Assert.True(provider.CheckRequestAcceptsCompression(context));
        Assert.True(provider.ShouldCompressResponse(context));
        Assert.Equal(expectedEncoding, provider.GetCompressionProvider(context)?.EncodingName);
    }

    [Fact]
    public void Configuration_DoesNotCompressAlreadyEncodedResponse()
    {
        using var services = CreateServices();
        var provider = services.GetRequiredService<IResponseCompressionProvider>();
        var context = new DefaultHttpContext
        {
            Request = { Scheme = "https" },
            Response = { ContentType = "application/json" }
        };
        context.Request.Headers.AcceptEncoding = "br, gzip";
        context.Response.Headers.ContentEncoding = "gzip";

        Assert.False(provider.ShouldCompressResponse(context));
    }

    [Fact]
    public void Configuration_UsesFastCompressionToProtectVpsCpu()
    {
        using var services = CreateServices();

        Assert.Equal(
            CompressionLevel.Fastest,
            services.GetRequiredService<IOptions<BrotliCompressionProviderOptions>>().Value.Level);
        Assert.Equal(
            CompressionLevel.Fastest,
            services.GetRequiredService<IOptions<GzipCompressionProviderOptions>>().Value.Level);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGarageBalanceResponseCompression();
        return services.BuildServiceProvider();
    }
}
