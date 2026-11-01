// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Users;

namespace SharpVault.Infrastructure.Users;

public sealed class UserStorageMappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<User, UserTableEntity>()
            .Map(d => d.PartitionKey, _ => UserKeys.PartitionKey)
            .Map(d => d.RowKey, s => UserKeys.RowKey(s.Id))
            .Map(d => d.Status, s => s.Status.ToString())
            .Ignore(d => d.ETag)
            .Ignore(d => d.Timestamp!);

        config.NewConfig<UserTableEntity, User>()
            .Map(d => d.Id, s => Guid.ParseExact(s.RowKey, "D"))
            .Map(d => d.Status, s => UserStatusText.Parse(s.Status));
    }
}
