// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

public sealed record SharingMatrixEndpoint(
    string Id,
    Func<SharingTestApi, SharingScenario, Task<HttpResponseMessage>> Send,
    HttpStatusCode Owner,
    HttpStatusCode View,
    HttpStatusCode Edit,
    HttpStatusCode Manager,
    HttpStatusCode FolderViewer,
    HttpStatusCode FolderManager,
    HttpStatusCode Other,
    HttpStatusCode Admin)
{
    public HttpStatusCode ExpectedFor(CallerKind caller) => caller switch
    {
        CallerKind.User => Owner,
        CallerKind.ShareViewer => View,
        CallerKind.ShareEditor => Edit,
        CallerKind.ShareManager => Manager,
        CallerKind.FolderViewer => FolderViewer,
        CallerKind.FolderManager => FolderManager,
        CallerKind.OtherUser => Other,
        CallerKind.Admin => Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
    };
}

public static class SharingMatrixEndpoints
{
    public const string RequestName = "matrix-request-name";

    private const HttpStatusCode Ok = HttpStatusCode.OK;
    private const HttpStatusCode Created = HttpStatusCode.Created;
    private const HttpStatusCode NoContent = HttpStatusCode.NoContent;
    private const HttpStatusCode NotFound = HttpStatusCode.NotFound;

    public static IReadOnlyList<CallerKind> DeniedCallers { get; } =
    [
        CallerKind.Anonymous,
        CallerKind.AlgNoneToken,
        CallerKind.UnknownKeyToken,
        CallerKind.OtherTenantUser,
        CallerKind.Guest,
        CallerKind.AuthenticatedWithoutRole,
        CallerKind.LockedUser,
        CallerKind.LockedAdmin,
        CallerKind.LockedRecipient,
    ];

    public static IReadOnlyList<CallerKind> ShapeCallers { get; } =
    [
        CallerKind.User,
        CallerKind.ShareViewer,
        CallerKind.ShareEditor,
        CallerKind.ShareManager,
        CallerKind.FolderViewer,
        CallerKind.FolderManager,
        CallerKind.OtherUser,
        CallerKind.Admin,
    ];

