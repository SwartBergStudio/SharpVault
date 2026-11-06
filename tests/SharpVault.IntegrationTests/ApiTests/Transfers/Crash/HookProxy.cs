// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public class HookProxy : DispatchProxy
{
    private object _inner = null!;
    private Func<MethodInfo, object?[]?, Task> _before = null!;

    public static T Wrap<T>(T inner, Func<MethodInfo, object?[]?, Task> before)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(before);

        var proxy = (HookProxy)(object)DispatchProxy.Create<T, HookProxy>();
        proxy._inner = inner;
        proxy._before = before;
        return (T)(object)proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.ReturnType == typeof(Task))
        {
            return RunVoidAsync(targetMethod, args);
        }

        if (targetMethod.ReturnType.IsGenericType && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(HookProxy)
                .GetMethod(nameof(RunAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType)
                .Invoke(this, [targetMethod, args]);
        }

        return CrashProxies.Forward(_inner, targetMethod, args);
    }

    private async Task RunVoidAsync(MethodInfo method, object?[]? args)
    {
        await _before(method, args);
        await (Task)CrashProxies.Forward(_inner, method, args)!;
    }

    private async Task<TResult> RunAsync<TResult>(MethodInfo method, object?[]? args)
    {
        await _before(method, args);
        return await (Task<TResult>)CrashProxies.Forward(_inner, method, args)!;
    }
}
