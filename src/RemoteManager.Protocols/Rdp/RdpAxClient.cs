using System.Runtime.InteropServices;
using System.Windows.Forms;
using RemoteManager.Core.Logging;

namespace RemoteManager.Protocols.Rdp;

[ComImport]
[Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
[TypeLibType(TypeLibTypeFlags.FHidden)]
public interface IMsTscAxEvents
{
    [DispId(1)] void OnConnecting();
    [DispId(2)] void OnConnected();
    [DispId(3)] void OnLoginComplete();
    [DispId(4)] void OnDisconnected(int discReason);
    [DispId(5)] void OnEnterFullScreenMode();
    [DispId(6)] void OnLeaveFullScreenMode();
    [DispId(7)] void OnChannelReceivedData(string chanName, string data);
    [DispId(8)] void OnRequestGoFullScreen();
    [DispId(9)] void OnRequestLeaveFullScreen();
    [DispId(10)] void OnFatalError(int errorCode);
    [DispId(11)] void OnWarning(int warningCode);
    [DispId(12)] void OnRemoteDesktopSizeChange(int width, int height);
    [DispId(13)] void OnIdleTimeoutNotification();
    [DispId(14)] void OnRequestContainerMinimize();
    [DispId(15)] void OnConfirmClose(out bool pfAllowClose);
    [DispId(16)] void OnReceivedTSPublicKey(string publicKey, out bool pfContinueLogon);
    [DispId(17)] void OnAutoReconnecting(int disconnectReason, int attemptCount, out int pContinueStatus);
    [DispId(18)] void OnAuthenticationWarningDisplayed();
    [DispId(19)] void OnAuthenticationWarningDismissed();
    [DispId(20)] void OnRemoteProgramResult(string bstrRemoteProgram, int lError, bool vbIsExecutable);
    [DispId(21)] void OnRemoteProgramDisplayed(bool vbDisplayed, uint uDisplayInformation);
    [DispId(29)] void OnRemoteWindowDisplayed(bool vbDisplayed, IntPtr hwnd, int windowState);
    [DispId(22)] void OnLogonError(int lError);
    [DispId(23)] void OnFocusReleased(int iDirection);
    [DispId(24)] void OnUserNameAcquired(string bstrUserName);
    [DispId(26)] void OnMouseInputModeChanged(bool fMouseModeRelative);
    [DispId(28)] void OnServiceMessageReceived(string serviceMessage);
    [DispId(30)] void OnConnectionBarPullDown();
    [DispId(32)] void OnNetworkStatusChanged(uint qualityLevel, int bandWidth, int rtt);
    [DispId(35)] void OnDevicesButtonPressed();
    [DispId(33)] void OnAutoReconnected();
    [DispId(34)] void OnAutoReconnecting2(int disconnectReason, bool networkAvailable, int attemptCount, int maxAttemptCount);
}

public class RdpAxClient : AxHost
{
    private AxHost.ConnectionPointCookie? _cookie;
    private RdpAxEventSink? _eventSink;

    public event Action? Connected;
    public event Action<int, int, string>? Disconnected; // discReason, extendedReason, description
    public event Action<int>? FatalError;
    public event Action<int>? Warning;
    public event Action<int>? LogonError;

    // CLSID for MsTscAx / MsRdpClient
    public RdpAxClient() : base("8b918b82-7985-4c24-89df-c33ad2bbfbcd")
    {
        Dock = DockStyle.Fill;
    }

    protected override void CreateSink()
    {
        base.CreateSink();
        try
        {
            _eventSink = new RdpAxEventSink(this);
            _cookie = new AxHost.ConnectionPointCookie(GetOcx(), _eventSink, typeof(IMsTscAxEvents));
            LogEngine.Instance.Debug("Protocol.RDP", "Successfully attached COM connection point for IMsTscAxEvents.");
        }
        catch (Exception ex)
        {
            LogEngine.Instance.Warn("Protocol.RDP", "Failed to attach IMsTscAxEvents connection point", ex);
        }
    }

    protected override void DetachSink()
    {
        if (_cookie != null)
        {
            try
            {
                _cookie.Disconnect();
            }
            catch { }
            _cookie = null;
        }
        _eventSink = null;
        base.DetachSink();
    }

    public void ConnectServer(
        string server,
        int port,
        string? username,
        string? domain,
        string? password,
        int width,
        int height,
        int authLevel = 2)
    {
        dynamic? ocx = GetOcx();
        if (ocx == null)
            throw new InvalidOperationException("ActiveX RDP control is not initialized.");

        ocx.Server = server;

        if (port > 0 && port != 3389)
        {
            try { ocx.AdvancedSettings2.RDPPort = port; } catch { }
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            ocx.UserName = username;
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            ocx.Domain = domain;
        }

        if (!string.IsNullOrEmpty(password))
        {
            SetPassword(ocx, password);
        }

        ConfigureSecurityAndDisplay(ocx, width, height, authLevel);

        ocx.Connect();
    }

    public void DisconnectServer()
    {
        try
        {
            dynamic? ocx = GetOcx();
            if (ocx is not null)
            {
                int connected = 0;
                try { connected = (int)ocx.Connected; } catch { }
                if (connected != 0)
                {
                    ocx.Disconnect();
                }
            }
        }
        catch
        {
            // Ignore disconnect errors during teardown
        }
    }

    public bool IsConnected
    {
        get
        {
            try
            {
                dynamic? ocx = GetOcx();
                if (ocx is null) return false;
                int connected = 0;
                try { connected = (int)ocx.Connected; } catch { }
                return connected != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void SetPassword(dynamic ocx, string password)
    {
        try { ocx.AdvancedSettings9.ClearTextPassword = password; return; } catch { }
        try { ocx.AdvancedSettings8.ClearTextPassword = password; return; } catch { }
        try { ocx.AdvancedSettings7.ClearTextPassword = password; return; } catch { }
        try { ocx.AdvancedSettings2.ClearTextPassword = password; return; } catch { }
        try { ocx.AdvancedSettings.ClearTextPassword = password; return; } catch { }
    }

    private static void ConfigureSecurityAndDisplay(dynamic ocx, int width, int height, int authLevel = 2)
    {
        try
        {
            // Configure modern security and negotiate
            dynamic adv = ocx.AdvancedSettings9 ?? ocx.AdvancedSettings7 ?? ocx.AdvancedSettings2 ?? ocx.AdvancedSettings;
            adv.EnableCredSspSupport = true;
            adv.NegotiateSecurityLayer = true;
            // AuthenticationLevel: 0 = No auth, 1 = Warn on cert error, 2 = Require authentication
            adv.AuthenticationLevel = authLevel;
            adv.SmartSizing = true; // Auto-fit to window/tab size
        }
        catch
        {
            // Fallbacks for older client settings
        }

        try
        {
            ocx.DesktopWidth = width > 0 ? width : 1920;
            ocx.DesktopHeight = height > 0 ? height : 1080;
            ocx.ColorDepth = 32;
        }
        catch
        {
        }
    }

    internal void RaiseConnected()
    {
        LogEngine.Instance.Info("Protocol.RDP", "RDP ActiveX fired OnConnected.");
        Connected?.Invoke();
    }

    internal void RaiseDisconnected(int discReason)
    {
        int extendedReason = 0;
        string description = string.Empty;

        try
        {
            dynamic? ocx = GetOcx();
            if (ocx != null)
            {
                try { extendedReason = (int)ocx.ExtendedDisconnectReason; } catch { }
                try
                {
                    string rawDesc = (string)ocx.GetErrorDescription((uint)discReason, (uint)extendedReason);
                    if (!string.IsNullOrWhiteSpace(rawDesc))
                    {
                        description = rawDesc.Replace("\r\n\r\n", " ").Replace("\r\n", " ").Trim();
                    }
                }
                catch { }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = FormatDisconnectReason(discReason, extendedReason);
        }

        LogEngine.Instance.Warn("Protocol.RDP", $"RDP ActiveX fired OnDisconnected (discReason={discReason}, extReason={extendedReason}): {description}");
        Disconnected?.Invoke(discReason, extendedReason, description);
    }

    internal void RaiseFatalError(int errorCode)
    {
        LogEngine.Instance.Error("Protocol.RDP", $"RDP ActiveX fired OnFatalError: {errorCode}");
        FatalError?.Invoke(errorCode);
    }

    internal void RaiseWarning(int warningCode)
    {
        LogEngine.Instance.Warn("Protocol.RDP", $"RDP ActiveX fired OnWarning: {warningCode}");
        Warning?.Invoke(warningCode);
    }

    internal void RaiseLogonError(int lError)
    {
        LogEngine.Instance.Error("Protocol.RDP", $"RDP ActiveX fired OnLogonError: {lError}");
        LogonError?.Invoke(lError);
    }

    public static string FormatDisconnectReason(int discReason, int extendedReason)
    {
        // Check known ExtendedDisconnectReason codes
        switch (extendedReason)
        {
            case 5: // exDiscReasonReplacedByOtherConnection
                return "You have been disconnected because another user connected to the remote computer.";
            case 2: // exDiscReasonLogoffByUser
                return "The session ended because you or an administrator logged off from the remote computer.";
            case 3: // exDiscReasonServerDeniedConnection
                return "The remote server denied the connection.";
            case 4: // exDiscReasonServerDeniedConnectionFips
                return "The remote server denied the connection due to encryption policy.";
            case 6: // exDiscReasonServerOutOfMemory
                return "The connection was terminated because the remote server is low on memory.";
            case 260: // exDiscReasonLicenseInternal
                return "The remote session was disconnected due to a client license configuration issue.";
            case 264:
                return "The remote session was disconnected due to an encryption/license protocol mismatch.";
        }

        // Check basic DisconnectReason codes
        switch (discReason)
        {
            case 1: // local disconnect
                return "The remote session was disconnected by the local user.";
            case 2: // remote by user
                return "The remote session was disconnected by the remote user.";
            case 3: // remote by server
                return "The connection was terminated by the remote server.";
            case 260: // dns lookup failed
                return "Could not resolve the remote computer's host name.";
            case 264: // timeout
                return "The connection to the remote computer timed out.";
            case 516: // host not found
                return "The remote computer could not be found or reached.";
            case 2308: // socket closed
                return "The network connection to the remote computer was lost.";
            default:
                return $"Disconnected from remote computer (Reason code: {discReason}, Extended: {extendedReason}).";
        }
    }

    private sealed class RdpAxEventSink : IMsTscAxEvents
    {
        private readonly RdpAxClient _client;

        public RdpAxEventSink(RdpAxClient client)
        {
            _client = client;
        }

        public void OnConnecting() { }

        public void OnConnected()
        {
            _client.RaiseConnected();
        }

        public void OnLoginComplete() { }

        public void OnDisconnected(int discReason)
        {
            _client.RaiseDisconnected(discReason);
        }

        public void OnEnterFullScreenMode() { }
        public void OnLeaveFullScreenMode() { }
        public void OnChannelReceivedData(string chanName, string data) { }
        public void OnRequestGoFullScreen() { }
        public void OnRequestLeaveFullScreen() { }

        public void OnFatalError(int errorCode)
        {
            _client.RaiseFatalError(errorCode);
        }

        public void OnWarning(int warningCode)
        {
            _client.RaiseWarning(warningCode);
        }

        public void OnRemoteDesktopSizeChange(int width, int height) { }
        public void OnIdleTimeoutNotification() { }
        public void OnRequestContainerMinimize() { }
        public void OnConfirmClose(out bool pfAllowClose) { pfAllowClose = true; }
        public void OnReceivedTSPublicKey(string publicKey, out bool pfContinueLogon) { pfContinueLogon = true; }
        public void OnAutoReconnecting(int disconnectReason, int attemptCount, out int pContinueStatus) { pContinueStatus = 0; }
        public void OnAuthenticationWarningDisplayed() { }
        public void OnAuthenticationWarningDismissed() { }
        public void OnRemoteProgramResult(string bstrRemoteProgram, int lError, bool vbIsExecutable) { }
        public void OnRemoteProgramDisplayed(bool vbDisplayed, uint uDisplayInformation) { }
        public void OnRemoteWindowDisplayed(bool vbDisplayed, IntPtr hwnd, int windowState) { }

        public void OnLogonError(int lError)
        {
            _client.RaiseLogonError(lError);
        }

        public void OnFocusReleased(int iDirection) { }
        public void OnUserNameAcquired(string bstrUserName) { }
        public void OnMouseInputModeChanged(bool fMouseModeRelative) { }
        public void OnServiceMessageReceived(string serviceMessage) { }
        public void OnConnectionBarPullDown() { }
        public void OnNetworkStatusChanged(uint qualityLevel, int bandWidth, int rtt) { }
        public void OnDevicesButtonPressed() { }
        public void OnAutoReconnected() { }
        public void OnAutoReconnecting2(int disconnectReason, bool networkAvailable, int attemptCount, int maxAttemptCount) { }
    }
}
