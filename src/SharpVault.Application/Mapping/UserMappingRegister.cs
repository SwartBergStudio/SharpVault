// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

namespace SharpVault.Application.Mapping;

public sealed class UserMappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<User, UserSummaryDto>()
            .Map(d => d.UserId, s => s.Id)
            .Map(d => d.Status, s => ToDto(s.Status));
    }

    private static UserStatusDto ToDto(UserStatus status) => status switch
    {
        UserStatus.Locked => UserStatusDto.Locked,
        UserStatus.Active => UserStatusDto.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
