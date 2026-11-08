namespace NajmCard.Core.Models;

/// <summary>Read-only status from RouterOS 6 /tool/user-manager/database/print.</summary>
public sealed record UserManagerV6DatabaseStatus(bool Available,string DbSize,
    string FreeSpace,string Diagnostic);
