// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Api.Endpoints.Vault;

public static class VaultEndpointConventions
{
    public static RouteHandlerBuilder ProducesVaultProblems(this RouteHandlerBuilder builder, bool itemRoute, bool concurrent = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        if (itemRoute)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound);
        }

        if (concurrent)
        {
            builder.ProducesProblem(StatusCodes.Status409Conflict);
        }

        return builder;
    }
}
