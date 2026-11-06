// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferEnumMap
{
    public static TransferStateDto ToDto(TransferState state) => Convert<TransferState, TransferStateDto>(state);

    public static TransferBlockReasonDto ToDto(TransferBlockReason reason) => Convert<TransferBlockReason, TransferBlockReasonDto>(reason);

    public static TransferScopeKindDto ToDto(TransferScopeKind kind) => Convert<TransferScopeKind, TransferScopeKindDto>(kind);

    public static TransferNoticeKindDto ToDto(TransferNoticeKind kind) => Convert<TransferNoticeKind, TransferNoticeKindDto>(kind);

    public static ReasonCategoryDto ToDto(ReasonCategory category) => Convert<ReasonCategory, ReasonCategoryDto>(category);

    public static UserStatusDto ToDto(UserStatus status) => Convert<UserStatus, UserStatusDto>(status);

    public static TransferScopeKind ToDomain(TransferScopeKindDto kind) => Enum.IsDefined(kind) ? Convert<TransferScopeKindDto, TransferScopeKind>(kind) : default;

    public static ReasonCategory ToDomain(ReasonCategoryDto category) => Enum.IsDefined(category) ? Convert<ReasonCategoryDto, ReasonCategory>(category) : default;

    private static TTo Convert<TFrom, TTo>(TFrom value)
        where TFrom : struct, Enum
        where TTo : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }

        return Enum.Parse<TTo>(value.ToString(), ignoreCase: false);
    }
}
