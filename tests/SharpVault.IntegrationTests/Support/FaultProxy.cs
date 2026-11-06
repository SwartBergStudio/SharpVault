// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SharpVault.IntegrationTests.Support;

public class FaultProxy<T> : DispatchProxy
    where T : class
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    private T _inner = null!;

    public Func<string, int, Task>? Before { get; set; }

    public Func<string, int, Task>? After { get; set; }

    public void Attach(T inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    public int CallsOf(string method) => _calls.TryGetValue(method, out var count) ? count : 0;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var name = targetMethod.Name;
        var number = _calls.AddOrUpdate(name, 1, (_, current) => current + 1);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
        {
            return RunAsync(targetMethod, args, name, number);
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return typeof(FaultProxy<T>)
                .GetMethod(nameof(RunAsyncOf), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(returnType.GetGenericArguments()[0])
                .Invoke(this, [targetMethod, args, name, number]);
        }

        return Call(targetMethod, args);
    }

    private async Task RunAsync(MethodInfo method, object?[]? args, string name, int number)
    {
        if (Before is { } before)
        {
            await before(name, number);
        }

        await (Task)Call(method, args)!;
        if (After is { } after)
        {
            await after(name, number);
        }
    }

    private async Task<TResult> RunAsyncOf<TResult>(MethodInfo method, object?[]? args, string name, int number)
    {
        if (Before is { } before)
        {
            await before(name, number);
        }

        var result = await (Task<TResult>)Call(method, args)!;
        if (After is { } after)
        {
            await after(name, number);
        }

        return result;
    }

    private object? Call(MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

public static class FaultProxy
{
    public static FaultProxy<T> Wrap<T>(T inner, out T proxy)
        where T : class
    {
        proxy = DispatchProxy.Create<T, FaultProxy<T>>();
        var control = (FaultProxy<T>)(object)proxy;
        control.Attach(inner);
        return control;
    }
}
