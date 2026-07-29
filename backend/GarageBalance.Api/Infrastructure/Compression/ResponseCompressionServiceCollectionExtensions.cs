using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace GarageBalance.Api.Infrastructure.Compression;

public static class ResponseCompressionServiceCollectionExtensions
{
    public static IServiceCollection AddGarageBalanceResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);
        return services;
    }
}
