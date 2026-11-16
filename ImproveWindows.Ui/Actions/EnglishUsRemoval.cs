using System.Runtime.InteropServices;
using System.Text;
using ImproveWindows.Core;
using Microsoft.Win32;

namespace ImproveWindows.Ui.Actions;

/// <summary>
/// Removes the English (United States) language, and the US keyboard layouts that something (RDP, an app) loaded into the session
/// without them being in the language settings. Those "ghost" layouts show in Win+Space but can't be removed from Settings.
/// </summary>
internal static partial class EnglishUsRemoval
{
    private const int UsLayout = 0x0409;
    private const string EnglishUs = "en-US";
    private const string UserProfileKey = @"Control Panel\International\User Profile";
    private const uint InstallLayoutOrTipUninstall = 0x1;

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetKeyboardLayoutList(int bufferSize, [Out] IntPtr[]? layouts);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnloadKeyboardLayout(IntPtr layout);

    // Same as adding or removing a keyboard in Settings
    [LibraryImport("input.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InstallLayoutOrTip(string profile, uint flags);

    public static async Task RunAsync(Action<string> log, CancellationToken cancellationToken)
    {
        LogState(log);

        var languages = ReadLanguageSettings();
        if (!languages.Any(x => x.Tag.Equals(EnglishUs, StringComparison.OrdinalIgnoreCase)))
        {
            log("English (United States) is not in the language settings");
        }
        else if (languages[0].Tag.Equals(EnglishUs, StringComparison.OrdinalIgnoreCase))
        {
            log("English (United States) is the first (display) language, kept");
        }
        else
        {
            // The only step left in PowerShell: Windows has no native API a desktop app may use to change the language list
            // (GlobalizationPreferences.TrySetLanguages is refused with access denied)
            await RunPowerShellAsync(
                """
                $l = Get-WinUserLanguageList
                [void]$l.Remove(($l | Where-Object LanguageTag -eq 'en-US'))
                Set-WinUserLanguageList $l -Force
                """,
                cancellationToken
            );
            log("Removed English (United States) from the language settings");
        }

        var savedTips = ReadLanguageSettings().SelectMany(x => x.Tips).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ghosts = GetLoadedLayouts()
            .Where(x => GetLayoutId(x) == UsLayout && !savedTips.Contains(GetTip(x)))
            .ToArray();

        if (ghosts.Length == 0)
        {
            log("No US layout loaded outside the language settings");
        }

        foreach (var ghost in ghosts)
        {
            var tip = GetTip(ghost);
            if (UnloadKeyboardLayout(ghost) && !IsLoaded(ghost))
            {
                log($"Unloaded the {tip} layout");
                continue;
            }

            // Adding then removing the layout in the settings unloads it too
            _ = InstallLayoutOrTip(tip, 0);
            _ = InstallLayoutOrTip(tip, InstallLayoutOrTipUninstall);
            log(IsLoaded(ghost) ? $"Couldn't unload the {tip} layout" : $"Unloaded the {tip} layout by adding and removing it in the settings");
        }

        LogState(log);
    }

    private static void LogState(Action<string> log)
    {
        log($"Settings: {string.Join(", ", ReadLanguageSettings().Select(x => $"{x.Tag} [{string.Join(", ", x.Tips)}]"))}");
        log($"Loaded layouts: {string.Join(", ", GetLoadedLayouts().Select(x => $"{(uint) x.ToInt64():X8}"))}");
    }

    /// <summary>
    /// The languages in Settings order with their keyboards, as "language:layout" input method tips (e.g. 1009:00011009).
    /// </summary>
    private static List<(string Tag, string[] Tips)> ReadLanguageSettings()
    {
        using var userProfile = Registry.CurrentUser.OpenSubKey(UserProfileKey);
        var tags = userProfile?.GetValue("Languages") as string[] ?? [];

        return tags
            .Select(tag =>
                {
                    using var language = userProfile!.OpenSubKey(tag);
                    var tips = language?.GetValueNames().Where(x => x.Contains(':', StringComparison.Ordinal)).ToArray() ?? [];
                    return (tag, tips);
                }
            )
            .ToList();
    }

    private static IntPtr[] GetLoadedLayouts()
    {
        var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
        var count = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(count).ToArray();
    }

    private static bool IsLoaded(IntPtr layout)
    {
        return GetLoadedLayouts().Contains(layout);
    }

    // A layout handle is the language in the low word and the layout in the high word, e.g. 04091009 is US for en-CA
    private static int GetLayoutId(IntPtr layout)
    {
        return (int) ((layout.ToInt64() >> 16) & 0xFFFF);
    }

    private static string GetTip(IntPtr layout)
    {
        return $"{layout.ToInt64() & 0xFFFF:X4}:{GetLayoutId(layout):X8}";
    }

    private static async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        // Encoded so the multi-line script survives the command line
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'\n" + script));
        await IoUtils
            .CreateCommand("powershell", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript])
            .ExecuteAndValidateAsync(cancellationToken);
    }
}
