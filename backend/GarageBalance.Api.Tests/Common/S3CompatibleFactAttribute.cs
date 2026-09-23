namespace GarageBalance.Api.Tests.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class S3CompatibleFactAttribute : FactAttribute
{
    public const string EndpointVariable = "GARAGEBALANCE_S3_TEST_ENDPOINT";
    public const string AccessKeyVariable = "GARAGEBALANCE_S3_TEST_ACCESS_KEY";
    public const string SecretKeyVariable = "GARAGEBALANCE_S3_TEST_SECRET_KEY";
    public const string ExistingBucketVariable = "GARAGEBALANCE_S3_TEST_EXISTING_BUCKET";
    public const string RegionVariable = "GARAGEBALANCE_S3_TEST_REGION";
    public const string KmsKeyIdVariable = "GARAGEBALANCE_S3_TEST_KMS_KEY_ID";

    public S3CompatibleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AccessKeyVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretKeyVariable)))
        {
            Skip = "Set the isolated S3-compatible test endpoint and credentials to run this integration test.";
        }
        else if (string.Equals(Environment.GetEnvironmentVariable(EndpointVariable), "https://s3.cloud.ru", StringComparison.OrdinalIgnoreCase) &&
                 string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KmsKeyIdVariable)))
        {
            Skip = "Cloud.ru requires a configured KMS key id for encrypted provider certification.";
        }
    }
}