    public static IReadOnlyList<SharingMatrixEndpoint> All { get; } =
    [
        new("E21", (api, s) => Response(api.ListSharedItemsAsync()), Ok, Ok, Ok, Ok, Ok, Ok, Ok, Ok),
        new("E22-S", (api, s) => Response(api.GetSharedSecretAsync(s.OwnerId, s.SecretS)), NotFound, Ok, Ok, Ok, NotFound, NotFound, NotFound, NotFound),
        new("E22-T", (api, s) => Response(api.GetSharedSecretAsync(s.OwnerId, s.SecretT)), NotFound, NotFound, NotFound, NotFound, Ok, Ok, NotFound, NotFound),
        new("E23-S", (api, s) => Response(api.UpdateSharedSecretAsync(s.OwnerId, s.SecretS, s.SecretToken, UpdateBody())), NotFound, NotFound, Ok, Ok, NotFound, NotFound, NotFound, NotFound),
        new("E23-T", (api, s) => Response(api.UpdateSharedSecretAsync(s.OwnerId, s.SecretT, s.NestedToken, UpdateBody())), NotFound, NotFound, NotFound, NotFound, NotFound, Ok, NotFound, NotFound),
        new("E24-S", (api, s) => Response(api.RevealSharedSecretAsync(s.OwnerId, s.SecretS, RevealBody())), NotFound, Ok, Ok, Ok, NotFound, NotFound, NotFound, NotFound),
        new("E24-T", (api, s) => Response(api.RevealSharedSecretAsync(s.OwnerId, s.SecretT, RevealBody())), NotFound, NotFound, NotFound, NotFound, Ok, Ok, NotFound, NotFound),
        new("E25-F", (api, s) => Response(api.GetSharedFolderAsync(s.OwnerId, s.FolderF)), NotFound, NotFound, NotFound, NotFound, Ok, Ok, NotFound, NotFound),
        new("E25-G", (api, s) => Response(api.GetSharedFolderAsync(s.OwnerId, s.FolderG)), NotFound, NotFound, NotFound, NotFound, Ok, Ok, NotFound, NotFound),
        new("E26-S", (api, s) => api.FileSharedSecretAsync(s.OwnerId, s.SecretS, new FileSharedItemRequestDto { FolderId = null }), NotFound, NoContent, NoContent, NoContent, NotFound, NotFound, NotFound, NotFound),
        new("E27-F", (api, s) => api.FileSharedFolderAsync(s.OwnerId, s.FolderF, new FileSharedItemRequestDto { FolderId = null }), NotFound, NotFound, NotFound, NotFound, NoContent, NoContent, NotFound, NotFound),
        new("E28-S", (api, s) => Response(api.ListSecretGrantsAsync(s.OwnerId, s.SecretS)), Ok, NotFound, NotFound, Ok, NotFound, NotFound, NotFound, NotFound),
        new("E28-T", (api, s) => Response(api.ListSecretGrantsAsync(s.OwnerId, s.SecretT)), Ok, NotFound, NotFound, NotFound, NotFound, Ok, NotFound, NotFound),
        new("E29-S", (api, s) => Response(api.CreateSecretGrantAsync(s.OwnerId, s.SecretS, NewGrant())), Created, NotFound, NotFound, Created, NotFound, NotFound, NotFound, NotFound),
        new("E29-T", (api, s) => Response(api.CreateSecretGrantAsync(s.OwnerId, s.SecretT, NewGrant())), Created, NotFound, NotFound, NotFound, NotFound, Created, NotFound, NotFound),
        new("E30-S-grantee", (api, s) => Response(api.ChangeSecretGrantAsync(s.OwnerId, s.SecretS, TestUsers.ManagerGrantee.ObjectId, s.ManagerGranteeGrantToken, ChangeBody())), Ok, NotFound, NotFound, Ok, NotFound, NotFound, NotFound, NotFound),
        new("E30-S-viewer", (api, s) => Response(api.ChangeSecretGrantAsync(s.OwnerId, s.SecretS, TestUsers.ShareViewer.ObjectId, s.ViewerGrantToken, ChangeBody())), Ok, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound),
        new("E31-S-grantee", (api, s) => api.RevokeSecretGrantAsync(s.OwnerId, s.SecretS, TestUsers.ManagerGrantee.ObjectId), NoContent, NotFound, NotFound, NoContent, NotFound, NotFound, NotFound, NotFound),
        new("E31-S-viewer", (api, s) => api.RevokeSecretGrantAsync(s.OwnerId, s.SecretS, TestUsers.ShareViewer.ObjectId), NoContent, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound),
        new("E32-F", (api, s) => Response(api.ListFolderGrantsAsync(s.OwnerId, s.FolderF)), Ok, NotFound, NotFound, NotFound, NotFound, Ok, NotFound, NotFound),
        new("E33-F", (api, s) => Response(api.CreateFolderGrantAsync(s.OwnerId, s.FolderF, NewGrant())), Created, NotFound, NotFound, NotFound, NotFound, Created, NotFound, NotFound),
        new("E33-G", (api, s) => Response(api.CreateFolderGrantAsync(s.OwnerId, s.FolderG, NewGrant())), Created, NotFound, NotFound, NotFound, NotFound, Created, NotFound, NotFound),
        new("E34-F", (api, s) => Response(api.ChangeFolderGrantAsync(s.OwnerId, s.FolderF, TestUsers.FolderViewer.ObjectId, s.FolderViewerGrantToken, ChangeBody())), Ok, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound),
        new("E35-F", (api, s) => api.RevokeFolderGrantAsync(s.OwnerId, s.FolderF, TestUsers.FolderViewer.ObjectId), NoContent, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound, NotFound),
        new("E36", (api, s) => Response(api.SearchDirectoryAsync("other")), Ok, Ok, Ok, Ok, Ok, Ok, Ok, Ok),
    ];

    public static IReadOnlyList<string> OwnerRouteIds { get; } =
    [
        "E28-S", "E28-T", "E29-S", "E29-T", "E30-S-grantee", "E30-S-viewer", "E31-S-grantee", "E31-S-viewer", "E32-F", "E33-F", "E33-G", "E34-F", "E35-F",
    ];

    public static SharingMatrixEndpoint Find(string id) => All.Single(endpoint => endpoint.Id == id);

    public static HttpStatusCode ExpectedDenied(CallerKind caller) => caller switch
    {
        CallerKind.Anonymous or CallerKind.AlgNoneToken or CallerKind.UnknownKeyToken or CallerKind.OtherTenantUser => HttpStatusCode.Unauthorized,
        _ => HttpStatusCode.Forbidden,
    };

    private static async Task<HttpResponseMessage> Response<T>(Task<(HttpResponseMessage Response, T? Body)> call) => (await call).Response;

    private static CreateShareGrantRequestDto NewGrant() => new() { RecipientId = TestUsers.OtherUser.ObjectId, Permission = SharePermissionDto.View };

    private static ChangeShareGrantRequestDto ChangeBody() => new() { Permission = SharePermissionDto.Edit };

    private static UpdateSecretRequestDto UpdateBody() => new() { Type = SecretTypeDto.Login, Name = RequestName };

    private static RevealSecretRequestDto RevealBody() => new() { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
}
