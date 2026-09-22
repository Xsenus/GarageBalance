namespace GarageBalance.Api.Tests.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class S3CompatibleFactAttribute : FactAttribute
{
    public const string EndpointVariable = "GARAGEBALANCE_S3_TEST_ENDPOINT";
    public const string AccessKeyVariable = "GARAGEBALANCE_S3_TEST_ACCESS_KEY";
    public const string SecretKeyVariable = "GARAGEBALANCE_S3_TEST_SECRET_KEY";

    public S3CompatibleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AccessKeyVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretKeyVariable)))
        {
            Skip = "Set the isolated S3-compatible test endpoint and credentials to run this integration test.";
        }
    }
}
