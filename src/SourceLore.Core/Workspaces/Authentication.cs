namespace SourceLore.Core.Workspaces;

// The URL is transient and must not be written to preferences, journals, or diagnostics.
public sealed record AuthenticationLink(string Url);
public interface IAuthenticationBackend
{
    Task LoginAsync(string remoteUrl, IProgress<AuthenticationLink> link);
}
