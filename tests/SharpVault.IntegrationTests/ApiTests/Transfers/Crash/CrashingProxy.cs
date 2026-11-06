// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public static class CrashProxies
{
    public static T Wrap<T>(T inner, CrashPlan plan)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(plan);

        var proxy = (CrashingProxy)(object)DispatchProxy.Create<T, CrashingProxy>();
        proxy.Initialize(inner, plan);
        return (T)(object)proxy;
    }

    internal static object CrashAfter(object? result, string name)
    {
        if (result is not Task task)
        {
            throw new SimulatedCrashException(name);
        }

        var taskType = task.GetType();
        if (!taskType.IsGenericType)
        {
            return AwaitThenCrashAsync(task, name);
        }

        var resultType = taskType.GetGenericArguments()[0];
        var helper = typeof(CrashProxies)
            .GetMethod(nameof(AwaitThenCrashGenericAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(resultType);
        return helper.Invoke(null, [task, name])!;
    }

    internal static object? Forward(object target, MethodInfo targetMethod, object?[]? args)
    {
        try
        {
            return targetMethod.Invoke(target, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static async Task AwaitThenCrashAsync(Task task, string name)
    {
        await task.ConfigureAwait(false);
        throw new SimulatedCrashException(name);
    }

    private static async Task<TResult> AwaitThenCrashGenericAsync<TResult>(Task<TResult> task, string name)
    {
        await task.ConfigureAwait(false);
        throw new SimulatedCrashException(name);
    }
}

public class CrashingProxy : DispatchProxy
{
    private object _inner = null!;
    private CrashPlan _plan = null!;

    internal void Initialize(object inner, CrashPlan plan)
    {
        _inner = inner;
        _plan = plan;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var name = targetMethod.DeclaringType!.Name + "." + targetMethod.Name;
        var decision = _plan.Decide(name);
        if (decision == CrashDecision.Before)
        {
            throw new SimulatedCrashException(name);
        }

        var result = CrashProxies.Forward(_inner, targetMethod, args);
        return decision == CrashDecision.After ? CrashProxies.CrashAfter(result, name) : result;
    }
}
