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

## Func<object, string> FormatArgumentForTag

If non-null, this is called for each argument passed to the method (e.g. `a` and `b` on `ISomething.Add` above), and the result is set as a tag on the span for the method call. The key for the tag will be the parameter name.

### Example

```C#
ISomething something = new FakeSomething().WrapInterfaceWithTracingAdapter<ISomething>(
  formatArgumentForTag: obj => "number " + obj,
  /* ... */);
something.Add(1, 2);
```

The span produced will be
'FakeSomething.Add' with tags { ["a"] = "number 1", ["b"] = "number 2" }


## Func<object, string> FormatResultForTag

If non-null, this is called for the return value of the method, and the result is set as a tag on the span for the method call. The key for the tag will be `result`, until/unless the [OpenTracing Semantic Conventions spec](https://github.com/opentracing/specification/blob/master/semantic_conventions.md) provides a standard for this.

### Example

```C#
ISomething something = new FakeSomething().WrapInterfaceWithTracingAdapter<ISomething>(
  formatResultForTag: (Task<int> intTask) => "value: " + intTask.Result,
  /* ... */);
something.Add(1, 2);
```

The span produced will be
'FakeSomething.Add' with tag { ["result"] = "value: 3" }

## bool WrapInterfaces

If this is true, and the return value for a method on the interface is also an interface, that return value will be wrapped with tracing as well.

### Example 1

```C#
// Note that this example is for demonstrative purposes, as actually right now System.* namespaces are explicitly excluded from this, specifically because of this scenario here
IEnumerable sample = /* ... */;
IEnumerable tracedSample = sample.WrapInterfaceWithTracingAdapter<ISomething>(
  wrapInterfaces: true,
  /* ... */);

IEnumerator enumerator = tracedSample.GetEnumerator(); // Starts/Finishes an IEnumerable.GetEnumerator span
enumerator.MoveNext(); // Starts/Finishes an IEnumerator.MoveNext span
```

### Example 2
Show the difference versus RecursivelyWrapInterfaces
```C#

interface ICount
{
  int Count { get; }
  ICount MePlusOne();
}

ICount sample = /* ... */;
IEnumerable tracedSample = sample.WrapInterfaceWithTracingAdapter<ICount>(
  wrapInterfaces: true,
  recursivelyWrapInterfaces: false,
  /* ... */);

tracedSample
  .MePlusOne() // Starts/Finishes an ICount.MePlusOne span
  .MePlusOne() // No tracing
  .MePlusOne();// No tracing
```

## bool RecursivelyWrapInterfaces

Please see WrapInterfaces first.

This is akin to WrapInterfaces, except that instead of 1-level down it will wrap results for as long as methods are returning interfaces.

### Example

```C#
interface ICount
{
  int Count { get; }
  ICount MePlusOne();
}

ICount sample = /* ... */;
IEnumerable tracedSample = sample.WrapInterfaceWithTracingAdapter<ICount>(
  wrapInterfaces: true,
  recursivelyWrapInterfaces: true,
  /* ... */);

tracedSample
  .MePlusOne() // Starts/Finishes an ICount.MePlusOne span
  .MePlusOne() // Starts/Finishes an ICount.MePlusOne span
  .MePlusOne();// Starts/Finishes an ICount.MePlusOne span
```

## bool IncludeClassNames

If true, the concrete implementation's class name is prepended to the OperationName for the span. E.g. `FakeSomething.Add`.
If false, only the method names are used for the OperationName. E.g. `Add`

# Caveats/Notes
* Note - by default async method spans will not be completed until the Task is completed.
* The wrapped interface must be visible to Castle DynamicProxy. See https://stackoverflow.com/questions/28234369/how-to-do-internal-interfaces-visible-for-moq for a related issue and how to fix it.
