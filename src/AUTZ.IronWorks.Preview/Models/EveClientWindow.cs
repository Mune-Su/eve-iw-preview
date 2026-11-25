namespace AUTZ.IronWorks.Preview.Models;

/// <param name="Started">When this client was opened (process start time), used to keep clients in opening order.</param>
public sealed record EveClientWindow(nint Handle, string Title, string CharacterName, DateTime Started, string AccountSignature)
{
    /// <summary>A client that is open but not logged in yet (its window is just titled "EVE").</summary>
    public bool IsLoginScreen => Title.Equals("EVE", StringComparison.OrdinalIgnoreCase);
}



