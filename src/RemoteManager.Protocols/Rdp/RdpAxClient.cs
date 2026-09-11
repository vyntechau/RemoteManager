using System.Windows.Forms;

namespace RemoteManager.Protocols.Rdp;

public class RdpAxClient : AxHost
{
    // CLSID for MsTscAx / MsRdpClient
    public RdpAxClient() : base("8b918b82-7985-4c24-89df-c33ad2bbfbcd")
    {
        Dock = DockStyle.Fill;
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
}
