using System.Security.Cryptography;

namespace Warden.Agent.Auth;

/// <summary>Token bearer da API — arquivo local, permissão 600 (mesma disciplina do `auth.py` do engine Python).</summary>
public static class TokenStore
{
    public static string LoadOrCreate(string path)
    {
        if (File.Exists(path)) return File.ReadAllText(path).Trim();

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, token);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return token;
    }
}
