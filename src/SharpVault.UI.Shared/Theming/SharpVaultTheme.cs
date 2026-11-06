// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using MudBlazor;

namespace SharpVault.UI.Shared.Theming;

public static class SharpVaultTheme
{
    public const string MonoFontFamily = "JetBrains Mono";

    public static MudTheme Instance { get; } = Build();

    private static MudTheme Build() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#D9AE55",
            PrimaryContrastText = "#17110A",
            Secondary = "#9AA7B6",
            SecondaryContrastText = "#0A1017",
            Tertiary = "#8DBDF0",
            TertiaryContrastText = "#0A1017",
            Info = "#8DBDF0",
            InfoContrastText = "#0A1017",
            Success = "#5BD69A",
            SuccessContrastText = "#0A1017",
            Warning = "#FF9D57",
            WarningContrastText = "#17110A",
            Error = "#E5484D",
            ErrorContrastText = "#1A0507",
            Background = "#0A1017",
            BackgroundGray = "#070B10",
            Surface = "#111923",
            AppbarBackground = "#111923",
            AppbarText = "#E7EBF0",
            DrawerBackground = "#111923",
            DrawerText = "#E7EBF0",
            DrawerIcon = "#9AA7B6",
            TextPrimary = "#E7EBF0",
            TextSecondary = "#9AA7B6",
            ActionDefault = "#9AA7B6",
            Divider = "#233140",
            DividerLight = "#233140",
            TableLines = "#233140",
            LinesDefault = "#6B7E93",
            LinesInputs = "#6B7E93",
        },
        PaletteLight = new PaletteLight
        {
            Primary = "#7D5A0F",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#4B5869",
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#1F5A94",
            TertiaryContrastText = "#FFFFFF",
            Info = "#1F5A94",
            InfoContrastText = "#FFFFFF",
            Success = "#146C43",
            SuccessContrastText = "#FFFFFF",
            Warning = "#9A4300",
            WarningContrastText = "#FFFFFF",
            Error = "#B3242B",
            ErrorContrastText = "#FFFFFF",
            Background = "#ECEFF3",
            BackgroundGray = "#E2E7ED",
            Surface = "#F8FAFC",
            AppbarBackground = "#F8FAFC",
            AppbarText = "#0E1620",
            DrawerBackground = "#F8FAFC",
            DrawerText = "#0E1620",
            DrawerIcon = "#4B5869",
            TextPrimary = "#0E1620",
            TextSecondary = "#4B5869",
            ActionDefault = "#4B5869",
            Divider = "#CBD3DC",
            DividerLight = "#CBD3DC",
            TableLines = "#CBD3DC",
            LinesDefault = "#6A7A8D",
            LinesInputs = "#6A7A8D",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Archivo", "ui-sans-serif", "system-ui", "sans-serif"],
            },
            Button = new ButtonTypography
            {
                TextTransform = "none",
                FontWeight = "600",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "15rem",
            AppbarHeight = "3.5rem",
        },
    };
}
