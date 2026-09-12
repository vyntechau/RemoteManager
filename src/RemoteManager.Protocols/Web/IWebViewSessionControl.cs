namespace RemoteManager.Protocols.Web;

public interface IWebViewSessionControl
{
    Task NavigateAsync(string url);
}
