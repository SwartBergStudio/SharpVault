// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Linq.Expressions;
using FluentValidation;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretFieldRules
{
    public static void AddMetadataRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> name,
        Expression<Func<T, string?>> username,
        Expression<Func<T, string?>> url,
        Expression<Func<T, IReadOnlyList<string>?>> tags)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(tags);

        var usernameOf = username.Compile();
        var urlOf = url.Compile();
        var tagsOf = tags.Compile();

        validator.RuleFor(name)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(VaultMessages.FieldRequired)
            .SingleLineText(VaultLimits.NameMaxLength);

        validator.RuleFor(username)
            .SingleLineText(VaultLimits.UsernameMaxLength)
            .When(x => usernameOf(x) is not null);

        validator.RuleFor(url)
            .Cascade(CascadeMode.Stop)
            .Must(value => value is null || value.Length <= VaultLimits.UrlMaxLength).WithMessage(VaultMessages.FieldTooLong)
            .HttpUrl()
            .When(x => urlOf(x) is not null);

        validator.RuleFor(tags)
            .TagList()
            .When(x => tagsOf(x) is not null);
    }

    public static void AddSecretTextRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> value,
        Expression<Func<T, string?>> notes)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(notes);

        var valueOf = value.Compile();
        var notesOf = notes.Compile();

        validator.RuleFor(value)
            .MultiLineSecretText(VaultLimits.ValueMaxLength)
            .When(x => valueOf(x) is not null);

        validator.RuleFor(notes)
            .MultiLineSecretText(VaultLimits.NotesMaxLength)
            .When(x => notesOf(x) is not null);
    }
}
