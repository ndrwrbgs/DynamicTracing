namespace OpenTracing.Contrib.DynamicInstrumentation
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading.Tasks;

    using Castle.DynamicProxy;

    using global::OpenTracing.Contrib.DynamicInstrumentation.OpenTracing.Extensions;
    using global::OpenTracing.Util;

    using JetBrains.Annotations;

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

        /// <summary>
        /// <see cref="WrapInterfaceWithTracingAdapter{TInterface}(object,TracingInterceptorOptions)"/>,
        /// but with the ability to modify the <see cref="ISpanBuilder"/> before <see cref="ISpanBuilder.Start"/> is called.
        /// .
        /// Standard use case: `IRunnable interface` with `Run` method. Wrapping Run is wonderful, but it does not
        /// have all the information that would be useful if the user instead called `static Runner.Run(string command)`.
        /// Capturing that kind of state of the <paramref name="instance"/> can be useful.
        /// </summary>
        public static TInterface WrapInterfaceWithTracingAdapter<TInterface>(
            this object instance,
            TracingInterceptorOptions options,
            Func<ISpanBuilder, ISpanBuilder> spanBuilderTransform)
        {
            return (TInterface)ProxyGenerator.CreateInterfaceProxyWithTargetInterface(
                typeof(TInterface),
                instance,
                ProxyGenerationOptions.Default,
                new TracingIntercepter(options, spanBuilderTransform));
        }

        public sealed class TracingInterceptorOptions
        {
            [Obsolete("Use the format with the Func")]
            public TracingInterceptorOptions(
                bool useArgumentsAsTags,
                bool wrapInterfaces = false,
                bool recursivelyWrapInterfaces = false,
                bool includeClassName = true,
                bool logResult = false)
                : this (
                    useArgumentsAsTags ? new Func<object, string>((value) => value?.ToString()) : null,
                    logResult ? new Func<object, string>((value) => value?.ToString()) : null,
                    wrapInterfaces,
                    recursivelyWrapInterfaces,
                    includeClassName)
            {
            }

            /// <param name="formatArgumentForTag">If null, arguments will not be set as tags on the spans</param>
            /// <param name="formatResultForTag">If null, result value will not be set as tags on the spans</param>
            public TracingInterceptorOptions(
                Func<object, string> formatArgumentForTag,
                Func<object, string> formatResultForTag,
                bool wrapInterfaces = false,
                bool recursivelyWrapInterfaces = false,
                bool includeClassName = true)
            {
                if (recursivelyWrapInterfaces && !wrapInterfaces)
                {
                    throw new ArgumentException("If you're not wrapping interfaces, you can't recursively wrap interfaces.");
                }

                this.FormatArgumentForTag = formatArgumentForTag;
                this.FormatResultForTag = formatResultForTag;
                this.WrapInterfaces = wrapInterfaces;
                this.RecursivelyWrapInterfaces = recursivelyWrapInterfaces;
                this.IncludeClassName = includeClassName;
            }

            public Func<object, string> FormatResultForTag { get; }
            public bool WrapInterfaces { get; }
            public bool RecursivelyWrapInterfaces { get; }
            public bool IncludeClassName { get; }
            public Func<object, string> FormatArgumentForTag { get; }
        }

        private sealed class TracingIntercepter : AsyncInterceptorBase
        {
            private readonly TracingInterceptorOptions options;
            private readonly Func<ISpanBuilder, ISpanBuilder> spanBuilderTransform;

            public TracingIntercepter(
                TracingInterceptorOptions options,
                Func<ISpanBuilder, ISpanBuilder> spanBuilderTransform = null)
            {
                this.options = options;
                this.spanBuilderTransform = spanBuilderTransform;
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

                if (this.options.FormatArgumentForTag != null)
                {
                    ParameterInfo[] parameterInfos = invocation.Method.GetParameters();
                    for (int i = 0; i < parameterInfos.Length; i++)
                    {
                        var param = parameterInfos[i];
                        var formattedValue = this.options.FormatArgumentForTag(invocation.Arguments[i]);
                        spanBuilder.WithTag(param.Name, formattedValue);
                    }
                }

                if (this.spanBuilderTransform != null)
                {
                    spanBuilder = this.spanBuilderTransform(spanBuilder);
                }

                return await spanBuilder
                    .ExecuteInScopeAsync(
                        async (span) =>
                        {
                            var result = await proceed(invocation)
                                .ConfigureAwait(false);

                            if (this.options.FormatResultForTag != null)
                            {
                                var formattedResult = this.options.FormatResultForTag(result);
                                span.Log(
                                    new Dictionary<string, object>(1)
                                    {
                                        ["result"] = formattedResult
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
                                    this.options.FormatArgumentForTag,
                                    this.options.FormatResultForTag,
                                    wrapInterfaces: false,
                                    recursivelyWrapInterfaces: false);
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
