// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class OversizeTarget
{
    public Guid? SecretId { get; set; }
}

public static class OversizeStagingProxies
{
    public static ITransferStagingStore Wrap(ITransferStagingStore inner, OversizeTarget target)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(target);

        var proxy = (OversizeStagingProxy)(object)DispatchProxy.Create<ITransferStagingStore, OversizeStagingProxy>();
        proxy.Initialize(inner, target);
        return (ITransferStagingStore)(object)proxy;
    }
}

public class OversizeStagingProxy : DispatchProxy
{
    private const string StageSecretsMethod = nameof(ITransferStagingStore.StageSecretsAsync);

    private ITransferStagingStore _inner = null!;
    private OversizeTarget _target = null!;

    internal void Initialize(ITransferStagingStore inner, OversizeTarget target)
    {
        _inner = inner;
        _target = target;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name != StageSecretsMethod || _target.SecretId is not { } oversizedId)
        {
            return CrashProxies.Forward(_inner, targetMethod, args);
        }

        var items = (IReadOnlyList<StagedSecret>)args![1]!;
        var forced = items.Where(item => item.Secret.Id == oversizedId).Select(item => item.Secret.Id).ToList();
        args[1] = items.Where(item => item.Secret.Id != oversizedId).ToList();
        var inner = (Task<IReadOnlyList<Guid>>)CrashProxies.Forward(_inner, targetMethod, args)!;
        return AppendForcedAsync(inner, forced);
    }

    private static async Task<IReadOnlyList<Guid>> AppendForcedAsync(Task<IReadOnlyList<Guid>> inner, List<Guid> forced)
    {
        var oversized = await inner;
        return [.. oversized, .. forced];
    }
}
