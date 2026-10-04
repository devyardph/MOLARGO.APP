using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using DYS.Molargo.Services.Documents;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Wires the document store the <c>Storage</c> section asks for.
/// </summary>
public static class StorageRegistration
{
    /// <summary>
    /// Registers one <see cref="IDocumentStore"/>, chosen by configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as <c>AddMolargoCache</c>: the choice lives here and nothing else in
    /// the app knows it was made. Everything above <c>IDocumentStore</c> deals in a key and
    /// a stream.
    /// </para>
    /// <para>
    /// Misconfiguration is refused at startup rather than at the first upload. A bucket name
    /// that is missing would otherwise surface as a failed upload weeks later, on the day
    /// somebody scans a radiograph — and the practice would reasonably read that as the
    /// scan being the problem.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMolargoDocumentStore(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>()
            ?? new StorageOptions();

        services.AddSingleton(options);

        if (options.Provider == StorageProvider.Local)
        {
            services.AddSingleton<IDocumentPathProvider, ServerDocumentPathProvider>();
            services.AddSingleton<IDocumentStore, LocalDocumentStore>();

            return services;
        }

        if (string.IsNullOrWhiteSpace(options.S3.Bucket))
        {
            throw new InvalidOperationException(
                "Storage:Provider is S3 but Storage:S3:Bucket is not set. Name the bucket, "
                + "or set Storage:Provider to Local to use the server's own disk.");
        }

        services.AddSingleton<IAmazonS3>(_ => BuildClient(options.S3));
        services.AddSingleton<IDocumentStore, S3DocumentStore>();

        return services;
    }

    /// <summary>
    /// The S3 client, pointed at whichever implementation of the protocol this is.
    /// </summary>
    /// <remarks>
    /// AWS is the special case here, not the general one: a <c>ServiceUrl</c> covers
    /// Cloudflare R2, MinIO, Supabase Storage and anything else that speaks S3, while an
    /// empty one means real AWS and the region decides which endpoint.
    /// </remarks>
    private static IAmazonS3 BuildClient(StorageOptions.S3Options s3)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = s3.ForcePathStyle,
        };

        if (!string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            config.ServiceURL = s3.ServiceUrl;

            // Non-AWS endpoints still want a region in the signature even though they
            // ignore it. R2 names its own; the rest are happy with anything consistent.
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(s3.Region)
                ? "auto"
                : s3.Region;
        }
        else if (!string.IsNullOrWhiteSpace(s3.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region);
        }

        // No keys means the machine's own credentials — an instance role, a pod identity,
        // a developer's profile. Preferred where it exists, because those rotate on their
        // own and are never written down anywhere this app can leak them.
        if (string.IsNullOrWhiteSpace(s3.AccessKey) || string.IsNullOrWhiteSpace(s3.SecretKey))
        {
            return new AmazonS3Client(config);
        }

        return new AmazonS3Client(
            new BasicAWSCredentials(s3.AccessKey, s3.SecretKey), config);
    }
}
