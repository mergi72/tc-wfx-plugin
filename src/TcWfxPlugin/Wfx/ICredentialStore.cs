namespace TcWfxPlugin.Wfx;

public interface ICredentialStore
{
    bool TryRead(string target, out string username, out string password);
    void Save(string target, string username, string password);
}