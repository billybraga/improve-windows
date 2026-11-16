using System.Runtime.InteropServices;
using System.Text;
using ImproveWindows.Core;

namespace ImproveWindows.Ui.Actions;

/// <summary>
/// Removes the English (United States) language, and the US keyboard layouts that something (RDP, an app) loaded into the session
/// without them being in the language settings. Those "ghost" layouts show in Win+Space but can't be removed from Settings;
/// adding them to the settings then removing them unloads them.
/// </summary>
internal static partial class EnglishUsRemoval
{
    private const int UsLayout = 0x0409;
    private const int EnglishUs = 0x0409;

    public static async Task RunAsync(Action<string> log, CancellationToken cancellationToken)
    {
        log($"Settings: {await RunPowerShellAsync(ListLanguagesScript, cancellationToken)}");
        log($"Loaded layouts: {string.Join(", ", GetLoadedLayouts().Select(x => $"{x:X8}"))}");

        log(
            await RunPowerShellAsync(
                """
                $l = Get-WinUserLanguageList
                $en = $l | Where-Object LanguageTag -eq 'en-US'
                if (-not $en) { 'English (United States) is not in the language settings' }
                elseif ($l[0].LanguageTag -eq 'en-US') { 'English (United States) is the first (display) language, kept' }
                else { [void]$l.Remove($en); Set-WinUserLanguageList $l -Force; 'Removed English (United States) from the language settings' }
                """,
                cancellationToken
            )
        );

        var ghostLanguages = GetLoadedLayouts()
            .Where(x => ((x >> 16) & 0xFFFF) == UsLayout)
            .Select(x => x & 0xFFFF)
            .Distinct()
            .ToArray();

        if (ghostLanguages.Length == 0)
        {
            log("No US layout loaded");
        }

        foreach (var language in ghostLanguages)
        {
            log(await RunPowerShellAsync(language == EnglishUs ? UnloadEnglishUsScript : GetUnloadUsLayoutScript(language), cancellationToken));
        }

        log($"Settings: {await RunPowerShellAsync(ListLanguagesScript, cancellationToken)}");
        log($"Loaded layouts: {string.Join(", ", GetLoadedLayouts().Select(x => $"{x:X8}"))}");
    }

    private const string ListLanguagesScript =
        """
        # From a variable: the cmdlet outputs its list as a single item
        $l = Get-WinUserLanguageList
        $(foreach ($language in $l) { "$($language.LanguageTag) [$($language.InputMethodTips -join ', ')]" }) -join ', '
        """;

    // The en-US layout of the en-US language, with en-US not in the settings: add and remove the whole language
    private const string UnloadEnglishUsScript =
        """
        $l = Get-WinUserLanguageList
        if ($l | Where-Object LanguageTag -eq 'en-US') { 'English (United States) is in the language settings, its US layout is kept'; return }
        $l.Add('en-US'); Set-WinUserLanguageList $l -Force
        $l = Get-WinUserLanguageList; [void]$l.Remove(($l | Where-Object LanguageTag -eq 'en-US')); Set-WinUserLanguageList $l -Force
        'Unloaded the English (United States) US layout'
        """;

    private static string GetUnloadUsLayoutScript(int language)
    {
        return $$"""
            $tip = '{{language:X4}}:{{UsLayout:X8}}'
            function Find-Language($list) { $list | Where-Object { $_.InputMethodTips | Where-Object { $_ -like '{{language:X4}}:*' } } | Select-Object -First 1 }
            $l = Get-WinUserLanguageList
            $lang = Find-Language $l
            if (-not $lang) { "No language with {{language:X4}} layouts in the settings, can't unload $tip"; return }
            if ($lang.InputMethodTips -contains $tip) { "$tip is in the $($lang.LanguageTag) settings, kept"; return }
            $lang.InputMethodTips.Add($tip); Set-WinUserLanguageList $l -Force
            $l = Get-WinUserLanguageList; $lang = Find-Language $l; [void]$lang.InputMethodTips.Remove($tip); Set-WinUserLanguageList $l -Force
            "Unloaded the US layout of $($lang.LanguageTag) ($tip)"
            """;
    }

    // Not from CsWin32: it only generates the pointer overload, and this project doesn't allow unsafe code
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetKeyboardLayoutList(int bufferSize, [Out] IntPtr[]? layouts);

    private static int[] GetLoadedLayouts()
    {
        var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
        var count = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(count).Select(x => unchecked((int) x.ToInt64())).ToArray();
    }

    private static async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        // Encoded so the multi-line script survives the command line
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'\n" + script));
        var output = await IoUtils
            .CreateCommand("powershell", ["-NoProfile", "-NonInteractive", "-EncodedCommand", encodedScript])
            .ExecuteValidateAndGetOutputAsync(cancellationToken);
        return output.Trim();
    }
}
