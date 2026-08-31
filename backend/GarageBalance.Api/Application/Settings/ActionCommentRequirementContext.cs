namespace GarageBalance.Api.Application.Settings;

public static class ActionCommentRequirementContext
{
    private static readonly AsyncLocal<bool?> CurrentValue = new();

    public static bool IsRequired => CurrentValue.Value ?? true;

    public static IDisposable Push(bool required)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = required;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(bool? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentValue.Value = previous;
            _disposed = true;
        }
    }
}
