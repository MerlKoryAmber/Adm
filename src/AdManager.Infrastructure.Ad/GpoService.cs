using System.DirectoryServices;
using System.Text;
using System.Text.RegularExpressions;
using AdManager.Application;
using AdManager.Application.Abstractions;
using AdManager.Domain;

namespace AdManager.Infrastructure.Ad;

/// <summary>Управление линками GPO: правит атрибут gPLink на scope (OU/домен) через LDAP.</summary>
public sealed partial class GpoService : IGpoService
{
    private readonly AdConnectionOptions _opt;
    private readonly IOperationalCredentialProvider _cred;

    public GpoService(AdConnectionOptions opt, IOperationalCredentialProvider cred)
    {
        _opt = opt;
        _cred = cred;
    }

    private DirectoryEntry Bind(string dn) => Ldap.Bind(_opt, _cred, dn);

    private string BaseDn()
    {
        if (!string.IsNullOrWhiteSpace(_opt.BaseDn)) return _opt.BaseDn!;
        using var root = Bind("RootDSE");
        return root.Properties["defaultNamingContext"][0]?.ToString() ?? "";
    }

    public Task<OperationResult> LinkAsync(string scopeDn, string gpoId, CancellationToken ct = default)
        => Modify(scopeDn, entries =>
        {
            if (entries.Any(e => IdOf(e.Path).Equals(gpoId, StringComparison.OrdinalIgnoreCase)))
                return false; // уже привязан — no-op
            var path = $"cn={gpoId},cn=policies,cn=system,{BaseDn()}";
            entries.Add((path, 0));
            return true;
        }, ct);

    public Task<OperationResult> UnlinkAsync(string scopeDn, string gpoId, CancellationToken ct = default)
        => Modify(scopeDn, entries => entries.RemoveAll(e => IdOf(e.Path).Equals(gpoId, StringComparison.OrdinalIgnoreCase)) > 0, ct);

    public Task<OperationResult> SetLinkOptionsAsync(string scopeDn, string gpoId, bool enforced, bool enabled, CancellationToken ct = default)
        => Modify(scopeDn, entries =>
        {
            var idx = entries.FindIndex(e => IdOf(e.Path).Equals(gpoId, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            var opt = (enforced ? 2 : 0) | (enabled ? 0 : 1);
            entries[idx] = (entries[idx].Path, opt);
            return true;
        }, ct);

    private Task<OperationResult> Modify(string scopeDn, Func<List<(string Path, int Opt)>, bool> mutate, CancellationToken ct)
        => Task.Run(() =>
        {
            try
            {
                using var de = Bind(scopeDn);
                de.RefreshCache();
                var current = de.Properties["gPLink"].Count > 0 ? de.Properties["gPLink"][0]?.ToString() : null;
                var entries = Parse(current);
                var changed = mutate(entries);
                if (!changed) return new OperationResult(true, "no change");

                if (entries.Count == 0) de.Properties["gPLink"].Clear();
                else
                {
                    var sb = new StringBuilder();
                    foreach (var (path, opt) in entries) sb.Append($"[LDAP://{path};{opt}]");
                    de.Properties["gPLink"].Value = sb.ToString();
                }
                de.CommitChanges();
                return new OperationResult(true, "gPLink updated");
            }
            catch (Exception ex)
            {
                return new OperationResult(false, ex.Message);
            }
        }, ct);

    private static List<(string Path, int Opt)> Parse(string? gplink)
    {
        var list = new List<(string, int)>();
        if (string.IsNullOrWhiteSpace(gplink)) return list;
        foreach (Match m in GpLinkRegex().Matches(gplink))
            list.Add((m.Groups["path"].Value, int.TryParse(m.Groups["opt"].Value, out var o) ? o : 0));
        return list;
    }

    private static string IdOf(string path)
    {
        var m = Regex.Match(path, @"cn=(\{[0-9A-Fa-f\-]+\})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : path;
    }

    [GeneratedRegex(@"\[LDAP://(?<path>[^;\]]+);(?<opt>\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex GpLinkRegex();
}
