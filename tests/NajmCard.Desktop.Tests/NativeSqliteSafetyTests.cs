using System;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class NativeSqliteSafetyTests
{
    [Fact]
    public void Loaded_Native_SQLite_Is_Newer_Than_Vulnerable_3_50_2_Boundary()
    {
        // Checking NuGet versions alone is not sufficient: the actual native
        // DLL loaded on Windows x86/x64 must be patched as well.
        using var db=new SqliteConnection("Data Source=:memory:;Pooling=False");
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT sqlite_version()";
        var raw=Assert.IsType<string>(cmd.ExecuteScalar());
        Assert.True(Version.TryParse(raw,out var loaded),
            "SQLite returned an unparseable runtime version: "+raw);
        Assert.True(loaded!.CompareTo(new Version(3,50,2))>=0,
            "Native SQLite "+raw+" is older than the required safe version 3.50.2.");

        cmd.CommandText="PRAGMA integrity_check";
        Assert.Equal("ok",Assert.IsType<string>(cmd.ExecuteScalar()));
    }
}
