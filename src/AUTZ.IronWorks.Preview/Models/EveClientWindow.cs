// EVE-IW-Preview
// Copyright (C) 2026 Mune-Su
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software, licensed under the GNU GPL version 3. It comes with NO WARRANTY. See the LICENSE file.
namespace AUTZ.IronWorks.Preview.Models;

/// <param name="Started">When this client was opened (process start time), used to keep clients in opening order.</param>
public sealed record EveClientWindow(nint Handle, string Title, string CharacterName, DateTime Started, string AccountSignature)
{
    /// <summary>A client that is open but not logged in yet (its window is just titled "EVE").</summary>
    public bool IsLoginScreen => Title.Equals("EVE", StringComparison.OrdinalIgnoreCase);
}
