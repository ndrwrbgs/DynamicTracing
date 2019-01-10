[![NuGet](https://img.shields.io/nuget/v/OpenTracing.Contrib.DynamicInstrumentation.svg)](https://www.nuget.org/packages/OpenTracing.Contrib.DynamicInstrumentation)

# DynamicTracing
Uses Castle DynamicProxy to add OpenTracing instrumentation to an interface at runtime.

# Example
## Summary Example
```C#
IMyInterface instance = /* ... */;
IMyInterface tracedInstance = instance.WrapInterfaceWithTracingAdapter<ISomething>(/* settings... */);
tracedInstance.MethodCall(); // Will open a Span around the method call, and close it when the method is complete.
```

## Full Example
```C#

using System;

using OpenTracing;
using OpenTracing.Contrib.Decorators;
using OpenTracing.Contrib.DynamicInstrumentation;
using OpenTracing.Mock;
using OpenTracing.Util;

internal static class Program
{
    private static void Main()
    {
        GlobalTracer.Register(BuildTracer());

        ISomething something = new FakeSomething();

        var tracedSomething = something.WrapInterfaceWithTracingAdapter<ISomething>(
            new DynamicTracingWrapper.TracingInterceptorOptions(
                formatArgumentForTag: null,
                formatResultForTag: null,
                wrapInterfaces: false,
                recursivelyWrapInterfaces: false,
                includeClassName: true));

        tracedSomething.DoNothing("str"); // Outputs "Span FakeSomething.DoNothing started"
    }

#region Boilerplate for demo

    private static ITracer BuildTracer()
    {
        // Using https://www.nuget.org/packages/OpenTracing.Contrib.Decorators/ for a dev box tracer
        return new TracerDecoratorBuilder(new MockTracer())
            .OnSpanActivated((span, name) => Console.WriteLine($"Span {name} started"))
            .Build();
    }
}

// Type must be accessible to Castle's DynamicProxy. You can also use InternalsVisibleTo if required.
public interface ISomething
{
    void DoNothing(string str);
    int Add(int a, int b);
}

internal sealed class FakeSomething : ISomething
{
    public void DoNothing(string str) { }

    public int Add(int a, int b) => a + b;
}

#endregion
```

# TracingInterceptorOptions

[wiki](https://github.com/ndrwrbgs/DynamicTracing/wiki/TracingInterceptorOptions)

# Caveats/Notes
* Note - by default async method spans will not be completed until the Task is completed.
* The wrapped interface must be visible to Castle DynamicProxy. See https://stackoverflow.com/questions/28234369/how-to-do-internal-interfaces-visible-for-moq for a related issue and how to fix it.
