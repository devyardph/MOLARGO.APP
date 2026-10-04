namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Where patient files are kept.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="CacheOptions"/>, and switched the same way: a provider name
/// in configuration, nothing in calling code. Everything above <c>IDocumentStore</c> deals
/// in a key and a stream and has no idea which of these answered.
/// </para>
/// <para>
/// This is the setting that decides whether the API can run on more than one server. Local
/// writes to the machine the request happened to land on, so a file uploaded through one
/// instance cannot be opened through another — the symptom is a document that is there for
/// some staff and missing for others, depending on which server they reached.
/// </para>
/// </remarks>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Which store answers.</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    public S3Options S3 { get; set; } = new();

    public sealed class S3Options
    {
        /// <summary>
        /// The endpoint, for anything that is not AWS itself.
        /// </summary>
        /// <remarks>
        /// Cloudflare R2, MinIO, Supabase Storage and the rest speak the S3 protocol at
        /// their own address. Left empty, the SDK talks to AWS and <see cref="Region"/>
        /// decides which one.
        /// </remarks>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>The AWS region. Ignored when <see cref="ServiceUrl"/> is set.</summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>The bucket holding every practice's files.</summary>
        /// <remarks>
        /// One bucket, with the tenant in the key rather than a bucket per practice. A
        /// bucket per tenant means provisioning one on signup, which is an operation that
        /// can fail halfway and leave a clinic that exists but cannot hold a file.
        /// </remarks>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>
        /// The access key, or empty to use the machine's own credentials.
        /// </summary>
        /// <remarks>
        /// Empty is the better answer on AWS: an instance role or a pod identity hands the
        /// SDK credentials that rotate on their own and are never written down. The
        /// explicit pair is here because R2 and MinIO have no such mechanism.
        ///
        /// Not committed either way — see the note on the Redis connection string.
        /// </remarks>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>The secret key. From the deployment's secret store, never a file.</summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Whether to address the bucket as a path segment rather than a subdomain.
        /// </summary>
        /// <remarks>
        /// True for MinIO and most self-hosted gateways, which serve
        /// <c>endpoint/bucket/key</c>. AWS and R2 want the bucket in the hostname and are
        /// fine with the default.
        /// </remarks>
        public bool ForcePathStyle { get; set; }
    }
}

/// <summary>Which document store is wired up.</summary>
public enum StorageProvider
{
    /// <summary>
    /// The API server's own filesystem. One server only.
    /// </summary>
    /// <remarks>
    /// The default, because it needs nothing provisioned and a clone of the repository has
    /// to work. It is the right choice for a single-server practice and the wrong one the
    /// moment a second instance exists.
    /// </remarks>
    Local = 0,

    /// <summary>Any S3-compatible object store: AWS, R2, MinIO, Supabase Storage.</summary>
    S3 = 1,
}
