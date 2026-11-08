using System.Text.RegularExpressions;

namespace NajmCard.Core.Services;

/// <summary>
/// Fail-closed RouterOS command boundary, ported from Hadramout core/mikrotik.py.
/// Every real RouterOsClient command passes this guard before any TCP I/O.
/// This is not a general-purpose WinBox terminal and must not mutate other
/// network configuration menus (firewall/IP/routes/scripts/DHCP etc.).
/// </summary>
public static partial class RouterOsCommandPolicy
{
    private static readonly HashSet<string> WriteAllowlist = new(StringComparer.Ordinal)
    {
        "/ip/hotspot/user/add", "/ip/hotspot/user/set", "/ip/hotspot/user/remove",
        "/ip/hotspot/active/remove", "/ip/hotspot/set",
        "/ip/hotspot/user/profile/add", "/ip/hotspot/user/profile/set",
        "/ip/hotspot/user/profile/remove",
        "/ip/hotspot/ip-binding/add", "/ip/hotspot/ip-binding/remove",
        "/ppp/secret/add", "/ppp/secret/set", "/ppp/secret/remove",
        "/ppp/active/remove",
        "/ppp/profile/add", "/ppp/profile/set", "/ppp/profile/remove",
        "/user-manager/user/add", "/user-manager/user/set", "/user-manager/user/remove",
        "/user-manager/user-profile/add",
        "/user-manager/profile/add", "/user-manager/profile/set", "/user-manager/profile/remove",
        "/user-manager/limitation/add", "/user-manager/limitation/set", "/user-manager/limitation/remove",
        "/user-manager/profile-limitation/add", "/user-manager/profile-limitation/remove",
        "/tool/user-manager/user/add", "/tool/user-manager/user/set", "/tool/user-manager/user/remove",
        "/tool/user-manager/user/create-and-activate-profile",
        "/tool/user-manager/profile/add", "/tool/user-manager/profile/set",
        "/tool/user-manager/profile/remove",
        "/tool/user-manager/profile/limitation/add",
        "/tool/user-manager/profile/limitation/set",
        "/tool/user-manager/profile/limitation/remove",
        "/tool/user-manager/profile/profile-limitation/add",
        "/tool/user-manager/profile/profile-limitation/remove"
    };
    private static readonly HashSet<string> MaintenanceCommands = new(StringComparer.Ordinal)
    {
        "/system/reboot", "/user-manager/database/optimize-db"
    };
    private static readonly HashSet<string> ScriptAttributes =
        new(StringComparer.OrdinalIgnoreCase) { "on-login", "on-logout",
            "on-up", "on-down", "source" };

    [GeneratedRegex(@"^(?:/[a-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PathSyntax();

    [GeneratedRegex(@"^\*[A-Fa-f0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SingleInternalId();

    public static void Validate(string command,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(command) || !PathSyntax().IsMatch(command))
            throw new ArgumentException("مسار أمر RouterOS غير صالح.", nameof(command));

        var attrs = attributes ?? new Dictionary<string,string>();
        if (attrs.Any(a => string.IsNullOrWhiteSpace(a.Key)
            || a.Key.Contains('=') || a.Key.StartsWith('=') || a.Value is null))
            throw new ArgumentException("معاملات أمر RouterOS غير صالحة.", nameof(attributes));

        if (MaintenanceCommands.Contains(command))
        {
            if (attrs.Count > 0)
                throw new ArgumentException("أوامر الصيانة لا تقبل معاملات.", nameof(attributes));
            return;
        }

        var action = command[(command.LastIndexOf('/') + 1)..];
        if (action == "print")
        {
            if (attrs.Keys.Any(key => key is not (".proplist" or "count-only")))
                throw new ArgumentException("قراءة RouterOS لا تسمح بكتابة ملفات أو متابعة مستمرة.", nameof(attributes));
            return;
        }

        if (!WriteAllowlist.Contains(command))
            throw new ArgumentException("أمر التعديل غير مصرح به في نجم كارد.", nameof(command));
        if (attrs.Keys.Any(ScriptAttributes.Contains))
            throw new ArgumentException("منع إدخال سكربتات إلى إعدادات MikroTik.", nameof(attributes));
        if (action is "set" or "remove")
        {
            if (!attrs.TryGetValue(".id", out string? id) || !SingleInternalId().IsMatch(id))
                throw new ArgumentException("التعديل يتطلب معرف سجل واحد من الراوتر.", nameof(attributes));
        }

        if (command == "/ip/hotspot/set" &&
            (attrs.Count != 2 || !attrs.TryGetValue("disabled", out var disabled)
                || disabled is not ("yes" or "no")))
            throw new ArgumentException("تغيير سيرفر Hotspot محصور في تفعيل/تعطيل سجل واحد.", nameof(attributes));

        if (command == "/tool/user-manager/user/create-and-activate-profile")
        {
            if (!attrs.TryGetValue("numbers", out string? id) ||
                !SingleInternalId().IsMatch(id))
                throw new ArgumentException(
                    "ربط باقة User Manager يتطلب معرف حساب واحد وليس اسماً أو قائمة.",
                    nameof(attributes));
        }
    }
}
