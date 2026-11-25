using System.IO;
using System.Security.Cryptography;
using System.Text;
using AUTZ.IronWorks.Preview.Core;
using Microsoft.Win32;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// The Admin menu is only shown on the owner's PC. tools\make-admin-key.ps1 makes a key pair: the private half stays on the
/// owner's PC (never in the repo), the public half is written below, and a signed admin.key for this PC goes in the settings
/// folder. The app checks the signature. Anyone else's copy has no valid admin.key, so the menu never appears.
/// </summary>
internal static class AdminAccess
{
    /// <summary>Public half of the owner's key. Filled in by tools\make-admin-key.ps1. Empty = no Admin menu for anyone.</summary>
    private const string PublicKeyXml = "";

    public static string KeyFile => Path.Combine(AppInfo.DataFolder, "admin.key");

    private static bool? _isOwner;

    public static bool IsOwner => _isOwner ??= Check();

    private static bool Check()
    {
        try
        {
            if (PublicKeyXml.Length == 0 || !File.Exists(KeyFile)) return false;
            byte[] signature = Convert.FromBase64String(File.ReadAllText(KeyFile).Trim());
            using var rsa = RSA.Create();
            rsa.FromXmlString(PublicKeyXml);
            bool ok = rsa.VerifyData(Encoding.UTF8.GetBytes(Message()), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!ok) Log.Debug("APP", "admin.key found but it isn't valid for this PC");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Debug("APP", "admin.key couldn't be checked: " + ex.Message);
            return false;
        }
    }

    /// <summary>What the key signs: tied to this PC, so a copied admin.key doesn't work anywhere else.</summary>
    private static string Message() => "EVE-IW-ADMIN|" + MachineId();

    private static string MachineId()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return (key?.GetValue("MachineGuid") as string ?? "").Trim().ToLowerInvariant();
    }
}

