namespace OpenTracing.Contrib.DynamicTracing
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading.Tasks;

    using Castle.DynamicProxy;

    using JetBrains.Annotations;

    using OpenTracing.Util;

    /// <summary>
    ///     Dynamically wraps an interface with tracing
    /// </summary>
    public static class DynamicTracingWrapper
    {
        private static readonly ProxyGenerator ProxyGenerator = new ProxyGenerator();

        public static TInterface WrapInterfaceWithTracingAdapter<TInterface>(
            this object instance,
            TracingInterceptorOptions options)
        {
            return (TInterface)ProxyGenerator.CreateInterfaceProxyWithTargetInterface(
                typeof(TInterface),
                instance,
                ProxyGenerationOptions.Default,
                new TracingIntercepter(options));
        }

        public sealed class TracingInterceptorOptions
        {
            public TracingInterceptorOptions(
                bool useArgumentsAsTags,
                bool wrapInterfaces = false,
                bool recursivelyWrapInterfaces = false,
                bool includeClassName = true,
                bool logResult = false)
            {
                if (recursivelyWrapInterfaces && !wrapInterfaces)
                {
                    throw new ArgumentException("If you're not wrapping interfaces, you can't recursively wrap interfaces.");
                }

                this.UseArgumentsAsTags = useArgumentsAsTags;
                this.WrapInterfaces = wrapInterfaces;
                this.RecursivelyWrapInterfaces = recursivelyWrapInterfaces;
                this.IncludeClassName = includeClassName;
                this.LogResult = logResult;
            }

            public bool UseArgumentsAsTags { get; }
            public bool WrapInterfaces { get; }
            public bool RecursivelyWrapInterfaces { get; }
            public bool IncludeClassName { get; }
            public bool LogResult { get; }
        }

        private sealed class TracingIntercepter : AsyncInterceptorBase
        {
            private readonly TracingInterceptorOptions options;

            public TracingIntercepter(TracingInterceptorOptions options)
            {
                this.options = options;
            }

            protected override async Task InterceptAsync([NotNull] IInvocation invocation, Func<IInvocation, Task> proceed)
            {
                await this.InterceptAsync(
                    invocation,
                    async invo =>
                    {
                        await proceed(invo)
                            .ConfigureAwait(false);
                        return 1;
                    })
                    .ConfigureAwait(false);
            }

            protected override async Task<TResult> InterceptAsync<TResult>([NotNull] IInvocation invocation, Func<IInvocation, Task<TResult>> proceed)
            {
                // Must not have any await before await proceed, due to bug in the AsyncInterceptor library right now

                string operationName = invocation.Method.Name;

                if (this.options.IncludeClassName)
                {
                    operationName = invocation.TargetType.FullName + "." + operationName;
                }

                var spanBuilder = GlobalTracer.Instance
                    .BuildSpan(operationName);

                if (this.options.UseArgumentsAsTags)
                {
                    ParameterInfo[] parameterInfos = invocation.Method.GetParameters();
                    for (int i = 0; i < parameterInfos.Length; i++)
                    {
                        var param = parameterInfos[i];
                        // TODO: Should these use json instead?
                        spanBuilder.WithTag(param.Name, invocation.Arguments[i]?.ToString());
                    }
                }

                return await spanBuilder
                    .ExecuteInScopeAsync(
                        async (span) =>
                        {
                            var result = await proceed(invocation)
                                .ConfigureAwait(false);

                            if (this.options.LogResult)
                            {
                                span.Log(
                                    new Dictionary<string, object>(1)
                                    {
                                        ["result"] = result
                                    });
                            }

                            if (!this.options.WrapInterfaces)
                            {
                                return result;
                            }

                            if (ReferenceEquals(result, null))
                            {
                                return result;
                            }

                            if (!typeof(TResult).IsInterface)
                            {
                                return result;
                            }

                            // TODO: Permit this to be configured
                            if (typeof(TResult).Namespace?.StartsWith("System") == true)
                            {
                                return result;
                            }

                            var newOptions = this.options;
                            if (!this.options.RecursivelyWrapInterfaces)
                            {
                                newOptions = new TracingInterceptorOptions(
                                    this.options.UseArgumentsAsTags,
                                    false,
                                    false);
                            }

                            var newResult = WrapInterfaceWithTracingAdapter<TResult>(
                                result,
                                newOptions);
                            invocation.ReturnValue = newResult;
                            return newResult;

                        })
                    .ConfigureAwait(false);
            }
        }
    }
}
