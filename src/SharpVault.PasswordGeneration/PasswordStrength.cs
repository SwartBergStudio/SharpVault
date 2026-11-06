// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>A coarse rating of an entropy value in bits.</summary>
public enum PasswordStrength
{
    VeryWeak = 0,
    Weak = 1,
    Reasonable = 2,
    Strong = 3,
    VeryStrong = 4,
}
