// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;

namespace SharpVault.Application.Common.Models;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Result<T>.Success and Result<T>.Failure are the project-wide result factory API.")]
public sealed class Result<T>
{
    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
        Errors = [];
    }

    private Result(IReadOnlyList<string> errors)
    {
        IsSuccess = false;
        Errors = errors;
        Error = errors.Count > 0 ? errors[0] : null;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Error { get; }

    public IReadOnlyList<string> Errors { get; }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(string error) => new([error]);

    public static Result<T> Failure(IReadOnlyList<string> errors) => new(errors);
}
