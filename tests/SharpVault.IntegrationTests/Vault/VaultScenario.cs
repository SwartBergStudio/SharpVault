// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;

namespace SharpVault.IntegrationTests.Vault;

public sealed record VaultScenario(
    PlaintextMarkers Markers,
    Guid FolderF,
    Guid FolderG,
    Guid SecretS,
    Guid VersionV,
    Guid BinnedSecretB,
    Guid BinnedFolderC,
    string SecretToken)
{
    public const string RequestName = "scenario-request-name";

    public IReadOnlyList<Guid> Ids => [FolderF, FolderG, SecretS, VersionV, BinnedSecretB, BinnedFolderC];

    public static async Task<VaultScenario> ArrangeAsync(VaultTestApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var markers = new PlaintextMarkers();
        var folderF = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-F" }));
        var folderG = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-G", ParentId = folderF.Id }));
        var secret = Require(await api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            Value = markers.Value,
            Notes = markers.Notes,
            FolderId = folderF.Id,
        }));
        Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, new UpdateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            ReplaceValue = true,
            Value = markers.Value + "-v2",
        }));
        var history = Require(await api.ListHistoryAsync(secret.Id));
        var version = history.Items[0];
        var current = Require(await api.GetSecretAsync(secret.Id));
        var binned = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name + "-binned", Value = "binned" }));
        EnsureStatus(await api.DeleteSecretAsync(binned.Id, binned.ConcurrencyToken), HttpStatusCode.NoContent);
        var binnedFolder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-C" }));
        EnsureStatus(await api.DeleteFolderAsync(binnedFolder.Id, FolderDeleteModeDto.DeleteContents), HttpStatusCode.NoContent);

        return new VaultScenario(markers, folderF.Id, folderG.Id, secret.Id, version.VersionId, binned.Id, binnedFolder.Id, current.ConcurrencyToken);
    }

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        if (!result.Response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Arrangement failed with {result.Response.StatusCode}.");
        }

        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private static void EnsureStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException($"Arrangement failed with {response.StatusCode}, expected {expected}.");
        }
    }
}

public static class VaultEndpointCalls
{
    public static IReadOnlyList<string> AllIds { get; } =
    [
        "E1", "E2", "E3", "E3-folder", "E4", "E5", "E6", "E7", "E8", "E9", "E10",
        "E11", "E12", "E12-parent", "E13", "E14", "E15-move", "E15-delete",
        "E16", "E17", "E18", "E19", "E20",
    ];

    public static IReadOnlyList<string> SecretItemIds { get; } = ["E2", "E4", "E5", "E6", "E7", "E8", "E9", "E10"];

    public static IReadOnlyList<string> FolderItemIds { get; } = ["E13", "E14", "E15-move", "E15-delete"];

    public static IReadOnlyList<string> NonOwnerNotFoundIds { get; } =
    [
        "E2", "E3-folder", "E4", "E5", "E6", "E7", "E8", "E9", "E10",
        "E12-parent", "E13", "E14", "E15-move", "E15-delete", "E17", "E18", "E19", "E20",
    ];

    public static Task<HttpResponseMessage> SendAsync(string id, VaultTestApi api, VaultScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(scenario);

        return id switch
        {
            "E1" => Response(api.ListSecretsAsync()),
            "E2" => Response(api.GetSecretAsync(scenario.SecretS)),
            "E3" => Response(api.CreateSecretAsync(NewSecret(null))),
            "E3-folder" => Response(api.CreateSecretAsync(NewSecret(scenario.FolderF))),
            "E4" => Response(api.UpdateSecretAsync(scenario.SecretS, scenario.SecretToken, UpdateBody())),
            "E5" => Response(api.MoveSecretAsync(scenario.SecretS, scenario.SecretToken, new MoveSecretRequestDto { FolderId = null })),
            "E6" => api.DeleteSecretAsync(scenario.SecretS, scenario.SecretToken),
            "E7" => Response(api.RevealSecretAsync(scenario.SecretS, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })),
            "E8" => Response(api.ListHistoryAsync(scenario.SecretS)),
            "E9" => Response(api.RevealVersionAsync(scenario.SecretS, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View })),
            "E10" => Response(api.RestoreVersionAsync(scenario.SecretS, scenario.VersionV, scenario.SecretToken)),
            "E11" => Response(api.ListFoldersAsync()),
            "E12" => Response(api.CreateFolderAsync(new CreateFolderRequestDto { Name = VaultScenario.RequestName })),
            "E12-parent" => Response(api.CreateFolderAsync(new CreateFolderRequestDto { Name = VaultScenario.RequestName, ParentId = scenario.FolderF })),
            "E13" => Response(api.RenameFolderAsync(scenario.FolderF, new RenameFolderRequestDto { Name = VaultScenario.RequestName })),
            "E14" => Response(api.MoveFolderAsync(scenario.FolderG, new MoveFolderRequestDto { ParentId = null })),
            "E15-move" => api.DeleteFolderAsync(scenario.FolderG, FolderDeleteModeDto.MoveToParent),
            "E15-delete" => api.DeleteFolderAsync(scenario.FolderG, FolderDeleteModeDto.DeleteContents),
            "E16" => Response(api.ListRecycleBinAsync()),
            "E17" => Response(api.RestoreRecycledSecretAsync(scenario.BinnedSecretB)),
            "E18" => Response(api.RestoreRecycledFolderAsync(scenario.BinnedFolderC)),
            "E19" => api.PurgeRecycledSecretAsync(scenario.BinnedSecretB),
            "E20" => api.PurgeRecycledFolderAsync(scenario.BinnedFolderC),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
        };
    }

    private static async Task<HttpResponseMessage> Response<T>(Task<(HttpResponseMessage Response, T? Body)> call) => (await call).Response;

    private static CreateSecretRequestDto NewSecret(Guid? folderId) => new()
    {
        Type = SecretTypeDto.Login,
        Name = VaultScenario.RequestName,
        Value = "scenario-request-value",
        FolderId = folderId,
    };

    private static UpdateSecretRequestDto UpdateBody() => new() { Type = SecretTypeDto.Login, Name = VaultScenario.RequestName };
}
