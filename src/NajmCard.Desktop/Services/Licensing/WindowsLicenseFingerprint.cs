using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NajmCard.Desktop.Services.Licensing;

/// <summary>Paid fingerprint must match Hadramout's Python machine_code()
/// exactly: Windows HKLM MachineGuid in 64-bit registry view; same digest
/// for 32-bit and 64-bit NajmCard binaries. Trial uses a DISTINCT local
/// domain separator and is never included in an outbound request.</summary>
public static class WindowsLicenseFingerprint
{
    public static string PaidFromWindowsGuid(string guid)
        => Digest("HADRAMOUT-GITHUB-LICENSE-V1|",guid);
    public static string TrialFromWindowsGuid(string guid)
        => Digest("NAJMCARD-LOCAL-TRIAL-V1|",guid);
    private static string Digest(string domain,string machineGuid)
    {
        if(string.IsNullOrWhiteSpace(machineGuid))
            throw new InvalidOperationException("تعذر الحصول على معرف Windows.");
        var identity=machineGuid.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(domain+identity)));
    }
    private static string ReadWindowsGuid()
    {
        using var root=RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,RegistryView.Registry64);
        using var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var value=key?.GetValue("MachineGuid") as string;
        if(string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                "لا يمكن قراءة MachineGuid. لا يُستخدم بديل قد يغير بصمة ترخيص حضرموت.");
        return value;
    }
    public static string Paid()=>PaidFromWindowsGuid(ReadWindowsGuid());
    public static string LocalTrial()=>TrialFromWindowsGuid(ReadWindowsGuid());
}
