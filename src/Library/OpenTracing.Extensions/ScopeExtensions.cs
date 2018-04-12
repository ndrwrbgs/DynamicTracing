namespace OpenTracing.Contrib.DynamicTracing
{
    using System;
    using System.Threading.Tasks;

    using JetBrains.Annotations;

    internal static class ScopeExtensions
    {
        public static async Task ExecuteInScopeAsync(
            [NotNull] this ISpanBuilder spanBuilder,
            [NotNull] Func<Task> action)
        {
            using (IScope scope = spanBuilder.StartActive(true))
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Span.LogError(e);
                    throw;
                }
            }
        }

        public static async Task<T> ExecuteInScopeAsync<T>(
            [NotNull] this ISpanBuilder spanBuilder,
            // TODO: Parity, add overloads
            [NotNull] Func<ISpan, Task<T>> action)
        {
            using (IScope scope = spanBuilder.StartActive(true))
            {
                try
                {
                    return await action(scope.Span).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    scope.Span.LogError(e);
                    throw;
                }
            }
        }
    }
}