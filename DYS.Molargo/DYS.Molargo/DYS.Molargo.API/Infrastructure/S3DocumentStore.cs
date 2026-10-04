using Amazon.S3;
using Amazon.S3.Model;
using DYS.Molargo.Services.Documents;

namespace DYS.Molargo.Api.Infrastructure;

/// <summary>
/// Patient files in an S3-compatible bucket.
/// </summary>
/// <remarks>
/// <para>
/// The implementation <c>IDocumentStore</c> was written for. <see cref="LocalDocumentStore"/>
/// writes to the machine the request landed on, which is correct for one server and silently
/// wrong for two: a file uploaded through one instance is not on the other, so the same
/// document is there for one member of staff and missing for another.
/// </para>
/// <para>
/// In the API project rather than beside the local store in Services, and that is on
/// purpose: Services is referenced by the device app, and putting an AWS SDK there would
/// carry it into every phone build to be used by nothing. The API is the only host that
/// ever touches a document store.
/// </para>
/// <para>
/// The key format is unchanged from the local store — <c>patients/{id}/{guid}-{name}</c> —
/// so a practice that has been running on local disk can be moved by copying the directory
/// into the bucket, with nothing in the database to rewrite.
/// </para>
/// </remarks>
internal sealed class S3DocumentStore : IDocumentStore
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3DocumentStore> _log;

    public S3DocumentStore(IAmazonS3 s3, StorageOptions options, ILogger<S3DocumentStore> log)
    {
        _s3 = s3;
        _bucket = options.S3.Bucket;
        _log = log;
    }

    public async Task<StoredFile> SaveAsync(
        Guid patientId, string fileName, Stream content, CancellationToken ct = default)
    {
        // The same key shape as the local store, including the GUID. Two patients uploading
        // "scan.jpg" must not collide and neither must one patient uploading it twice —
        // and S3 has no equivalent of FileMode.CreateNew to catch it if they do: a PUT to
        // an existing key overwrites it without a word.
        var key = DocumentKey.For(patientId, fileName);

        // Spooled to a temporary file before the PUT, which is the part worth explaining.
        //
        // The upload arrives as a forward-only stream of unknown length, and S3 wants
        // either a length or chunked encoding to sign the payload. What the SDK does with
        // an unseekable stream differs between AWS, R2 and MinIO, and the failure is not
        // the kind you find in testing: it is a radiograph that uploads on the developer's
        // machine and is rejected by the practice's bucket.
        //
        // A temp file removes the question. It costs one write of at most the upload cap —
        // 25 MB — against a correctness problem that would otherwise surface in production.
        // The alternative, buffering in memory, is the same cost held somewhere far worse:
        // several concurrent radiographs would be tens of megabytes each on the heap.
        var spool = Path.Combine(Path.GetTempPath(), $"molargo-upload-{Guid.NewGuid():n}");

        try
        {
            long written;

            // DeleteOnClose as well as the finally below. The finally covers an orderly
            // failure; this covers the process being killed mid-upload, which would
            // otherwise leave a copy of a patient's file in the server's temp directory.
            await using (var file = new FileStream(
                spool, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, ct).ConfigureAwait(false);

                written = file.Length;

                file.Position = 0;

                await _s3.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        InputStream = file,
                    },
                    ct).ConfigureAwait(false);
            }

            // From the spooled file, which is the only place the true count exists: the
            // source is an unseekable upload stream whose Length throws or reads zero, and
            // S3 does not report what it wrote. That is how every document once showed as
            // "0 B".
            return new StoredFile(key, written);
        }
        finally
        {
            // A patient's file, in a world-readable temp directory. Removed whether the
            // upload succeeded or not.
            try
            {
                if (File.Exists(spool)) File.Delete(spool);
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    ex, "Could not remove the temporary upload at {Path}.", spool);
            }
        }
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3
                .GetObjectAsync(_bucket, key, ct)
                .ConfigureAwait(false);

            // The response stream, not a copy. The caller streams it to the browser, and
            // buffering here would hold a radiograph in memory per concurrent download.
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Null, not an exception. The contract says a key with nothing behind it is a
            // missing file, and the documents tab renders that as a row whose file has
            // gone rather than as a failure.
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        // S3 deletes are already idempotent: removing a key that is not there succeeds.
        // That matches the contract, which needs a row whose file has gone to stay
        // deletable.
        await _s3.DeleteObjectAsync(_bucket, key, ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3
                .GetObjectMetadataAsync(_bucket, key, ct)
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            // Anything else is the bucket being unreachable or a credential being wrong,
            // which is not the same as the file being absent. Reported as present, because
            // this answer only drives a "file missing" warning on screen and telling a
            // practice their records have vanished because the network blinked is the worse
            // of the two mistakes.
            _log.LogWarning(
                ex, "Could not check whether {Key} exists; assuming it does.", key);

            return true;
        }
    }
}
