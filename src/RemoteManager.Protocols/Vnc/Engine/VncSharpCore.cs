#nullable disable
#pragma warning disable CS0649, CS8618, CS8602, CS8604, CS8625, CS8767, CS0169

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Sockets;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using VncSharpCore.Encodings;
using VncSharpCore.zlib.NET;

namespace VncSharpCore
{
	public class ConnectEventArgs : EventArgs
	{
		public int DesktopWidth { get; }

		public int DesktopHeight { get; }

		public string DesktopName { get; }

		public ConnectEventArgs(int width, int height, string name)
		{
			DesktopWidth = width;
			DesktopHeight = height;
			DesktopName = name;
		}
	}
	public class EncodedRectangleFactory
	{
		private RfbProtocol rfb;

		private Framebuffer framebuffer;

		public EncodedRectangleFactory(RfbProtocol rfb, Framebuffer framebuffer)
		{
			this.rfb = rfb;
			this.framebuffer = framebuffer;
		}

		public EncodedRectangle Build(Rectangle rectangle, int encoding)
		{
			return encoding switch
			{
				0 => new RawRectangle(rfb, framebuffer, rectangle), 
				1 => new CopyRectRectangle(rfb, framebuffer, rectangle), 
				2 => new RreRectangle(rfb, framebuffer, rectangle), 
				4 => new CoRreRectangle(rfb, framebuffer, rectangle), 
				5 => new HextileRectangle(rfb, framebuffer, rectangle), 
				16 => new ZrleRectangle(rfb, framebuffer, rectangle), 
				_ => throw new VncProtocolException("Unsupported Encoding Format received: " + encoding + "."), 
			};
		}
	}
	public class Framebuffer
	{
		private string name;

		private readonly int[] pixels;

		public int this[int index]
		{
			get
			{
				return pixels[index];
			}
			set
			{
				pixels[index] = value;
			}
		}

		public int Width { get; }

		public int Height { get; }

		public Rectangle Rectangle => new Rectangle(0, 0, Width, Height);

		public int BitsPerPixel { get; private set; }

		private int Depth { get; set; }

		private bool BigEndian { get; set; }

		private bool TrueColour { get; set; }

		public int RedMax { get; private set; }

		public int GreenMax { get; private set; }

		public int BlueMax { get; private set; }

		public int RedShift { get; private set; }

		public int GreenShift { get; private set; }

		public int BlueShift { get; private set; }

		public string DesktopName
		{
			get
			{
				return name;
			}
			set
			{
				if (value == null)
				{
					throw new ArgumentNullException("DesktopName");
				}
				name = value;
			}
		}

		private Framebuffer(int width, int height)
		{
			Width = width;
			Height = height;
			int num = width * height;
			pixels = new int[num];
		}

		public byte[] ToPixelFormat()
		{
			return new byte[16]
			{
				(byte)BitsPerPixel,
				(byte)Depth,
				(byte)(BigEndian ? 1u : 0u),
				(byte)(TrueColour ? 1u : 0u),
				(byte)((RedMax >> 8) & 0xFF),
				(byte)(RedMax & 0xFF),
				(byte)((GreenMax >> 8) & 0xFF),
				(byte)(GreenMax & 0xFF),
				(byte)((BlueMax >> 8) & 0xFF),
				(byte)(BlueMax & 0xFF),
				(byte)RedShift,
				(byte)GreenShift,
				(byte)BlueShift,
				0,
				0,
				0
			};
		}

		public static Framebuffer FromPixelFormat(byte[] b, int width, int height, int bitsPerPixel, int depth)
		{
			if (b.Length != 16)
			{
				throw new ArgumentException("Length of b must be 16 bytes.");
			}
			Framebuffer framebuffer = new Framebuffer(width, height);
			if (bitsPerPixel == 16 && depth == 16)
			{
				framebuffer.BitsPerPixel = 16;
				framebuffer.Depth = 16;
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = false;
				framebuffer.RedMax = 31;
				framebuffer.GreenMax = 63;
				framebuffer.BlueMax = 31;
				framebuffer.RedShift = 11;
				framebuffer.GreenShift = 5;
				framebuffer.BlueShift = 0;
			}
			else if (bitsPerPixel == 16 && depth == 8)
			{
				framebuffer.BitsPerPixel = 16;
				framebuffer.Depth = 8;
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = false;
				framebuffer.RedMax = 31;
				framebuffer.GreenMax = 63;
				framebuffer.BlueMax = 31;
				framebuffer.RedShift = 11;
				framebuffer.GreenShift = 5;
				framebuffer.BlueShift = 0;
			}
			else if (bitsPerPixel == 8 && depth == 8)
			{
				framebuffer.BitsPerPixel = 8;
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = false;
				framebuffer.Depth = 8;
				framebuffer.RedMax = 7;
				framebuffer.GreenMax = 7;
				framebuffer.BlueMax = 3;
				framebuffer.RedShift = 0;
				framebuffer.GreenShift = 3;
				framebuffer.BlueShift = 6;
			}
			else if (bitsPerPixel == 8 && depth == 6)
			{
				framebuffer.BitsPerPixel = 8;
				framebuffer.Depth = 6;
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = false;
				framebuffer.RedMax = 3;
				framebuffer.GreenMax = 3;
				framebuffer.BlueMax = 3;
				framebuffer.RedShift = 4;
				framebuffer.GreenShift = 2;
				framebuffer.BlueShift = 0;
			}
			else if (bitsPerPixel == 8 && depth == 3)
			{
				framebuffer.BitsPerPixel = 8;
				framebuffer.Depth = 3;
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = false;
				framebuffer.RedMax = 1;
				framebuffer.GreenMax = 1;
				framebuffer.BlueMax = 1;
				framebuffer.RedShift = 2;
				framebuffer.GreenShift = 1;
				framebuffer.BlueShift = 0;
			}
			else
			{
				framebuffer.BitsPerPixel = b[0];
				framebuffer.Depth = b[1];
				framebuffer.BigEndian = b[2] != 0;
				framebuffer.TrueColour = b[3] != 0;
				framebuffer.RedMax = b[5] | (b[4] << 8);
				framebuffer.GreenMax = b[7] | (b[6] << 8);
				framebuffer.BlueMax = b[9] | (b[8] << 8);
				framebuffer.RedShift = b[10];
				framebuffer.GreenShift = b[11];
				framebuffer.BlueShift = b[12];
			}
			return framebuffer;
		}
	}
	public interface IDesktopUpdater
	{
		Rectangle UpdateRectangle { get; }

		void Draw(Bitmap desktop);
	}
	public interface IVncInputPolicy
	{
		void WriteKeyboardEvent(uint keysym, bool pressed);

		void WritePointerEvent(byte buttonMask, Point point);
	}
	public class KeyboardHook
	{
		[Flags]
		public enum ModifierKeys
		{
			None = 0,
			Shift = 1,
			LeftShift = 2,
			RightShift = 4,
			Control = 8,
			LeftControl = 0x10,
			RightControl = 0x20,
			Alt = 0x40,
			LeftAlt = 0x80,
			RightAlt = 0x100,
			Win = 0x200,
			LeftWin = 0x400,
			RightWin = 0x800
		}

		protected class KeyNotificationEntry : IEquatable<KeyNotificationEntry>
		{
			public IntPtr WindowHandle;

			public int KeyCode;

			public ModifierKeys ModifierKeys;

			public bool Block;

			public bool Equals(KeyNotificationEntry obj)
			{
				if (obj != null && WindowHandle == obj.WindowHandle && KeyCode == obj.KeyCode && ModifierKeys == obj.ModifierKeys)
				{
					return Block == obj.Block;
				}
				return false;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public class HookKeyMsgData
		{
			public int KeyCode;

			public ModifierKeys ModifierKeys;

			public bool WasBlocked;
		}

		private const string HookKeyMsgName = "HOOKKEYMSG-{EC4E5587-8F3A-4A56-A00B-2A5F827ABA79}";

		private static uint _hookKeyMsg;

		private static int _referenceCount;

		private static IntPtr _hook;

		private static readonly NativeMethods.LowLevelKeyboardProcDelegate LowLevelKeyboardProcStaticDelegate = LowLevelKeyboardProc;

		private static readonly List<KeyNotificationEntry> NotificationEntries = new List<KeyNotificationEntry>();

		private static readonly Dictionary<int, ModifierKeys> ModifierKeyTable = new Dictionary<int, ModifierKeys>
		{
			{
				16,
				ModifierKeys.Shift
			},
			{
				160,
				ModifierKeys.LeftShift
			},
			{
				161,
				ModifierKeys.RightShift
			},
			{
				17,
				ModifierKeys.Control
			},
			{
				162,
				ModifierKeys.LeftControl
			},
			{
				163,
				ModifierKeys.RightControl
			},
			{
				18,
				ModifierKeys.Alt
			},
			{
				164,
				ModifierKeys.LeftAlt
			},
			{
				165,
				ModifierKeys.RightAlt
			},
			{
				91,
				ModifierKeys.LeftWin
			},
			{
				92,
				ModifierKeys.RightWin
			}
		};

		public static uint HookKeyMsg
		{
			get
			{
				if (_hookKeyMsg != 0)
				{
					return _hookKeyMsg;
				}
				_hookKeyMsg = NativeMethods.RegisterWindowMessage("HOOKKEYMSG-{EC4E5587-8F3A-4A56-A00B-2A5F827ABA79}");
				if (_hookKeyMsg == 0)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
				return _hookKeyMsg;
			}
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, HookKeyMsgData lParam);

		public KeyboardHook()
		{
			_referenceCount++;
			SetHook();
		}

		~KeyboardHook()
		{
			_referenceCount--;
			if (_referenceCount < 1)
			{
				UnsetHook();
			}
		}

		private static void SetHook()
		{
			if (!(_hook != IntPtr.Zero))
			{
				Process currentProcess = Process.GetCurrentProcess();
				ProcessModule mainModule = currentProcess.MainModule;
				IntPtr intPtr = NativeMethods.SetWindowsHookEx(13, LowLevelKeyboardProcStaticDelegate, NativeMethods.GetModuleHandle(mainModule.ModuleName), 0);
				if (intPtr == IntPtr.Zero)
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
				_hook = intPtr;
			}
		}

		private static void UnsetHook()
		{
			if (!(_hook == IntPtr.Zero))
			{
				NativeMethods.UnhookWindowsHookEx(_hook);
				_hook = IntPtr.Zero;
			}
		}

		private static IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, NativeMethods.KBDLLHOOKSTRUCT lParam)
		{
			int num = wParam.ToInt32();
			int num2 = 0;
			if (nCode != 0)
			{
				if (num2 == 0)
				{
					return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
				}
				return new IntPtr(num2);
			}
			if ((uint)(num - 256) <= 1u || (uint)(num - 260) <= 1u)
			{
				num2 = OnKey(num, lParam);
			}
			if (num2 == 0)
			{
				return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
			}
			return new IntPtr(num2);
		}

		private static int OnKey(int msg, NativeMethods.KBDLLHOOKSTRUCT key)
		{
			int result = 0;
			foreach (KeyNotificationEntry notificationEntry in NotificationEntries)
			{
				try
				{
					if (GetFocusWindow() != notificationEntry.WindowHandle || notificationEntry.KeyCode != key.vkCode)
					{
						continue;
					}
					ModifierKeys modifierKeyState = GetModifierKeyState();
					if (ModifierKeysMatch(notificationEntry.ModifierKeys, modifierKeyState))
					{
						IntPtr wParam = new IntPtr(msg);
						HookKeyMsgData lParam = new HookKeyMsgData
						{
							KeyCode = key.vkCode,
							ModifierKeys = modifierKeyState,
							WasBlocked = notificationEntry.Block
						};
						if (!PostMessage(notificationEntry.WindowHandle, HookKeyMsg, wParam, lParam))
						{
							throw new Win32Exception(Marshal.GetLastWin32Error());
						}
						if (notificationEntry.Block)
						{
							result = 1;
						}
					}
				}
				catch (Win32Exception ex)
				{
					if (ex.NativeErrorCode != 0)
					{
						throw;
					}
				}
			}
			return result;
		}

		private static IntPtr GetFocusWindow()
		{
			NativeMethods.GUITHREADINFO gUITHREADINFO = new NativeMethods.GUITHREADINFO();
			if (NativeMethods.GetGUIThreadInfo(0, gUITHREADINFO))
			{
				return NativeMethods.GetAncestor(gUITHREADINFO.hwndFocus, 2u);
			}
			int lastWin32Error = Marshal.GetLastWin32Error();
			throw new Win32Exception(lastWin32Error);
		}

		public static ModifierKeys GetModifierKeyState()
		{
			ModifierKeys modifierKeys = ModifierKeys.None;
			foreach (KeyValuePair<int, ModifierKeys> item in ModifierKeyTable)
			{
				if ((NativeMethods.GetAsyncKeyState(item.Key) & 0x8000) != 0)
				{
					modifierKeys |= item.Value;
				}
			}
			if ((modifierKeys & ModifierKeys.LeftWin) != ModifierKeys.None)
			{
				modifierKeys |= ModifierKeys.Win;
			}
			if ((modifierKeys & ModifierKeys.RightWin) != ModifierKeys.None)
			{
				modifierKeys |= ModifierKeys.Win;
			}
			return modifierKeys;
		}

		private static bool ModifierKeysMatch(ModifierKeys requestedKeys, ModifierKeys pressedKeys)
		{
			if ((requestedKeys & ModifierKeys.Shift) != ModifierKeys.None)
			{
				pressedKeys &= ~(ModifierKeys.LeftShift | ModifierKeys.RightShift);
			}
			if ((requestedKeys & ModifierKeys.Control) != ModifierKeys.None)
			{
				pressedKeys &= ~(ModifierKeys.LeftControl | ModifierKeys.RightControl);
			}
			if ((requestedKeys & ModifierKeys.Alt) != ModifierKeys.None)
			{
				pressedKeys &= ~(ModifierKeys.LeftAlt | ModifierKeys.RightAlt);
			}
			if ((requestedKeys & ModifierKeys.Win) != ModifierKeys.None)
			{
				pressedKeys &= ~(ModifierKeys.LeftWin | ModifierKeys.RightWin);
			}
			return requestedKeys == pressedKeys;
		}

		public static void RequestKeyNotification(IntPtr windowHandle, int keyCode, bool block)
		{
			RequestKeyNotification(windowHandle, keyCode, ModifierKeys.None, block);
		}

		public static void RequestKeyNotification(IntPtr windowHandle, int keyCode, ModifierKeys modifierKeys = ModifierKeys.None, bool block = false)
		{
			KeyNotificationEntry keyNotificationEntry = new KeyNotificationEntry
			{
				WindowHandle = windowHandle,
				KeyCode = keyCode,
				ModifierKeys = modifierKeys,
				Block = block
			};
			foreach (KeyNotificationEntry notificationEntry in NotificationEntries)
			{
				if (notificationEntry == keyNotificationEntry)
				{
					return;
				}
			}
			NotificationEntries.Add(keyNotificationEntry);
		}

		public static void CancelKeyNotification(IntPtr windowHandle, int keyCode, bool block)
		{
			CancelKeyNotification(windowHandle, keyCode, ModifierKeys.None, block);
		}

		private static void CancelKeyNotification(IntPtr windowHandle, int keyCode, ModifierKeys modifierKeys = ModifierKeys.None, bool block = false)
		{
			KeyNotificationEntry item = new KeyNotificationEntry
			{
				WindowHandle = windowHandle,
				KeyCode = keyCode,
				ModifierKeys = modifierKeys,
				Block = block
			};
			NotificationEntries.Remove(item);
		}
	}
	public static class NativeMethods
	{
		public delegate IntPtr LowLevelKeyboardProcDelegate(int nCode, IntPtr wParam, KBDLLHOOKSTRUCT lParam);

		[StructLayout(LayoutKind.Sequential)]
		public class KBDLLHOOKSTRUCT
		{
			internal int vkCode;

			internal int scanCode;

			internal int flags;

			internal int time;

			internal IntPtr dwExtraInfo;
		}

		public struct RECT
		{
			internal int left;

			internal int top;

			internal int right;

			internal int bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		public class GUITHREADINFO
		{
			internal int cbSize;

			internal int flags;

			internal IntPtr hwndActive;

			internal IntPtr hwndFocus;

			internal IntPtr hwndCapture;

			internal IntPtr hwndMenuOwner;

			internal IntPtr hwndMoveSize;

			internal IntPtr hwndCaret;

			internal RECT rcCaret;

			public GUITHREADINFO()
			{
				cbSize = Convert.ToInt32(Marshal.SizeOf(this));
			}
		}

		public const int GA_ROOT = 2;

		public const int WH_KEYBOARD_LL = 13;

		public const int HC_ACTION = 0;

		public const int WM_KEYDOWN = 256;

		public const int WM_KEYUP = 257;

		public const int WM_SYSKEYDOWN = 260;

		public const int WM_SYSKEYUP = 261;

		public const int KEYSTATE_PRESSED = 32768;

		public const int VK_CANCEL = 3;

		public const int VK_BACK = 8;

		public const int VK_TAB = 9;

		public const int VK_CLEAR = 12;

		public const int VK_RETURN = 13;

		public const int VK_PAUSE = 19;

		public const int VK_ESCAPE = 27;

		public const int VK_SNAPSHOT = 44;

		public const int VK_INSERT = 45;

		public const int VK_DELETE = 46;

		public const int VK_HOME = 36;

		public const int VK_END = 35;

		public const int VK_PRIOR = 33;

		public const int VK_NEXT = 34;

		public const int VK_LEFT = 37;

		public const int VK_UP = 38;

		public const int VK_RIGHT = 39;

		public const int VK_DOWN = 40;

		public const int VK_SELECT = 41;

		public const int VK_PRINT = 42;

		public const int VK_EXECUTE = 43;

		public const int VK_HELP = 47;

		public const int VK_LWIN = 91;

		public const int VK_RWIN = 92;

		public const int VK_APPS = 93;

		public const int VK_F1 = 112;

		public const int VK_F2 = 113;

		public const int VK_F3 = 114;

		public const int VK_F4 = 115;

		public const int VK_F5 = 116;

		public const int VK_F6 = 117;

		public const int VK_F7 = 118;

		public const int VK_F8 = 119;

		public const int VK_F9 = 120;

		public const int VK_F10 = 121;

		public const int VK_F11 = 122;

		public const int VK_F12 = 123;

		public const int VK_SHIFT = 16;

		public const int VK_LSHIFT = 160;

		public const int VK_RSHIFT = 161;

		public const int VK_CONTROL = 17;

		public const int VK_LCONTROL = 162;

		public const int VK_RCONTROL = 163;

		public const int VK_MENU = 18;

		public const int VK_LMENU = 164;

		public const int VK_RMENU = 165;

		public const int VK_OEM_1 = 186;

		public const int VK_OEM_2 = 191;

		public const int VK_OEM_3 = 192;

		public const int VK_OEM_4 = 219;

		public const int VK_OEM_5 = 220;

		public const int VK_OEM_6 = 221;

		public const int VK_OEM_7 = 222;

		public const int VK_OEM_8 = 223;

		public const int VK_OEM_102 = 226;

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProcDelegate lpfn, IntPtr hMod, int dwThreadId);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, KBDLLHOOKSTRUCT lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		internal static extern uint RegisterWindowMessage(string lpString);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetGUIThreadInfo(int idThread, GUITHREADINFO lpgui);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetKeyboardState(byte[] lpKeyState);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern int MapVirtualKey(int uCode, int uMapType);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern int ToAscii(int uVirtKey, int uScanCode, byte[] lpKeyState, byte[] lpwTransKey, int fuState);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
	}
	public class PasswordDialog : Form
	{
		private Button btnOk;

		private Button btnCancel;

		private TextBox txtPassword;

		private Container components;

		public string Password => txtPassword.Text;

		private PasswordDialog()
		{
			InitializeComponent();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				components?.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.btnOk = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this.txtPassword = new System.Windows.Forms.TextBox();
			base.SuspendLayout();
			this.btnOk.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.btnOk.Location = new System.Drawing.Point(144, 8);
			this.btnOk.Name = "btnOk";
			this.btnOk.Size = new System.Drawing.Size(64, 23);
			this.btnOk.TabIndex = 1;
			this.btnOk.Text = "OK";
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Location = new System.Drawing.Point(144, 40);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(64, 23);
			this.btnCancel.TabIndex = 2;
			this.btnCancel.Text = "Cancel";
			this.txtPassword.Location = new System.Drawing.Point(16, 16);
			this.txtPassword.Name = "txtPassword";
			this.txtPassword.PasswordChar = '*';
			this.txtPassword.Size = new System.Drawing.Size(112, 20);
			this.txtPassword.TabIndex = 0;
			this.txtPassword.Text = "";
			base.AcceptButton = this.btnOk;
			this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
			base.CancelButton = this.btnCancel;
			base.ClientSize = new System.Drawing.Size(216, 73);
			base.Controls.AddRange(new System.Windows.Forms.Control[3] { this.txtPassword, this.btnCancel, this.btnOk });
			base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.Name = "ConnectionPassword";
			base.ShowInTaskbar = false;
			base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Password";
			base.ResumeLayout(false);
		}

		public static string GetPassword()
		{
			using PasswordDialog passwordDialog = new PasswordDialog();
			return (passwordDialog.ShowDialog() == DialogResult.OK) ? passwordDialog.Password : null;
		}
	}
	public delegate void ConnectCompleteHandler(object sender, ConnectEventArgs e);
	public delegate string AuthenticateDelegate();
	public enum SpecialKeys
	{
		CtrlAltDel,
		AltF4,
		CtrlEsc,
		Ctrl,
		Alt
	}
	[ToolboxBitmap(typeof(RemoteDesktop), "Resources.vncviewer.ico")]
	public sealed class RemoteDesktop : Panel
	{
		private enum RuntimeState
		{
			Disconnected,
			Disconnecting,
			Connected,
			Connecting
		}

		public AuthenticateDelegate GetPassword;

		private Bitmap desktop;

		private readonly Image designModeDesktop;

		private VncClient vnc;

		private int port = 5900;

		private bool passwordPending;

		private VncDesktopTransformPolicy desktopPolicy;

		private RuntimeState state;

		private bool viewOnlyMode;

		private int bitsPerPixel;

		private int depth;

		private static readonly Dictionary<int, int> KeyTranslationTable = new Dictionary<int, int>
		{
			{ 3, 65385 },
			{ 8, 65288 },
			{ 9, 65289 },
			{ 12, 65291 },
			{ 13, 65293 },
			{ 19, 65299 },
			{ 27, 65307 },
			{ 44, 65301 },
			{ 45, 65379 },
			{ 46, 65535 },
			{ 36, 65360 },
			{ 35, 65367 },
			{ 33, 65365 },
			{ 34, 65366 },
			{ 37, 65361 },
			{ 38, 65362 },
			{ 39, 65363 },
			{ 40, 65364 },
			{ 41, 65376 },
			{ 42, 65377 },
			{ 43, 65378 },
			{ 47, 65386 },
			{ 112, 65470 },
			{ 113, 65471 },
			{ 114, 65472 },
			{ 115, 65473 },
			{ 116, 65474 },
			{ 117, 65475 },
			{ 118, 65476 },
			{ 119, 65477 },
			{ 120, 65478 },
			{ 121, 65479 },
			{ 122, 65480 },
			{ 123, 65481 },
			{ 93, 65383 }
		};

		private KeyboardHook.ModifierKeys PreviousModifierKeyState;

		[DefaultValue(5900)]
		[Description("The port number used by the VNC Host (typically 5900)")]
		public int VncPort
		{
			get
			{
				return port;
			}
			set
			{
				if (value < 1 || value > 65535)
				{
					value = 5900;
				}
				port = value;
			}
		}

		public bool IsConnected => state == RuntimeState.Connected;

		private new bool DesignMode
		{
			get
			{
				if (base.DesignMode)
				{
					return true;
				}
				for (Control parent = base.Parent; parent != null; parent = parent.Parent)
				{
					if (parent.Site != null && parent.Site.DesignMode)
					{
						return true;
					}
				}
				return false;
			}
		}

		protected override Size DefaultSize => new Size(400, 200);

		[Description("The name of the remote desktop.")]
		public string Hostname
		{
			get
			{
				if (vnc != null)
				{
					return vnc.HostName;
				}
				return "Disconnected";
			}
		}

		public Image Desktop => desktop;

		public bool ViewOnly
		{
			get
			{
				return viewOnlyMode;
			}
			set
			{
				viewOnlyMode = value;
			}
		}

		[DefaultValue(false)]
		[Description("Determines whether to use desktop scaling or leave it normal and clip")]
		public bool Scaled
		{
			get
			{
				return desktopPolicy is VncScaledDesktopPolicy;
			}
			set
			{
				SetScalingMode(value);
			}
		}

		[DefaultValue(0)]
		[Description("Sets the number of Bits Per Pixel for the Framebuffer--one of 8, 16, or 32")]
		public int BitsPerPixel
		{
			get
			{
				return bitsPerPixel;
			}
			set
			{
				bitsPerPixel = value;
			}
		}

		[DefaultValue(0)]
		[Description("Sets the Colour Depth of the Framebuffer--one of 3, 6, 8, or 16")]
		public int Depth
		{
			get
			{
				return depth;
			}
			set
			{
				depth = value;
			}
		}

		[Description("Raised after a successful call to the Connect() method.")]
		public event ConnectCompleteHandler ConnectComplete;

		[Description("Raised when the VNC Host drops the connection.")]
		public event EventHandler ConnectionLost;

		[Description("Raised when the VNC Host sends text to the client's clipboard.")]
		public event EventHandler ClipboardChanged;

		public RemoteDesktop()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer, value: true);
			try
			{
				Assembly assembly = Assembly.GetAssembly(GetType());
				Stream manifestResourceStream = assembly?.GetManifestResourceStream("VncSharpCore.Resources.screenshot.png");
				if (manifestResourceStream != null)
				{
					designModeDesktop = Image.FromStream(manifestResourceStream);
				}
			}
			catch { }
			desktopPolicy = new VncDesignModeDesktopPolicy(this);
			AutoScroll = desktopPolicy.AutoScroll;
			base.AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;
			GetPassword = PasswordDialog.GetPassword;
		}

		public void FullScreenUpdate()
		{
			InsureConnection(connected: true);
			vnc.FullScreenRefresh = true;
		}

		private void InsureConnection(bool connected)
		{
			if (connected)
			{
				if (state != RuntimeState.Connected && state != RuntimeState.Disconnecting)
				{
					throw new InvalidOperationException("RemoteDesktop must be in Connected state before calling methods that require an established connection.");
				}
			}
			else if (state != RuntimeState.Disconnected && state != RuntimeState.Disconnecting)
			{
				throw new InvalidOperationException("RemoteDesktop cannot be in Connected state when calling methods that establish a connection.");
			}
		}

		private void VncUpdate(object sender, VncEventArgs e)
		{
			e.DesktopUpdater.Draw(desktop);
			Invalidate(desktopPolicy.AdjustUpdateRectangle(e.DesktopUpdater.UpdateRectangle));
			if (state == RuntimeState.Connected)
			{
				vnc.FullScreenRefresh = false;
			}
		}

		public void Connect(string host)
		{
			Connect(host, 0);
		}

		public void Connect(string host, bool viewOnly)
		{
			Connect(host, 0, viewOnly);
		}

		public void Connect(string host, bool viewOnly, bool scaled)
		{
			Connect(host, 0, viewOnly, scaled);
		}

		public void Connect(string host, int display)
		{
			Connect(host, display, viewOnlyMode);
		}

		public void Connect(string host, int display, bool viewOnly)
		{
			Connect(host, display, viewOnly, scaled: false);
		}

		public void Connect(string host, int display, bool viewOnly, bool scaled)
		{
			InsureConnection(connected: false);
			if (host == null)
			{
				throw new ArgumentNullException("host");
			}
			if (display < 0)
			{
				throw new ArgumentOutOfRangeException("display", display, "Display number must be a positive integer.");
			}
			vnc = new VncClient();
			vnc.ConnectionLost += VncClientConnectionLost;
			vnc.ServerCutText += VncServerCutText;
			vnc.ViewOnly = viewOnly;
			passwordPending = vnc.Connect(host, display, VncPort, viewOnly);
			SetScalingMode(scaled);
			if (passwordPending)
			{
				string text = GetPassword();
				if (text != null)
				{
					Authenticate(text);
				}
			}
			else
			{
				Initialize();
			}
		}

		private void Authenticate(string password)
		{
			InsureConnection(connected: false);
			if (!passwordPending)
			{
				throw new InvalidOperationException("Authentication is only required when Connect() returns True and the VNC Host requires a password.");
			}
			if (password == null)
			{
				throw new NullReferenceException("password");
			}
			passwordPending = false;
			if (vnc.Authenticate(password))
			{
				Initialize();
			}
			else
			{
				OnConnectionLost();
			}
		}

		private void SetScalingMode(bool scaled)
		{
			if (scaled)
			{
				desktopPolicy = new VncScaledDesktopPolicy(vnc, this);
			}
			else
			{
				desktopPolicy = new VncClippedDesktopPolicy(vnc, this);
			}
			AutoScroll = desktopPolicy.AutoScroll;
			base.AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;
			Invalidate();
		}

		private void Initialize()
		{
			InsureConnection(connected: false);
			vnc.Initialize(bitsPerPixel, depth);
			SetState(RuntimeState.Connected);
			SetupDesktop();
			OnConnectComplete(new ConnectEventArgs(vnc.Framebuffer.Width, vnc.Framebuffer.Height, vnc.Framebuffer.DesktopName));
			base.AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;
			vnc.VncUpdate += VncUpdate;
			vnc.StartUpdates();
			KeyboardHook.RequestKeyNotification(base.Handle, 91, block: true);
			KeyboardHook.RequestKeyNotification(base.Handle, 92, block: true);
			KeyboardHook.RequestKeyNotification(base.Handle, 27, KeyboardHook.ModifierKeys.Control, block: true);
			KeyboardHook.RequestKeyNotification(base.Handle, 9, KeyboardHook.ModifierKeys.Alt, block: true);
		}

		private void SetState(RuntimeState newState)
		{
			state = newState;
			RuntimeState runtimeState = state;
			if (runtimeState == RuntimeState.Connected)
			{
				try
				{
					using var stream = GetType().Assembly.GetManifestResourceStream(GetType(), "Resources.vnccursor.cur");
					Cursor = stream != null ? new Cursor(stream) : Cursors.Default;
				}
				catch
				{
					Cursor = Cursors.Default;
				}
			}
			else
			{
				Cursor = Cursors.Default;
			}
		}

		private void SetupDesktop()
		{
			InsureConnection(connected: true);
			desktop = new Bitmap(vnc.Framebuffer.Width, vnc.Framebuffer.Height, PixelFormat.Format32bppPArgb);
			DrawDesktopMessage("Connecting to VNC host, please wait...");
		}

		private void DrawDesktopMessage(string message)
		{
			using Graphics graphics = Graphics.FromImage(desktop);
			graphics.FillRectangle(Brushes.Black, vnc.Framebuffer.Rectangle);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			graphics.DrawString(message, new Font("Arial", 12f), new SolidBrush(Color.White), new PointF(vnc.Framebuffer.Width / 2, vnc.Framebuffer.Height / 2), format);
		}

		public void Disconnect()
		{
			InsureConnection(connected: true);
			vnc.ConnectionLost -= VncClientConnectionLost;
			vnc.ServerCutText -= VncServerCutText;
			vnc.Disconnect();
			SetState(RuntimeState.Disconnected);
			OnConnectionLost();
			Invalidate();
		}

		public void FillServerClipboard()
		{
			FillServerClipboard(Clipboard.GetText());
		}

		private void FillServerClipboard(string text)
		{
			vnc.WriteClientCutText(text);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (state != RuntimeState.Disconnected)
				{
					Disconnect();
				}
				desktop?.Dispose();
				designModeDesktop?.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == KeyboardHook.HookKeyMsg)
			{
				KeyboardHook.HookKeyMsgData hookKeyMsgData = (KeyboardHook.HookKeyMsgData)Marshal.PtrToStructure(m.LParam, typeof(KeyboardHook.HookKeyMsgData));
				HandleKeyboardEvent(m.WParam.ToInt32(), hookKeyMsgData.KeyCode, hookKeyMsgData.ModifierKeys);
			}
			else
			{
				base.WndProc(ref m);
			}
		}

		protected override void OnPaint(PaintEventArgs pe)
		{
			if (!DesignMode)
			{
				switch (state)
				{
				case RuntimeState.Connected:
					if (desktop != null)
					{
						DrawDesktopImage(desktop, pe.Graphics);
					}
					break;
				default:
					throw new NotImplementedException($"RemoteDesktop in unknown State: {state}.");
				case RuntimeState.Disconnected:
				case RuntimeState.Disconnecting:
				case RuntimeState.Connecting:
					break;
				}
			}
			else if (designModeDesktop != null)
			{
				DrawDesktopImage(designModeDesktop, pe.Graphics);
			}
			base.OnPaint(pe);
		}

		protected override void OnResize(EventArgs eventargs)
		{
			Control control = base.Parent;
			while (control != null)
			{
				if (control is Form)
				{
					Form form = control as Form;
					if (form.WindowState == FormWindowState.Maximized)
					{
						form.Invalidate();
					}
					control = null;
				}
				else
				{
					control = control.Parent;
				}
			}
			base.OnResize(eventargs);
		}

		private void DrawDesktopImage(Image desktopImage, Graphics g)
		{
			g.DrawImage(desktopImage, desktopPolicy.RepositionImage(desktopImage));
		}

		private void VncClientConnectionLost(object sender, EventArgs e)
		{
			if (state == RuntimeState.Connected)
			{
				SetState(RuntimeState.Disconnecting);
				Disconnect();
			}
		}

		private void VncServerCutText(object sender, EventArgs e)
		{
			OnClipboardChanged();
		}

		private void OnClipboardChanged()
		{
			ClipboardChanged?.Invoke(this, EventArgs.Empty);
		}

		private void OnConnectionLost()
		{
			ConnectionLost?.Invoke(this, EventArgs.Empty);
		}

		private void OnConnectComplete(ConnectEventArgs e)
		{
			ConnectComplete?.Invoke(this, e);
		}

		protected override void OnMouseMove(MouseEventArgs mea)
		{
			UpdateRemotePointer();
			base.OnMouseMove(mea);
		}

		protected override void OnMouseDown(MouseEventArgs mea)
		{
			if (!Focused)
			{
				Focus();
				Select();
			}
			else
			{
				UpdateRemotePointer();
			}
			base.OnMouseDown(mea);
		}

		protected override void OnMouseUp(MouseEventArgs mea)
		{
			UpdateRemotePointer();
			base.OnMouseUp(mea);
		}

		protected override void OnMouseWheel(MouseEventArgs mea)
		{
			if (!DesignMode && IsConnected)
			{
				Point current = PointToClient(Control.MousePosition);
				byte b = 0;
				if (mea.Delta > 0)
				{
					b += 8;
				}
				else if (mea.Delta < 0)
				{
					b += 16;
				}
				vnc.WritePointerEvent(b, desktopPolicy.GetMouseMovePoint(current));
			}
			base.OnMouseWheel(mea);
		}

		private void UpdateRemotePointer()
		{
			if (!DesignMode && IsConnected)
			{
				Point point = PointToClient(Control.MousePosition);
				byte b = 0;
				if (Control.MouseButtons == MouseButtons.Left)
				{
					b++;
				}
				if (Control.MouseButtons == MouseButtons.Middle)
				{
					b += 2;
				}
				if (Control.MouseButtons == MouseButtons.Right)
				{
					b += 4;
				}
				if (desktopPolicy.GetMouseMoveRectangle().Contains(point))
				{
					vnc.WritePointerEvent(b, desktopPolicy.UpdateRemotePointer(point));
				}
			}
		}

		protected override bool ProcessKeyEventArgs(ref Message m)
		{
			return HandleKeyboardEvent(m.Msg, m.WParam.ToInt32(), KeyboardHook.GetModifierKeyState());
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			return ProcessKeyEventArgs(ref msg);
		}

		public static int TranslateVirtualKey(int virtualKey, KeyboardHook.ModifierKeys modifierKeys)
		{
			if (KeyTranslationTable.ContainsKey(virtualKey))
			{
				return KeyTranslationTable[virtualKey];
			}
			byte[] array = new byte[256];
			if (!NativeMethods.GetKeyboardState(array))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			array[17] = 0;
			array[162] = 0;
			array[163] = 0;
			array[18] = 0;
			array[164] = 0;
			array[165] = 0;
			array[91] = 0;
			array[92] = 0;
			byte[] array2 = new byte[2];
			int num = NativeMethods.ToAscii(virtualKey, NativeMethods.MapVirtualKey(virtualKey, 0), array, array2, 0);
			if (num <= 0)
			{
				return virtualKey;
			}
			return Convert.ToInt32(array2[num - 1]);
		}

		public static bool IsModifierKey(int keyCode)
		{
			if ((uint)(keyCode - 16) <= 2u || (uint)(keyCode - 91) <= 1u || (uint)(keyCode - 160) <= 5u)
			{
				return true;
			}
			return false;
		}

		private void SyncModifierKeyState(KeyboardHook.ModifierKeys modifierKeys)
		{
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftShift) != (modifierKeys & KeyboardHook.ModifierKeys.LeftShift))
			{
				vnc.WriteKeyboardEvent(65505u, (modifierKeys & KeyboardHook.ModifierKeys.LeftShift) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightShift) != (modifierKeys & KeyboardHook.ModifierKeys.RightShift))
			{
				vnc.WriteKeyboardEvent(65506u, (modifierKeys & KeyboardHook.ModifierKeys.RightShift) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftControl) != (modifierKeys & KeyboardHook.ModifierKeys.LeftControl))
			{
				vnc.WriteKeyboardEvent(65507u, (modifierKeys & KeyboardHook.ModifierKeys.LeftControl) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightControl) != (modifierKeys & KeyboardHook.ModifierKeys.RightControl))
			{
				vnc.WriteKeyboardEvent(65508u, (modifierKeys & KeyboardHook.ModifierKeys.RightControl) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftAlt) != (modifierKeys & KeyboardHook.ModifierKeys.LeftAlt))
			{
				vnc.WriteKeyboardEvent(65513u, (modifierKeys & KeyboardHook.ModifierKeys.LeftAlt) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightAlt) != (modifierKeys & KeyboardHook.ModifierKeys.RightAlt))
			{
				vnc.WriteKeyboardEvent(65514u, (modifierKeys & KeyboardHook.ModifierKeys.RightAlt) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftWin) != (modifierKeys & KeyboardHook.ModifierKeys.LeftWin))
			{
				vnc.WriteKeyboardEvent(65515u, (modifierKeys & KeyboardHook.ModifierKeys.LeftWin) != 0);
			}
			if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightWin) != (modifierKeys & KeyboardHook.ModifierKeys.RightWin))
			{
				vnc.WriteKeyboardEvent(65516u, (modifierKeys & KeyboardHook.ModifierKeys.RightWin) != 0);
			}
			PreviousModifierKeyState = modifierKeys;
		}

		private bool HandleKeyboardEvent(int msg, int virtualKey, KeyboardHook.ModifierKeys modifierKeys)
		{
			if (DesignMode || !IsConnected)
			{
				return false;
			}
			if (modifierKeys != PreviousModifierKeyState)
			{
				SyncModifierKeyState(modifierKeys);
			}
			if (IsModifierKey(virtualKey))
			{
				return true;
			}
			bool pressed;
			switch (msg)
			{
			case 256:
			case 260:
				pressed = true;
				break;
			case 257:
			case 261:
				pressed = false;
				break;
			default:
				return false;
			}
			vnc.WriteKeyboardEvent(Convert.ToUInt32(TranslateVirtualKey(virtualKey, modifierKeys)), pressed);
			return true;
		}

		public void SendSpecialKeys(SpecialKeys keys)
		{
			SendSpecialKeys(keys, release: true);
		}

		private void SendSpecialKeys(SpecialKeys keys, bool release)
		{
			InsureConnection(connected: true);
			switch (keys)
			{
			case SpecialKeys.Ctrl:
				PressKeys(new uint[1] { 65507u }, release);
				break;
			case SpecialKeys.Alt:
				PressKeys(new uint[1] { 65513u }, release);
				break;
			case SpecialKeys.CtrlAltDel:
				PressKeys(new uint[3] { 65507u, 65513u, 65535u }, release);
				break;
			case SpecialKeys.AltF4:
				PressKeys(new uint[2] { 65513u, 65473u }, release);
				break;
			case SpecialKeys.CtrlEsc:
				PressKeys(new uint[2] { 65507u, 65307u }, release);
				break;
			}
		}

		private void PressKeys(uint[] keys, bool release)
		{
			foreach (uint keysym in keys)
			{
				vnc.WriteKeyboardEvent(keysym, pressed: true);
			}
			if (release)
			{
				for (int num = keys.Length - 1; num >= 0; num--)
				{
					vnc.WriteKeyboardEvent(keys[num], pressed: false);
				}
			}
		}
	}
	public class RfbProtocol
	{
		protected sealed class BigEndianBinaryReader : BinaryReader
		{
			private byte[] buff = new byte[4];

			public BigEndianBinaryReader(Stream input)
				: base(input)
			{
			}

			public BigEndianBinaryReader(Stream input, Encoding encoding)
				: base(input, encoding)
			{
			}

			public override ushort ReadUInt16()
			{
				FillBuff(2);
				return (ushort)(buff[1] | (buff[0] << 8));
			}

			public override short ReadInt16()
			{
				FillBuff(2);
				return (short)((buff[1] & 0xFF) | (buff[0] << 8));
			}

			public override uint ReadUInt32()
			{
				FillBuff(4);
				return (uint)((buff[3] & 0xFF) | (buff[2] << 8) | (buff[1] << 16) | (buff[0] << 24));
			}

			public override int ReadInt32()
			{
				FillBuff(4);
				return buff[3] | (buff[2] << 8) | (buff[1] << 16) | (buff[0] << 24);
			}

			private void FillBuff(int totalBytes)
			{
				int num = 0;
				do
				{
					int num2 = BaseStream.Read(buff, num, totalBytes - num);
					if (num2 == 0)
					{
						throw new IOException("Unable to read next byte(s).");
					}
					num += num2;
				}
				while (num < totalBytes);
			}
		}

		protected sealed class BigEndianBinaryWriter : BinaryWriter
		{
			public BigEndianBinaryWriter(Stream input)
				: base(input)
			{
			}

			public BigEndianBinaryWriter(Stream input, Encoding encoding)
				: base(input, encoding)
			{
			}

			public override void Write(ushort value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			public override void Write(short value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			public override void Write(uint value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			public override void Write(int value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			public override void Write(ulong value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			public override void Write(long value)
			{
				FlipAndWrite(BitConverter.GetBytes(value));
			}

			private void FlipAndWrite(byte[] b)
			{
				Array.Reverse(b);
				base.Write(b);
			}
		}

		public sealed class ZRLECompressedReader : BinaryReader
		{
			private MemoryStream zlibMemoryStream;

			private ZOutputStream zlibDecompressedStream;

			private BinaryReader uncompressedReader;

			public ZRLECompressedReader(Stream uncompressedStream)
				: base(uncompressedStream)
			{
				zlibMemoryStream = new MemoryStream();
				zlibDecompressedStream = new ZOutputStream(zlibMemoryStream);
				uncompressedReader = new BinaryReader(zlibMemoryStream);
			}

			public override byte ReadByte()
			{
				return uncompressedReader.ReadByte();
			}

			public override byte[] ReadBytes(int count)
			{
				return uncompressedReader.ReadBytes(count);
			}

			public void DecodeStream()
			{
				zlibMemoryStream.Position = 0L;
				byte[] array = new byte[4];
				if (BaseStream.Read(array, 0, 4) != 4)
				{
					throw new Exception("ZRLE decoder: Invalid compressed stream size");
				}
				int num = array[3] | (array[2] << 8) | (array[1] << 16) | (array[0] << 24);
				if (num > 67108864)
				{
					throw new Exception("ZRLE decoder: Invalid compressed data size");
				}
				int num2 = num;
				byte[] buffer = new byte[65536];
				NetworkStream networkStream = (NetworkStream)BaseStream;
				networkStream.ReadTimeout = 15000;
				do
				{
					if (networkStream.DataAvailable)
					{
						int num3 = num2;
						if (num3 > 65536)
						{
							num3 = 65536;
						}
						int count = ((num3 > 1024) ? 1024 : num3);
						int num4 = 0;
						try
						{
							num4 = networkStream.Read(buffer, num4, count);
						}
						catch
						{
						}
						num2 -= num4;
						zlibDecompressedStream.Write(buffer, 0, num4);
					}
					else
					{
						Thread.Sleep(100);
					}
				}
				while (num2 > 0);
				zlibMemoryStream.Position = 0L;
			}
		}

		private const string RFB_VERSION_ZERO = "RFB 000.000\n";

		public const int RECEIVE_TIMEOUT = 15000;

		public const int SEND_TIMEOUT = 15000;

		public const int RAW_ENCODING = 0;

		public const int COPYRECT_ENCODING = 1;

		public const int RRE_ENCODING = 2;

		public const int CORRE_ENCODING = 4;

		public const int HEXTILE_ENCODING = 5;

		public const int ZRLE_ENCODING = 16;

		public const int FRAMEBUFFER_UPDATE = 0;

		public const int SET_COLOUR_MAP_ENTRIES = 1;

		public const int BELL = 2;

		public const int SERVER_CUT_TEXT = 3;

		private const byte SET_PIXEL_FORMAT = 0;

		private const byte SET_ENCODINGS = 2;

		private const byte FRAMEBUFFER_UPDATE_REQUEST = 3;

		private const byte KEY_EVENT = 4;

		private const byte POINTER_EVENT = 5;

		private const byte CLIENT_CUT_TEXT = 6;

		public const int XK_BackSpace = 65288;

		public const int XK_Tab = 65289;

		public const int XK_Clear = 65291;

		public const int XK_Return = 65293;

		public const int XK_Pause = 65299;

		public const int XK_Sys_Req = 65301;

		public const int XK_Escape = 65307;

		public const int XK_Home = 65360;

		public const int XK_Left = 65361;

		public const int XK_Up = 65362;

		public const int XK_Right = 65363;

		public const int XK_Down = 65364;

		public const int XK_Prior = 65365;

		public const int XK_Next = 65366;

		public const int XK_End = 65367;

		public const int XK_Select = 65376;

		public const int XK_Print = 65377;

		public const int XK_Execute = 65378;

		public const int XK_Insert = 65379;

		public const int XK_Menu = 65383;

		public const int XK_Cancel = 65385;

		public const int XK_Help = 65386;

		public const int XK_Break = 65387;

		public const int XK_F1 = 65470;

		public const int XK_F2 = 65471;

		public const int XK_F3 = 65472;

		public const int XK_F4 = 65473;

		public const int XK_F5 = 65474;

		public const int XK_F6 = 65475;

		public const int XK_F7 = 65476;

		public const int XK_F8 = 65477;

		public const int XK_F9 = 65478;

		public const int XK_F10 = 65479;

		public const int XK_F11 = 65480;

		public const int XK_F12 = 65481;

		public const int XK_Shift_L = 65505;

		public const int XK_Shift_R = 65506;

		public const int XK_Control_L = 65507;

		public const int XK_Control_R = 65508;

		public const int XK_Meta_L = 65511;

		public const int XK_Meta_R = 65512;

		public const int XK_Alt_L = 65513;

		public const int XK_Alt_R = 65514;

		public const int XK_Super_L = 65515;

		public const int XK_Super_R = 65516;

		public const int XK_Hyper_L = 65517;

		public const int XK_Hyper_R = 65518;

		public const int XK_Delete = 65535;

		private int verMajor;

		private int verMinor;

		private TcpClient tcp;

		private NetworkStream stream;

		private BinaryWriter writer;

		public float ServerVersion => (float)verMajor + (float)verMinor * 0.1f;

		private int ProxyID { get; set; }

		public BinaryReader Reader { get; private set; }

		public ZRLECompressedReader ZrleReader { get; private set; }

		public ushort[,] MapEntries { get; } = new ushort[256, 3];

		public void Connect(string host, int port)
		{
			if (host == null)
			{
				throw new ArgumentNullException("host");
			}
			tcp = new TcpClient
			{
				NoDelay = true
			};
			tcp.ReceiveTimeout = 15000;
			tcp.SendTimeout = 15000;
			tcp.Connect(host, port);
			stream = tcp.GetStream();
			stream.ReadTimeout = 15000;
			stream.WriteTimeout = 15000;
			Reader = new BigEndianBinaryReader(stream);
			writer = new BigEndianBinaryWriter(stream);
			ZrleReader = new ZRLECompressedReader(stream);
		}

		public void Close()
		{
			try
			{
				writer.Close();
				Reader.Close();
				stream.Close();
				tcp.Close();
			}
			catch (Exception)
			{
			}
		}

		public void ReadProtocolVersion()
		{
			byte[] array = Reader.ReadBytes(12);
			string text = "";
			string text2 = "";
			for (int i = 0; i < 12; i++)
			{
				text += $"{array[i]} ";
			}
			for (int j = 0; j < 12; j++)
			{
				text2 += $"{Convert.ToChar(array[j])} ";
			}
			if (Encoding.ASCII.GetString(array) == "RFB 000.000\n")
			{
				verMajor = 0;
				verMinor = 0;
				return;
			}
			if (array[0] == 82 && array[1] == 70 && array[2] == 66 && array[3] == 32 && array[4] == 48 && array[5] == 48 && array[6] == 52 && array[7] == 46 && array[8] == 48 && array[9] == 48 && array[10] == 49 && array[11] == 10)
			{
				verMajor = 3;
				verMinor = 8;
				return;
			}
			if (array[0] == 82 && array[1] == 70 && array[2] == 66 && array[3] == 32 && array[4] == 48 && array[5] == 48 && array[6] == 51 && array[7] == 46 && (array[8] == 48 || array[8] == 56) && (array[9] == 48 || array[9] == 56) && (array[10] == 51 || array[10] == 54 || array[10] == 55 || array[10] == 56 || array[10] == 57) && array[11] == 10)
			{
				verMajor = 3;
				switch (array[10])
				{
				case 51:
				case 54:
					verMinor = 3;
					break;
				case 55:
					verMinor = 7;
					break;
				case 56:
					verMinor = 8;
					break;
				case 57:
					verMinor = 8;
					break;
				case 52:
				case 53:
					break;
				}
				return;
			}
			throw new NotSupportedException("Only versions 3.3, 3.7, and 3.8 of the RFB Protocol are supported.\r\n" + text + "\r\n" + text2);
		}

		public void WriteProtocolVersion()
		{
			writer.Write(GetBytes($"RFB 003.00{verMinor}\n"));
			writer.Flush();
		}

		public void WriteProxyAddress()
		{
			byte[] array = new byte[250];
			GetBytes("ID:" + ProxyID + "\n").CopyTo(array, 0);
			writer.Write(array);
			writer.Flush();
		}

		public byte[] ReadSecurityTypes()
		{
			byte[] array;
			if (verMinor == 3)
			{
				array = new byte[1] { (byte)Reader.ReadUInt32() };
			}
			else
			{
				byte b = Reader.ReadByte();
				array = new byte[b];
				for (int i = 0; i < b; i++)
				{
					array[i] = Reader.ReadByte();
				}
			}
			return array;
		}

		public string ReadSecurityFailureReason()
		{
			int count = (int)Reader.ReadUInt32();
			return GetString(Reader.ReadBytes(count));
		}

		public void WriteSecurityType(byte type)
		{
			if (verMinor >= 7)
			{
				writer.Write(type);
				writer.Flush();
			}
		}

		public byte[] ReadSecurityChallenge()
		{
			return Reader.ReadBytes(16);
		}

		public void WriteSecurityResponse(byte[] response)
		{
			writer.Write(response, 0, response.Length);
			writer.Flush();
		}

		public uint ReadSecurityResult()
		{
			return Reader.ReadUInt32();
		}

		public void WriteClientInitialisation(bool shared)
		{
			writer.Write((byte)(shared ? 1u : 0u));
			writer.Flush();
		}

		public Framebuffer ReadServerInit(int bitsPerPixel, int depth)
		{
			int width = Reader.ReadUInt16();
			int height = Reader.ReadUInt16();
			Framebuffer framebuffer = Framebuffer.FromPixelFormat(Reader.ReadBytes(16), width, height, bitsPerPixel, depth);
			int count = (int)Reader.ReadUInt32();
			framebuffer.DesktopName = GetString(Reader.ReadBytes(count));
			return framebuffer;
		}

		public void WriteSetPixelFormat(Framebuffer buffer)
		{
			byte[] array = new byte[20]
			{
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0
			};
			buffer.ToPixelFormat().CopyTo(array, 4);
			writer.Write(array);
			writer.Flush();
		}

		public void WriteSetEncodings(uint[] encodings)
		{
			byte[] array = new byte[encodings.Length * 4 + 4];
			int num = 0;
			array[0] = 2;
			array[1] = 0;
			Enumerable.Reverse(BitConverter.GetBytes((ushort)encodings.Length)).ToArray().CopyTo(array, 2);
			foreach (uint value in encodings)
			{
				Enumerable.Reverse(BitConverter.GetBytes(value)).ToArray().CopyTo(array, 4 + num);
				num += 4;
			}
			writer.Write(array);
			writer.Flush();
		}

		public void WriteFramebufferUpdateRequest(ushort x, ushort y, ushort width, ushort height, bool incremental)
		{
			byte[] array = new byte[10]
			{
				3,
				(byte)(incremental ? 1u : 0u),
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0
			};
			Enumerable.Reverse(BitConverter.GetBytes(x)).ToArray().CopyTo(array, 2);
			Enumerable.Reverse(BitConverter.GetBytes(y)).ToArray().CopyTo(array, 4);
			Enumerable.Reverse(BitConverter.GetBytes(width)).ToArray().CopyTo(array, 6);
			Enumerable.Reverse(BitConverter.GetBytes(height)).ToArray().CopyTo(array, 8);
			writer.Write(array);
			writer.Flush();
		}

		public void WriteKeyEvent(uint keysym, bool pressed)
		{
			byte[] array = new byte[8]
			{
				4,
				(byte)(pressed ? 1u : 0u),
				0,
				0,
				0,
				0,
				0,
				0
			};
			Enumerable.Reverse(BitConverter.GetBytes(keysym)).ToArray().CopyTo(array, 4);
			writer.Write(array);
			writer.Flush();
		}

		public void WritePointerEvent(byte buttonMask, Point point)
		{
			byte[] array = new byte[6] { 5, buttonMask, 0, 0, 0, 0 };
			Enumerable.Reverse(BitConverter.GetBytes((ushort)point.X)).ToArray().CopyTo(array, 2);
			Enumerable.Reverse(BitConverter.GetBytes((ushort)point.Y)).ToArray().CopyTo(array, 4);
			writer.Write(array);
			writer.Flush();
		}

		public void WriteClientCutText(string text)
		{
			byte[] array = new byte[text.Length + 8];
			array[0] = 6;
			array[1] = 0;
			array[2] = 0;
			array[3] = 0;
			Enumerable.Reverse(BitConverter.GetBytes((uint)text.Length)).ToArray().CopyTo(array, 4);
			GetBytes(text).CopyTo(array, 8);
			writer.Write(array);
			writer.Flush();
		}

		public int ReadServerMessageType()
		{
			return Reader.ReadByte();
		}

		public int ReadFramebufferUpdate()
		{
			ReadPadding(1);
			return Reader.ReadUInt16();
		}

		public void ReadFramebufferUpdateRectHeader(out Rectangle rectangle, out int encoding)
		{
			rectangle = new Rectangle
			{
				X = Reader.ReadUInt16(),
				Y = Reader.ReadUInt16(),
				Width = Reader.ReadUInt16(),
				Height = Reader.ReadUInt16()
			};
			encoding = (int)Reader.ReadUInt32();
		}

		public void ReadColourMapEntry()
		{
			ReadPadding(1);
			ushort num = ReadUInt16();
			ushort num2 = ReadUInt16();
			int num3 = 0;
			while (num3 < num2)
			{
				MapEntries[num, 0] = (byte)(ReadUInt16() * 255 / 65535);
				MapEntries[num, 1] = (byte)(ReadUInt16() * 255 / 65535);
				MapEntries[num, 2] = (byte)(ReadUInt16() * 255 / 65535);
				num3++;
				num++;
			}
		}

		public string ReadServerCutText()
		{
			ReadPadding(3);
			int count = (int)Reader.ReadUInt32();
			return GetString(Reader.ReadBytes(count));
		}

		public uint ReadUint32()
		{
			return Reader.ReadUInt32();
		}

		public ushort ReadUInt16()
		{
			return Reader.ReadUInt16();
		}

		public byte ReadByte()
		{
			return Reader.ReadByte();
		}

		public byte[] ReadBytes(int count)
		{
			return Reader.ReadBytes(count);
		}

		public void WriteUint32(uint value)
		{
			writer.Write(value);
		}

		public void WriteUInt16(ushort value)
		{
			writer.Write(value);
		}

		public void WriteByte(byte value)
		{
			writer.Write(value);
		}

		protected void ReadPadding(int length)
		{
			ReadBytes(length);
		}

		protected void WritePadding(int length)
		{
			byte[] array = new byte[length];
			writer.Write(array, 0, array.Length);
		}

		protected static byte[] GetBytes(string text)
		{
			return Encoding.ASCII.GetBytes(text);
		}

		protected static string GetString(byte[] bytes)
		{
			return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
		}
	}
	public delegate void VncUpdateHandler(object sender, VncEventArgs e);
	public class VncClient
	{
		private RfbProtocol rfb;

		private byte securityType;

		private EncodedRectangleFactory factory;

		private Thread worker;

		private ManualResetEvent done;

		private IVncInputPolicy inputPolicy;

		private bool viewOnlyMode;

		public Framebuffer Framebuffer { get; private set; }

		public bool FullScreenRefresh { get; set; }

		public string HostName => Framebuffer.DesktopName;

		public bool ViewOnly
		{
			get
			{
				return viewOnlyMode;
			}
			set
			{
				viewOnlyMode = value;
				if (rfb != null)
				{
					if (viewOnlyMode)
					{
						inputPolicy = new VncViewInputPolicy(rfb);
					}
					else
					{
						inputPolicy = new VncDefaultInputPolicy(rfb);
					}
				}
			}
		}

		public event EventHandler ConnectionLost;

		public event EventHandler ServerCutText;

		public event VncUpdateHandler VncUpdate;

		public bool Connect(string host, int display, int port)
		{
			return Connect(host, display, port, viewOnlyMode);
		}

		public bool Connect(string host, int display, int port, bool viewOnly)
		{
			if (host == null)
			{
				throw new ArgumentNullException("host");
			}
			if (display < 0)
			{
				throw new ArgumentOutOfRangeException("display", display, "Display number must be non-negative.");
			}
			port += display;
			rfb = new RfbProtocol();
			viewOnlyMode = viewOnly;
			if (viewOnly)
			{
				inputPolicy = new VncViewInputPolicy(rfb);
			}
			else
			{
				inputPolicy = new VncDefaultInputPolicy(rfb);
			}
			try
			{
				rfb.Connect(host, port);
				rfb.ReadProtocolVersion();
				if ((double)rfb.ServerVersion == 0.0)
				{
					rfb.WriteProxyAddress();
					rfb.ReadProtocolVersion();
				}
				rfb.WriteProtocolVersion();
				byte[] array = rfb.ReadSecurityTypes();
				if (array.Length == 0)
				{
					throw new VncProtocolException("Protocol Error Connecting to Server. The Server didn't send any Security Types during the initial handshake.");
				}
				if (array[0] == 0)
				{
					throw new VncProtocolException("Connection Failed. The server rejected the connection for the following reason: " + rfb.ReadSecurityFailureReason());
				}
				securityType = GetSupportedSecurityType(array);
				rfb.WriteSecurityType(securityType);
				if (rfb.ServerVersion != 3.8f || securityType != 1)
				{
					return securityType > 1;
				}
				if (rfb.ReadSecurityResult() != 0)
				{
					throw new VncProtocolException("Unable to Connecto to the Server. The Server rejected the connection for the following reason: " + rfb.ReadSecurityFailureReason());
				}
				return securityType > 1;
			}
			catch (Exception ex)
			{
				throw new VncProtocolException("Unable to connect to the server. Error was: " + ex.Message, ex);
			}
		}

		public bool Connect(string host)
		{
			return Connect(host, 0, 5900);
		}

		public bool Connect(string host, int display)
		{
			return Connect(host, display, 5900);
		}

		private byte GetSupportedSecurityType(byte[] types)
		{
			for (int i = 0; i < types.Length; i++)
			{
				if (types[i] == 1 || types[i] == 2)
				{
					return types[i];
				}
			}
			return 0;
		}

		public bool Authenticate(string password)
		{
			if (password == null)
			{
				throw new ArgumentNullException("password");
			}
			if (securityType == 2)
			{
				PerformVncAuthentication(password);
				if (rfb.ReadSecurityResult() == 0)
				{
					return true;
				}
				if ((double)rfb.ServerVersion == 3.8)
				{
					rfb.ReadSecurityFailureReason();
				}
				rfb.Close();
				return false;
			}
			throw new NotSupportedException("Unable to Authenticate with Server. The Server uses an Authentication scheme unknown to the client.");
		}

		private void PerformVncAuthentication(string password)
		{
			byte[] challenge = rfb.ReadSecurityChallenge();
			rfb.WriteSecurityResponse(EncryptChallenge(password, challenge));
		}

		private byte[] EncryptChallenge(string password, byte[] challenge)
		{
			byte[] array = new byte[8];
			Encoding.ASCII.GetBytes(password, 0, (password.Length >= 8) ? 8 : password.Length, array, 0);
			for (int i = 0; i < 8; i++)
			{
				array[i] = (byte)(((array[i] & 1) << 7) | ((array[i] & 2) << 5) | ((array[i] & 4) << 3) | ((array[i] & 8) << 1) | ((array[i] & 0x10) >> 1) | ((array[i] & 0x20) >> 3) | ((array[i] & 0x40) >> 5) | ((array[i] & 0x80) >> 7));
			}
			DES dES = new DESCryptoServiceProvider
			{
				Padding = PaddingMode.None,
				Mode = CipherMode.ECB
			};
			ICryptoTransform cryptoTransform = dES.CreateEncryptor(array, null);
			byte[] array2 = new byte[16];
			cryptoTransform.TransformBlock(challenge, 0, challenge.Length, array2, 0);
			return array2;
		}

		public void Initialize(int bitsPerPixel, int depth)
		{
			rfb.WriteClientInitialisation(shared: true);
			Framebuffer = rfb.ReadServerInit(bitsPerPixel, depth);
			rfb.WriteSetEncodings(new uint[5] { 16u, 5u, 2u, 1u, 0u });
			rfb.WriteSetPixelFormat(Framebuffer);
			factory = new EncodedRectangleFactory(rfb, Framebuffer);
		}

		public void StartUpdates()
		{
			worker = new Thread(GetRfbUpdates);
			worker.SetApartmentState(ApartmentState.STA);
			worker.IsBackground = true;
			done = new ManualResetEvent(initialState: false);
			worker.Start();
		}

		public void Disconnect()
		{
			if (done != null)
			{
				done.Set();
			}
			try
			{
				rfb.WriteFramebufferUpdateRequest(0, 0, 1, 1, incremental: false);
			}
			catch
			{
			}
			if (worker != null)
			{
				worker.Join(3000);
			}
			rfb.Close();
			rfb = null;
		}

		private bool CheckIfThreadDone()
		{
			return done.WaitOne(0, exitContext: false);
		}

		private void GetRfbUpdates()
		{
			int num = 0;
			RequestScreenUpdate(refreshFullScreen: true);
			while (!CheckIfThreadDone())
			{
				try
				{
					switch (rfb.ReadServerMessageType())
					{
					case 0:
					{
						int num2 = rfb.ReadFramebufferUpdate();
						if (CheckIfThreadDone())
						{
							break;
						}
						for (int i = 0; i < num2; i++)
						{
							rfb.ReadFramebufferUpdateRectHeader(out var rectangle, out var encoding);
							EncodedRectangle encodedRectangle = factory.Build(rectangle, encoding);
							encodedRectangle.Decode();
							if (!CheckIfThreadDone() && VncUpdate != null)
							{
								VncEventArgs e = new VncEventArgs(encodedRectangle);
								if (VncUpdate.Target is Control control)
								{
									control.Invoke(VncUpdate, this, e);
								}
								else
								{
									VncUpdate(this, new VncEventArgs(encodedRectangle));
								}
							}
						}
						break;
					}
					case 2:
						Beep();
						break;
					case 3:
						if (!CheckIfThreadDone())
						{
							Clipboard.SetDataObject(rfb.ReadServerCutText().Replace("\n", Environment.NewLine), copy: true);
							OnServerCutText();
						}
						break;
					case 1:
						rfb.ReadColourMapEntry();
						break;
					}
					RequestScreenUpdate(FullScreenRefresh);
					num = 0;
				}
				catch
				{
					if (num++ > 1)
					{
						OnConnectionLost();
					}
					else
					{
						RequestScreenUpdate(refreshFullScreen: true);
					}
				}
				FullScreenRefresh = false;
			}
		}

		private void OnConnectionLost()
		{
			if (ConnectionLost?.Target is Control)
			{
				Control control = (Control)ConnectionLost.Target;
				if (control != null)
				{
					control.Invoke(ConnectionLost, this, EventArgs.Empty);
				}
				else
				{
					ConnectionLost(this, EventArgs.Empty);
				}
			}
		}

		private void OnServerCutText()
		{
			if (ServerCutText?.Target is Control)
			{
				Control control = (Control)ServerCutText.Target;
				if (control != null)
				{
					control.Invoke(ServerCutText, this, EventArgs.Empty);
				}
				else
				{
					ServerCutText(this, EventArgs.Empty);
				}
			}
		}

		private static void Beep()
		{
			SystemSounds.Beep.Play();
		}

		public void WriteClientCutText(string text)
		{
			try
			{
				rfb.WriteClientCutText(text);
			}
			catch
			{
				OnConnectionLost();
			}
		}

		public void WriteKeyboardEvent(uint keysym, bool pressed)
		{
			try
			{
				inputPolicy.WriteKeyboardEvent(keysym, pressed);
			}
			catch
			{
				OnConnectionLost();
			}
		}

		public void WritePointerEvent(byte buttonMask, Point point)
		{
			try
			{
				inputPolicy.WritePointerEvent(buttonMask, point);
			}
			catch
			{
				OnConnectionLost();
			}
		}

		public void RequestScreenUpdate(bool refreshFullScreen)
		{
			try
			{
				rfb.WriteFramebufferUpdateRequest(0, 0, (ushort)Framebuffer.Width, (ushort)Framebuffer.Height, !refreshFullScreen);
			}
			catch
			{
				OnConnectionLost();
			}
		}
	}
	public sealed class VncClippedDesktopPolicy : VncDesktopTransformPolicy
	{
		public override bool AutoScroll => true;

		public override Size AutoScrollMinSize
		{
			get
			{
				if (vnc?.Framebuffer == null)
				{
					return new Size(100, 100);
				}
				return new Size(vnc.Framebuffer.Width, vnc.Framebuffer.Height);
			}
		}

		public VncClippedDesktopPolicy(VncClient vnc, RemoteDesktop remoteDesktop)
			: base(vnc, remoteDesktop)
		{
		}

		public override Point UpdateRemotePointer(Point current)
		{
			Point result = default(Point);
			if (remoteDesktop.ClientSize.Width > remoteDesktop.Desktop.Size.Width)
			{
				result.X = current.X - (remoteDesktop.ClientRectangle.Width - remoteDesktop.Desktop.Width) / 2;
			}
			else
			{
				result.X = current.X - remoteDesktop.AutoScrollPosition.X;
			}
			if (remoteDesktop.ClientSize.Height > remoteDesktop.Desktop.Size.Height)
			{
				result.Y = current.Y - (remoteDesktop.ClientRectangle.Height - remoteDesktop.Desktop.Height) / 2;
			}
			else
			{
				result.Y = current.Y - remoteDesktop.AutoScrollPosition.Y;
			}
			return result;
		}

		public override Rectangle AdjustUpdateRectangle(Rectangle updateRectangle)
		{
			int x = ((remoteDesktop.ClientSize.Width <= remoteDesktop.Desktop.Size.Width) ? (updateRectangle.X + remoteDesktop.AutoScrollPosition.X) : (updateRectangle.X + (remoteDesktop.ClientRectangle.Width - remoteDesktop.Desktop.Width) / 2));
			int y = ((remoteDesktop.ClientSize.Height <= remoteDesktop.Desktop.Size.Height) ? (updateRectangle.Y + remoteDesktop.AutoScrollPosition.Y) : (updateRectangle.Y + (remoteDesktop.ClientRectangle.Height - remoteDesktop.Desktop.Height) / 2));
			return new Rectangle(x, y, updateRectangle.Width, updateRectangle.Height);
		}

		public override Rectangle RepositionImage(Image desktopImage)
		{
			int x = ((remoteDesktop.ClientSize.Width <= desktopImage.Width) ? remoteDesktop.DisplayRectangle.X : ((remoteDesktop.ClientRectangle.Width - desktopImage.Width) / 2));
			int y = ((remoteDesktop.ClientSize.Height <= desktopImage.Height) ? remoteDesktop.DisplayRectangle.Y : ((remoteDesktop.ClientRectangle.Height - desktopImage.Height) / 2));
			return new Rectangle(x, y, desktopImage.Width, desktopImage.Height);
		}

		public override Rectangle GetMouseMoveRectangle()
		{
			Rectangle rectangle = vnc.Framebuffer.Rectangle;
			if (remoteDesktop.ClientSize.Width > remoteDesktop.Desktop.Size.Width)
			{
				rectangle.X = (remoteDesktop.ClientRectangle.Width - remoteDesktop.Desktop.Width) / 2;
			}
			if (remoteDesktop.ClientSize.Height > remoteDesktop.Desktop.Size.Height)
			{
				rectangle.Y = (remoteDesktop.ClientRectangle.Height - remoteDesktop.Desktop.Height) / 2;
			}
			return rectangle;
		}

		public override Point GetMouseMovePoint(Point current)
		{
			return current;
		}
	}
	public sealed class VncDefaultInputPolicy : IVncInputPolicy
	{
		private RfbProtocol rfb;

		public VncDefaultInputPolicy(RfbProtocol rfb)
		{
			this.rfb = rfb;
		}

		public void WriteKeyboardEvent(uint keysym, bool pressed)
		{
			rfb.WriteKeyEvent(keysym, pressed);
		}

		public void WritePointerEvent(byte buttonMask, Point point)
		{
			rfb.WritePointerEvent(buttonMask, point);
		}
	}
	public sealed class VncDesignModeDesktopPolicy : VncDesktopTransformPolicy
	{
		public override bool AutoScroll => true;

		public override Size AutoScrollMinSize => new Size(608, 427);

		public VncDesignModeDesktopPolicy(RemoteDesktop remoteDesktop)
			: base(null, remoteDesktop)
		{
		}

		public override Point UpdateRemotePointer(Point current)
		{
			throw new NotImplementedException();
		}

		public override Rectangle AdjustUpdateRectangle(Rectangle updateRectangle)
		{
			throw new NotImplementedException();
		}

		public override Rectangle RepositionImage(Image desktopImage)
		{
			int x = ((remoteDesktop.ClientSize.Width <= desktopImage.Width) ? remoteDesktop.DisplayRectangle.X : ((remoteDesktop.ClientRectangle.Width - desktopImage.Width) / 2));
			int y = ((remoteDesktop.ClientSize.Height <= desktopImage.Height) ? remoteDesktop.DisplayRectangle.Y : ((remoteDesktop.ClientRectangle.Height - desktopImage.Height) / 2));
			return new Rectangle(x, y, remoteDesktop.ClientSize.Width, remoteDesktop.ClientSize.Height);
		}

		public override Rectangle GetMouseMoveRectangle()
		{
			throw new NotImplementedException();
		}

		public override Point GetMouseMovePoint(Point current)
		{
			throw new NotImplementedException();
		}
	}
	public abstract class VncDesktopTransformPolicy
	{
		protected VncClient vnc;

		protected RemoteDesktop remoteDesktop;

		public virtual bool AutoScroll => false;

		public abstract Size AutoScrollMinSize { get; }

		public VncDesktopTransformPolicy(VncClient vnc, RemoteDesktop remoteDesktop)
		{
			this.vnc = vnc;
			this.remoteDesktop = remoteDesktop;
		}

		public abstract Rectangle AdjustUpdateRectangle(Rectangle updateRectangle);

		public abstract Rectangle RepositionImage(Image desktopImage);

		public abstract Rectangle GetMouseMoveRectangle();

		public abstract Point GetMouseMovePoint(Point current);

		public abstract Point UpdateRemotePointer(Point current);
	}
	public class VncEventArgs : EventArgs
	{
		public IDesktopUpdater DesktopUpdater { get; }

		public VncEventArgs(IDesktopUpdater updater)
		{
			DesktopUpdater = updater;
		}
	}
	public class VncProtocolException : ApplicationException
	{
		public VncProtocolException()
		{
		}

		public VncProtocolException(string message)
			: base(message)
		{
		}

		public VncProtocolException(string message, Exception inner)
			: base(message, inner)
		{
		}

		public VncProtocolException(SerializationInfo info, StreamingContext cxt)
			: base(info, cxt)
		{
		}
	}
	public sealed class VncScaledDesktopPolicy : VncDesktopTransformPolicy
	{
		public override Size AutoScrollMinSize => new Size(100, 100);

		private double ScaleFactor
		{
			get
			{
				if ((double)remoteDesktop.ClientRectangle.Width / (double)vnc.Framebuffer.Width <= (double)remoteDesktop.ClientRectangle.Height / (double)vnc.Framebuffer.Height)
				{
					return (double)remoteDesktop.ClientRectangle.Width / (double)vnc.Framebuffer.Width;
				}
				return (double)remoteDesktop.ClientRectangle.Height / (double)vnc.Framebuffer.Height;
			}
		}

		public VncScaledDesktopPolicy(VncClient vnc, RemoteDesktop remoteDesktop)
			: base(vnc, remoteDesktop)
		{
		}

		public override Rectangle AdjustUpdateRectangle(Rectangle updateRectangle)
		{
			Size scaledSize = GetScaledSize(remoteDesktop.ClientRectangle.Size);
			Rectangle result = new Rectangle(AdjusteNormalToScaled(updateRectangle.X) + (remoteDesktop.ClientRectangle.Width - scaledSize.Width) / 2, AdjusteNormalToScaled(updateRectangle.Y) + (remoteDesktop.ClientRectangle.Height - scaledSize.Height) / 2, AdjusteNormalToScaled(updateRectangle.Width), AdjusteNormalToScaled(updateRectangle.Height));
			result.Inflate(1, 1);
			return result;
		}

		public override Rectangle RepositionImage(Image desktopImage)
		{
			return GetScaledRectangle(remoteDesktop.ClientRectangle);
		}

		public override Point UpdateRemotePointer(Point current)
		{
			return GetScaledMouse(current);
		}

		public override Rectangle GetMouseMoveRectangle()
		{
			return GetScaledRectangle(remoteDesktop.ClientRectangle);
		}

		public override Point GetMouseMovePoint(Point current)
		{
			return GetScaledMouse(current);
		}

		private Size GetScaledSize(Size s)
		{
			if (vnc == null)
			{
				return new Size(remoteDesktop.Width, remoteDesktop.Height);
			}
			if (!((double)s.Width / (double)vnc.Framebuffer.Width <= (double)s.Height / (double)vnc.Framebuffer.Height))
			{
				return new Size((int)((double)s.Height / (double)vnc.Framebuffer.Height * (double)vnc.Framebuffer.Width), s.Height);
			}
			return new Size(s.Width, (int)((double)s.Width / (double)vnc.Framebuffer.Width * (double)vnc.Framebuffer.Height));
		}

		private Point GetScaledMouse(Point src)
		{
			Size scaledSize = GetScaledSize(remoteDesktop.ClientRectangle.Size);
			src.X = AdjusteScaledToNormal(src.X - (remoteDesktop.ClientRectangle.Width - scaledSize.Width) / 2);
			src.Y = AdjusteScaledToNormal(src.Y - (remoteDesktop.ClientRectangle.Height - scaledSize.Height) / 2);
			return src;
		}

		private Rectangle GetScaledRectangle(Rectangle rect)
		{
			Size scaledSize = GetScaledSize(rect.Size);
			return new Rectangle((rect.Width - scaledSize.Width) / 2, (rect.Height - scaledSize.Height) / 2, scaledSize.Width, scaledSize.Height);
		}

		private int AdjusteScaledToNormal(double value)
		{
			return (int)Math.Round(value / ScaleFactor);
		}

		private int AdjusteNormalToScaled(double value)
		{
			return (int)Math.Round(value * ScaleFactor);
		}
	}
	public sealed class VncViewInputPolicy : IVncInputPolicy
	{
		public VncViewInputPolicy(RfbProtocol rfb)
		{
		}

		public void WriteKeyboardEvent(uint keysym, bool pressed)
		{
		}

		public void WritePointerEvent(byte buttonMask, Point point)
		{
		}
	}
}
namespace VncSharpCore.zlib.NET
{
	internal sealed class Adler32
	{
		private const int BASE = 65521;

		private const int NMAX = 5552;

		internal long adler32(long adler, byte[] buf, int index, int len)
		{
			if (buf == null)
			{
				return 1L;
			}
			long num = adler & 0xFFFF;
			long num2 = (adler >> 16) & 0xFFFF;
			while (len > 0)
			{
				int num3 = ((len < 5552) ? len : 5552);
				len -= num3;
				while (num3 >= 16)
				{
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num += buf[index++] & 0xFF;
					num2 += num;
					num3 -= 16;
				}
				if (num3 != 0)
				{
					do
					{
						num += buf[index++] & 0xFF;
						num2 += num;
					}
					while (--num3 != 0);
				}
				num %= 65521;
				num2 %= 65521;
			}
			return (num2 << 16) | num;
		}
	}
	public sealed class Deflate
	{
		internal class Config
		{
			internal int good_length;

			internal int max_lazy;

			internal int nice_length;

			internal int max_chain;

			internal int func;

			internal Config(int good_length, int max_lazy, int nice_length, int max_chain, int func)
			{
				this.good_length = good_length;
				this.max_lazy = max_lazy;
				this.nice_length = nice_length;
				this.max_chain = max_chain;
				this.func = func;
			}
		}

		private const int MAX_MEM_LEVEL = 9;

		private const int Z_DEFAULT_COMPRESSION = -1;

		private const int MAX_WBITS = 15;

		private const int DEF_MEM_LEVEL = 8;

		private const int STORED = 0;

		private const int FAST = 1;

		private const int SLOW = 2;

		private static Config[] config_table;

		private static readonly string[] z_errmsg;

		private const int NeedMore = 0;

		private const int BlockDone = 1;

		private const int FinishStarted = 2;

		private const int FinishDone = 3;

		private const int PRESET_DICT = 32;

		private const int Z_FILTERED = 1;

		private const int Z_HUFFMAN_ONLY = 2;

		private const int Z_DEFAULT_STRATEGY = 0;

		private const int Z_NO_FLUSH = 0;

		private const int Z_PARTIAL_FLUSH = 1;

		private const int Z_SYNC_FLUSH = 2;

		private const int Z_FULL_FLUSH = 3;

		private const int Z_FINISH = 4;

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		private const int INIT_STATE = 42;

		private const int BUSY_STATE = 113;

		private const int FINISH_STATE = 666;

		private const int Z_DEFLATED = 8;

		private const int STORED_BLOCK = 0;

		private const int STATIC_TREES = 1;

		private const int DYN_TREES = 2;

		private const int Z_BINARY = 0;

		private const int Z_ASCII = 1;

		private const int Z_UNKNOWN = 2;

		private const int Buf_size = 16;

		private const int REP_3_6 = 16;

		private const int REPZ_3_10 = 17;

		private const int REPZ_11_138 = 18;

		private const int MIN_MATCH = 3;

		private const int MAX_MATCH = 258;

		private static readonly int MIN_LOOKAHEAD;

		private const int MAX_BITS = 15;

		private const int D_CODES = 30;

		private const int BL_CODES = 19;

		private const int LENGTH_CODES = 29;

		private const int LITERALS = 256;

		private static readonly int L_CODES;

		private static readonly int HEAP_SIZE;

		private const int END_BLOCK = 256;

		internal ZStream strm;

		internal int status;

		internal byte[] pending_buf;

		internal int pending_buf_size;

		internal int pending_out;

		internal int pending;

		internal int noheader;

		internal byte data_type;

		internal byte method;

		internal int last_flush;

		internal int w_size;

		internal int w_bits;

		internal int w_mask;

		internal byte[] window;

		internal int window_size;

		internal short[] prev;

		internal short[] head;

		internal int ins_h;

		internal int hash_size;

		internal int hash_bits;

		internal int hash_mask;

		internal int hash_shift;

		internal int block_start;

		internal int match_length;

		internal int prev_match;

		internal int match_available;

		internal int strstart;

		internal int match_start;

		internal int lookahead;

		internal int prev_length;

		internal int max_chain_length;

		internal int max_lazy_match;

		internal int level;

		internal int strategy;

		internal int good_match;

		internal int nice_match;

		internal short[] dyn_ltree;

		internal short[] dyn_dtree;

		internal short[] bl_tree;

		internal Tree l_desc = new Tree();

		internal Tree d_desc = new Tree();

		internal Tree bl_desc = new Tree();

		internal short[] bl_count = new short[16];

		internal int[] heap = new int[2 * L_CODES + 1];

		internal int heap_len;

		internal int heap_max;

		internal byte[] depth = new byte[2 * L_CODES + 1];

		internal int l_buf;

		internal int lit_bufsize;

		internal int last_lit;

		internal int d_buf;

		internal int opt_len;

		internal int static_len;

		internal int matches;

		internal int last_eob_len;

		internal short bi_buf;

		internal int bi_valid;

		internal Deflate()
		{
			dyn_ltree = new short[HEAP_SIZE * 2];
			dyn_dtree = new short[122];
			bl_tree = new short[78];
		}

		internal void lm_init()
		{
			window_size = 2 * w_size;
			head[hash_size - 1] = 0;
			for (int i = 0; i < hash_size - 1; i++)
			{
				head[i] = 0;
			}
			max_lazy_match = config_table[level].max_lazy;
			good_match = config_table[level].good_length;
			nice_match = config_table[level].nice_length;
			max_chain_length = config_table[level].max_chain;
			strstart = 0;
			block_start = 0;
			lookahead = 0;
			match_length = (prev_length = 2);
			match_available = 0;
			ins_h = 0;
		}

		internal void tr_init()
		{
			l_desc.dyn_tree = dyn_ltree;
			l_desc.stat_desc = StaticTree.static_l_desc;
			d_desc.dyn_tree = dyn_dtree;
			d_desc.stat_desc = StaticTree.static_d_desc;
			bl_desc.dyn_tree = bl_tree;
			bl_desc.stat_desc = StaticTree.static_bl_desc;
			bi_buf = 0;
			bi_valid = 0;
			last_eob_len = 8;
			init_block();
		}

		internal void init_block()
		{
			for (int i = 0; i < L_CODES; i++)
			{
				dyn_ltree[i * 2] = 0;
			}
			for (int j = 0; j < 30; j++)
			{
				dyn_dtree[j * 2] = 0;
			}
			for (int k = 0; k < 19; k++)
			{
				bl_tree[k * 2] = 0;
			}
			dyn_ltree[512] = 1;
			opt_len = (static_len = 0);
			last_lit = (matches = 0);
		}

		internal void pqdownheap(short[] tree, int k)
		{
			int num = heap[k];
			for (int num2 = k << 1; num2 <= heap_len; num2 <<= 1)
			{
				if (num2 < heap_len && smaller(tree, heap[num2 + 1], heap[num2], depth))
				{
					num2++;
				}
				if (smaller(tree, num, heap[num2], depth))
				{
					break;
				}
				heap[k] = heap[num2];
				k = num2;
			}
			heap[k] = num;
		}

		internal static bool smaller(short[] tree, int n, int m, byte[] depth)
		{
			if (tree[n * 2] >= tree[m * 2])
			{
				if (tree[n * 2] == tree[m * 2])
				{
					return depth[n] <= depth[m];
				}
				return false;
			}
			return true;
		}

		internal void scan_tree(short[] tree, int max_code)
		{
			int num = -1;
			int num2 = tree[1];
			int num3 = 0;
			int num4 = 7;
			int num5 = 4;
			if (num2 == 0)
			{
				num4 = 138;
				num5 = 3;
			}
			tree[(max_code + 1) * 2 + 1] = (short)SupportClass.Identity(65535L);
			for (int i = 0; i <= max_code; i++)
			{
				int num6 = num2;
				num2 = tree[(i + 1) * 2 + 1];
				if (++num3 < num4 && num6 == num2)
				{
					continue;
				}
				if (num3 < num5)
				{
					bl_tree[num6 * 2] = (short)(bl_tree[num6 * 2] + num3);
				}
				else if (num6 != 0)
				{
					if (num6 != num)
					{
						bl_tree[num6 * 2]++;
					}
					bl_tree[32]++;
				}
				else if (num3 <= 10)
				{
					bl_tree[34]++;
				}
				else
				{
					bl_tree[36]++;
				}
				num3 = 0;
				num = num6;
				if (num2 == 0)
				{
					num4 = 138;
					num5 = 3;
				}
				else if (num6 == num2)
				{
					num4 = 6;
					num5 = 3;
				}
				else
				{
					num4 = 7;
					num5 = 4;
				}
			}
		}

		internal int build_bl_tree()
		{
			scan_tree(dyn_ltree, l_desc.max_code);
			scan_tree(dyn_dtree, d_desc.max_code);
			bl_desc.build_tree(this);
			int num = 18;
			while (num >= 3 && bl_tree[Tree.bl_order[num] * 2 + 1] == 0)
			{
				num--;
			}
			opt_len += 3 * (num + 1) + 5 + 5 + 4;
			return num;
		}

		internal void send_all_trees(int lcodes, int dcodes, int blcodes)
		{
			send_bits(lcodes - 257, 5);
			send_bits(dcodes - 1, 5);
			send_bits(blcodes - 4, 4);
			for (int i = 0; i < blcodes; i++)
			{
				send_bits(bl_tree[Tree.bl_order[i] * 2 + 1], 3);
			}
			send_tree(dyn_ltree, lcodes - 1);
			send_tree(dyn_dtree, dcodes - 1);
		}

		internal void send_tree(short[] tree, int max_code)
		{
			int num = -1;
			int num2 = tree[1];
			int num3 = 0;
			int num4 = 7;
			int num5 = 4;
			if (num2 == 0)
			{
				num4 = 138;
				num5 = 3;
			}
			for (int i = 0; i <= max_code; i++)
			{
				int num6 = num2;
				num2 = tree[(i + 1) * 2 + 1];
				if (++num3 < num4 && num6 == num2)
				{
					continue;
				}
				if (num3 < num5)
				{
					do
					{
						send_code(num6, bl_tree);
					}
					while (--num3 != 0);
				}
				else if (num6 != 0)
				{
					if (num6 != num)
					{
						send_code(num6, bl_tree);
						num3--;
					}
					send_code(16, bl_tree);
					send_bits(num3 - 3, 2);
				}
				else if (num3 <= 10)
				{
					send_code(17, bl_tree);
					send_bits(num3 - 3, 3);
				}
				else
				{
					send_code(18, bl_tree);
					send_bits(num3 - 11, 7);
				}
				num3 = 0;
				num = num6;
				if (num2 == 0)
				{
					num4 = 138;
					num5 = 3;
				}
				else if (num6 == num2)
				{
					num4 = 6;
					num5 = 3;
				}
				else
				{
					num4 = 7;
					num5 = 4;
				}
			}
		}

		internal void put_byte(byte[] p, int start, int len)
		{
			Array.Copy(p, start, pending_buf, pending, len);
			pending += len;
		}

		internal void put_byte(byte c)
		{
			pending_buf[pending++] = c;
		}

		internal void put_short(int w)
		{
			put_byte((byte)w);
			put_byte((byte)SupportClass.URShift(w, 8));
		}

		internal void putShortMSB(int b)
		{
			put_byte((byte)(b >> 8));
			put_byte((byte)b);
		}

		internal void send_code(int c, short[] tree)
		{
			send_bits(tree[c * 2] & 0xFFFF, tree[c * 2 + 1] & 0xFFFF);
		}

		internal void send_bits(int value_Renamed, int length)
		{
			if (bi_valid > 16 - length)
			{
				bi_buf = (short)((ushort)bi_buf | (ushort)((value_Renamed << bi_valid) & 0xFFFF));
				put_short(bi_buf);
				bi_buf = (short)SupportClass.URShift(value_Renamed, 16 - bi_valid);
				bi_valid += length - 16;
			}
			else
			{
				bi_buf = (short)((ushort)bi_buf | (ushort)((value_Renamed << bi_valid) & 0xFFFF));
				bi_valid += length;
			}
		}

		internal void _tr_align()
		{
			send_bits(2, 3);
			send_code(256, StaticTree.static_ltree);
			bi_flush();
			if (1 + last_eob_len + 10 - bi_valid < 9)
			{
				send_bits(2, 3);
				send_code(256, StaticTree.static_ltree);
				bi_flush();
			}
			last_eob_len = 7;
		}

		internal bool _tr_tally(int dist, int lc)
		{
			pending_buf[d_buf + last_lit * 2] = (byte)SupportClass.URShift(dist, 8);
			pending_buf[d_buf + last_lit * 2 + 1] = (byte)dist;
			pending_buf[l_buf + last_lit] = (byte)lc;
			last_lit++;
			if (dist == 0)
			{
				dyn_ltree[lc * 2]++;
			}
			else
			{
				matches++;
				dist--;
				dyn_ltree[(Tree._length_code[lc] + 256 + 1) * 2]++;
				dyn_dtree[Tree.d_code(dist) * 2]++;
			}
			if ((last_lit & 0x1FFF) == 0 && level > 2)
			{
				int num = last_lit * 8;
				int num2 = strstart - block_start;
				for (int i = 0; i < 30; i++)
				{
					num = (int)(num + dyn_dtree[i * 2] * (5L + (long)Tree.extra_dbits[i]));
				}
				num = SupportClass.URShift(num, 3);
				if (matches < last_lit / 2 && num < num2 / 2)
				{
					return true;
				}
			}
			return last_lit == lit_bufsize - 1;
		}

		internal void compress_block(short[] ltree, short[] dtree)
		{
			int num = 0;
			if (last_lit != 0)
			{
				do
				{
					int num2 = ((pending_buf[d_buf + num * 2] << 8) & 0xFF00) | (pending_buf[d_buf + num * 2 + 1] & 0xFF);
					int num3 = pending_buf[l_buf + num] & 0xFF;
					num++;
					if (num2 == 0)
					{
						send_code(num3, ltree);
						continue;
					}
					int num4 = Tree._length_code[num3];
					send_code(num4 + 256 + 1, ltree);
					int num5 = Tree.extra_lbits[num4];
					if (num5 != 0)
					{
						num3 -= Tree.base_length[num4];
						send_bits(num3, num5);
					}
					num2--;
					num4 = Tree.d_code(num2);
					send_code(num4, dtree);
					num5 = Tree.extra_dbits[num4];
					if (num5 != 0)
					{
						num2 -= Tree.base_dist[num4];
						send_bits(num2, num5);
					}
				}
				while (num < last_lit);
			}
			send_code(256, ltree);
			last_eob_len = ltree[513];
		}

		internal void set_data_type()
		{
			int i = 0;
			int num = 0;
			int num2 = 0;
			for (; i < 7; i++)
			{
				num2 += dyn_ltree[i * 2];
			}
			for (; i < 128; i++)
			{
				num += dyn_ltree[i * 2];
			}
			for (; i < 256; i++)
			{
				num2 += dyn_ltree[i * 2];
			}
			data_type = (byte)((num2 <= SupportClass.URShift(num, 2)) ? 1u : 0u);
		}

		internal void bi_flush()
		{
			if (bi_valid == 16)
			{
				put_short(bi_buf);
				bi_buf = 0;
				bi_valid = 0;
			}
			else if (bi_valid >= 8)
			{
				put_byte((byte)bi_buf);
				bi_buf = (short)SupportClass.URShift(bi_buf, 8);
				bi_valid -= 8;
			}
		}

		internal void bi_windup()
		{
			if (bi_valid > 8)
			{
				put_short(bi_buf);
			}
			else if (bi_valid > 0)
			{
				put_byte((byte)bi_buf);
			}
			bi_buf = 0;
			bi_valid = 0;
		}

		internal void copy_block(int buf, int len, bool header)
		{
			bi_windup();
			last_eob_len = 8;
			if (header)
			{
				put_short((short)len);
				put_short((short)(~len));
			}
			put_byte(window, buf, len);
		}

		internal void flush_block_only(bool eof)
		{
			_tr_flush_block((block_start >= 0) ? block_start : (-1), strstart - block_start, eof);
			block_start = strstart;
			strm.flush_pending();
		}

		internal int deflate_stored(int flush)
		{
			int num = 65535;
			if (num > pending_buf_size - 5)
			{
				num = pending_buf_size - 5;
			}
			while (true)
			{
				if (lookahead <= 1)
				{
					fill_window();
					if (lookahead == 0 && flush == 0)
					{
						return 0;
					}
					if (lookahead == 0)
					{
						break;
					}
				}
				strstart += lookahead;
				lookahead = 0;
				int num2 = block_start + num;
				if (strstart == 0 || strstart >= num2)
				{
					lookahead = strstart - num2;
					strstart = num2;
					flush_block_only(eof: false);
					if (strm.avail_out == 0)
					{
						return 0;
					}
				}
				if (strstart - block_start >= w_size - MIN_LOOKAHEAD)
				{
					flush_block_only(eof: false);
					if (strm.avail_out == 0)
					{
						return 0;
					}
				}
			}
			flush_block_only(flush == 4);
			if (strm.avail_out == 0)
			{
				if (flush != 4)
				{
					return 0;
				}
				return 2;
			}
			if (flush != 4)
			{
				return 1;
			}
			return 3;
		}

		internal void _tr_stored_block(int buf, int stored_len, bool eof)
		{
			send_bits(eof ? 1 : 0, 3);
			copy_block(buf, stored_len, header: true);
		}

		internal void _tr_flush_block(int buf, int stored_len, bool eof)
		{
			int num = 0;
			int num2;
			int num3;
			if (level > 0)
			{
				if (data_type == 2)
				{
					set_data_type();
				}
				l_desc.build_tree(this);
				d_desc.build_tree(this);
				num = build_bl_tree();
				num2 = SupportClass.URShift(opt_len + 3 + 7, 3);
				num3 = SupportClass.URShift(static_len + 3 + 7, 3);
				if (num3 <= num2)
				{
					num2 = num3;
				}
			}
			else
			{
				num2 = (num3 = stored_len + 5);
			}
			if (stored_len + 4 <= num2 && buf != -1)
			{
				_tr_stored_block(buf, stored_len, eof);
			}
			else if (num3 == num2)
			{
				send_bits(2 + (eof ? 1 : 0), 3);
				compress_block(StaticTree.static_ltree, StaticTree.static_dtree);
			}
			else
			{
				send_bits(4 + (eof ? 1 : 0), 3);
				send_all_trees(l_desc.max_code + 1, d_desc.max_code + 1, num + 1);
				compress_block(dyn_ltree, dyn_dtree);
			}
			init_block();
			if (eof)
			{
				bi_windup();
			}
		}

		internal void fill_window()
		{
			do
			{
				int num = window_size - lookahead - strstart;
				int num2;
				if (num == 0 && strstart == 0 && lookahead == 0)
				{
					num = w_size;
				}
				else if (num == -1)
				{
					num--;
				}
				else if (strstart >= w_size + w_size - MIN_LOOKAHEAD)
				{
					Array.Copy(window, w_size, window, 0, w_size);
					match_start -= w_size;
					strstart -= w_size;
					block_start -= w_size;
					num2 = hash_size;
					int num3 = num2;
					do
					{
						int num4 = head[--num3] & 0xFFFF;
						head[num3] = (short)((num4 >= w_size) ? (num4 - w_size) : 0);
					}
					while (--num2 != 0);
					num2 = w_size;
					num3 = num2;
					do
					{
						int num4 = prev[--num3] & 0xFFFF;
						prev[num3] = (short)((num4 >= w_size) ? (num4 - w_size) : 0);
					}
					while (--num2 != 0);
					num += w_size;
				}
				if (strm.avail_in == 0)
				{
					break;
				}
				num2 = strm.read_buf(window, strstart + lookahead, num);
				lookahead += num2;
				if (lookahead >= 3)
				{
					ins_h = window[strstart] & 0xFF;
					ins_h = ((ins_h << hash_shift) ^ (window[strstart + 1] & 0xFF)) & hash_mask;
				}
			}
			while (lookahead < MIN_LOOKAHEAD && strm.avail_in != 0);
		}

		internal int deflate_fast(int flush)
		{
			int num = 0;
			while (true)
			{
				if (lookahead < MIN_LOOKAHEAD)
				{
					fill_window();
					if (lookahead < MIN_LOOKAHEAD && flush == 0)
					{
						return 0;
					}
					if (lookahead == 0)
					{
						break;
					}
				}
				if (lookahead >= 3)
				{
					ins_h = ((ins_h << hash_shift) ^ (window[strstart + 2] & 0xFF)) & hash_mask;
					num = head[ins_h] & 0xFFFF;
					prev[strstart & w_mask] = head[ins_h];
					head[ins_h] = (short)strstart;
				}
				if (num != 0L && ((strstart - num) & 0xFFFF) <= w_size - MIN_LOOKAHEAD && strategy != 2)
				{
					match_length = longest_match(num);
				}
				bool flag;
				if (match_length >= 3)
				{
					flag = _tr_tally(strstart - match_start, match_length - 3);
					lookahead -= match_length;
					if (match_length <= max_lazy_match && lookahead >= 3)
					{
						match_length--;
						do
						{
							strstart++;
							ins_h = ((ins_h << hash_shift) ^ (window[strstart + 2] & 0xFF)) & hash_mask;
							num = head[ins_h] & 0xFFFF;
							prev[strstart & w_mask] = head[ins_h];
							head[ins_h] = (short)strstart;
						}
						while (--match_length != 0);
						strstart++;
					}
					else
					{
						strstart += match_length;
						match_length = 0;
						ins_h = window[strstart] & 0xFF;
						ins_h = ((ins_h << hash_shift) ^ (window[strstart + 1] & 0xFF)) & hash_mask;
					}
				}
				else
				{
					flag = _tr_tally(0, window[strstart] & 0xFF);
					lookahead--;
					strstart++;
				}
				if (flag)
				{
					flush_block_only(eof: false);
					if (strm.avail_out == 0)
					{
						return 0;
					}
				}
			}
			flush_block_only(flush == 4);
			if (strm.avail_out == 0)
			{
				if (flush == 4)
				{
					return 2;
				}
				return 0;
			}
			if (flush != 4)
			{
				return 1;
			}
			return 3;
		}

		internal int deflate_slow(int flush)
		{
			int num = 0;
			while (true)
			{
				if (lookahead < MIN_LOOKAHEAD)
				{
					fill_window();
					if (lookahead < MIN_LOOKAHEAD && flush == 0)
					{
						return 0;
					}
					if (lookahead == 0)
					{
						break;
					}
				}
				if (lookahead >= 3)
				{
					ins_h = ((ins_h << hash_shift) ^ (window[strstart + 2] & 0xFF)) & hash_mask;
					num = head[ins_h] & 0xFFFF;
					prev[strstart & w_mask] = head[ins_h];
					head[ins_h] = (short)strstart;
				}
				prev_length = match_length;
				prev_match = match_start;
				match_length = 2;
				if (num != 0 && prev_length < max_lazy_match && ((strstart - num) & 0xFFFF) <= w_size - MIN_LOOKAHEAD)
				{
					if (strategy != 2)
					{
						match_length = longest_match(num);
					}
					if (match_length <= 5 && (strategy == 1 || (match_length == 3 && strstart - match_start > 4096)))
					{
						match_length = 2;
					}
				}
				if (prev_length >= 3 && match_length <= prev_length)
				{
					int num2 = strstart + lookahead - 3;
					bool flag = _tr_tally(strstart - 1 - prev_match, prev_length - 3);
					lookahead -= prev_length - 1;
					prev_length -= 2;
					do
					{
						if (++strstart <= num2)
						{
							ins_h = ((ins_h << hash_shift) ^ (window[strstart + 2] & 0xFF)) & hash_mask;
							num = head[ins_h] & 0xFFFF;
							prev[strstart & w_mask] = head[ins_h];
							head[ins_h] = (short)strstart;
						}
					}
					while (--prev_length != 0);
					match_available = 0;
					match_length = 2;
					strstart++;
					if (flag)
					{
						flush_block_only(eof: false);
						if (strm.avail_out == 0)
						{
							return 0;
						}
					}
				}
				else if (match_available != 0)
				{
					if (_tr_tally(0, window[strstart - 1] & 0xFF))
					{
						flush_block_only(eof: false);
					}
					strstart++;
					lookahead--;
					if (strm.avail_out == 0)
					{
						return 0;
					}
				}
				else
				{
					match_available = 1;
					strstart++;
					lookahead--;
				}
			}
			if (match_available != 0)
			{
				bool flag = _tr_tally(0, window[strstart - 1] & 0xFF);
				match_available = 0;
			}
			flush_block_only(flush == 4);
			if (strm.avail_out == 0)
			{
				if (flush == 4)
				{
					return 2;
				}
				return 0;
			}
			if (flush != 4)
			{
				return 1;
			}
			return 3;
		}

		internal int longest_match(int cur_match)
		{
			int num = max_chain_length;
			int num2 = strstart;
			int num3 = prev_length;
			int num4 = ((strstart > w_size - MIN_LOOKAHEAD) ? (strstart - (w_size - MIN_LOOKAHEAD)) : 0);
			int num5 = nice_match;
			int num6 = w_mask;
			int num7 = strstart + 258;
			byte b = window[num2 + num3 - 1];
			byte b2 = window[num2 + num3];
			if (prev_length >= good_match)
			{
				num >>= 2;
			}
			if (num5 > lookahead)
			{
				num5 = lookahead;
			}
			do
			{
				int num8 = cur_match;
				if (window[num8 + num3] != b2 || window[num8 + num3 - 1] != b || window[num8] != window[num2] || window[++num8] != window[num2 + 1])
				{
					continue;
				}
				num2 += 2;
				num8++;
				while (window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && window[++num2] == window[++num8] && num2 < num7)
				{
				}
				int num9 = 258 - (num7 - num2);
				num2 = num7 - 258;
				if (num9 > num3)
				{
					match_start = cur_match;
					num3 = num9;
					if (num9 >= num5)
					{
						break;
					}
					b = window[num2 + num3 - 1];
					b2 = window[num2 + num3];
				}
			}
			while ((cur_match = prev[cur_match & num6] & 0xFFFF) > num4 && --num != 0);
			if (num3 <= lookahead)
			{
				return num3;
			}
			return lookahead;
		}

		internal int deflateInit(ZStream strm, int level, int bits)
		{
			return deflateInit2(strm, level, 8, bits, 8, 0);
		}

		internal int deflateInit(ZStream strm, int level)
		{
			return deflateInit(strm, level, 15);
		}

		internal int deflateInit2(ZStream strm, int level, int method, int windowBits, int memLevel, int strategy)
		{
			int num = 0;
			strm.msg = null;
			if (level == -1)
			{
				level = 6;
			}
			if (windowBits < 0)
			{
				num = 1;
				windowBits = -windowBits;
			}
			if (memLevel < 1 || memLevel > 9 || method != 8 || windowBits < 9 || windowBits > 15 || level < 0 || level > 9 || strategy < 0 || strategy > 2)
			{
				return -2;
			}
			strm.dstate = this;
			noheader = num;
			w_bits = windowBits;
			w_size = 1 << w_bits;
			w_mask = w_size - 1;
			hash_bits = memLevel + 7;
			hash_size = 1 << hash_bits;
			hash_mask = hash_size - 1;
			hash_shift = (hash_bits + 3 - 1) / 3;
			window = new byte[w_size * 2];
			prev = new short[w_size];
			head = new short[hash_size];
			lit_bufsize = 1 << memLevel + 6;
			pending_buf = new byte[lit_bufsize * 4];
			pending_buf_size = lit_bufsize * 4;
			d_buf = lit_bufsize;
			l_buf = 3 * lit_bufsize;
			this.level = level;
			this.strategy = strategy;
			this.method = (byte)method;
			return deflateReset(strm);
		}

		internal int deflateReset(ZStream strm)
		{
			strm.total_in = (strm.total_out = 0L);
			strm.msg = null;
			strm.data_type = 2;
			pending = 0;
			pending_out = 0;
			if (noheader < 0)
			{
				noheader = 0;
			}
			status = ((noheader != 0) ? 113 : 42);
			strm.adler = strm._adler.adler32(0L, null, 0, 0);
			last_flush = 0;
			tr_init();
			lm_init();
			return 0;
		}

		internal int deflateEnd()
		{
			if (status != 42 && status != 113 && status != 666)
			{
				return -2;
			}
			pending_buf = null;
			head = null;
			prev = null;
			window = null;
			if (status != 113)
			{
				return 0;
			}
			return -3;
		}

		internal int deflateParams(ZStream strm, int _level, int _strategy)
		{
			int result = 0;
			if (_level == -1)
			{
				_level = 6;
			}
			if (_level < 0 || _level > 9 || _strategy < 0 || _strategy > 2)
			{
				return -2;
			}
			if (config_table[level].func != config_table[_level].func && strm.total_in != 0L)
			{
				result = strm.deflate(1);
			}
			if (level != _level)
			{
				level = _level;
				max_lazy_match = config_table[level].max_lazy;
				good_match = config_table[level].good_length;
				nice_match = config_table[level].nice_length;
				max_chain_length = config_table[level].max_chain;
			}
			strategy = _strategy;
			return result;
		}

		internal int deflateSetDictionary(ZStream strm, byte[] dictionary, int dictLength)
		{
			int num = dictLength;
			int sourceIndex = 0;
			if (dictionary == null || status != 42)
			{
				return -2;
			}
			strm.adler = strm._adler.adler32(strm.adler, dictionary, 0, dictLength);
			if (num < 3)
			{
				return 0;
			}
			if (num > w_size - MIN_LOOKAHEAD)
			{
				num = w_size - MIN_LOOKAHEAD;
				sourceIndex = dictLength - num;
			}
			Array.Copy(dictionary, sourceIndex, window, 0, num);
			strstart = num;
			block_start = num;
			ins_h = window[0] & 0xFF;
			ins_h = ((ins_h << hash_shift) ^ (window[1] & 0xFF)) & hash_mask;
			for (int i = 0; i <= num - 3; i++)
			{
				ins_h = ((ins_h << hash_shift) ^ (window[i + 2] & 0xFF)) & hash_mask;
				prev[i & w_mask] = head[ins_h];
				head[ins_h] = (short)i;
			}
			return 0;
		}

		internal int deflate(ZStream strm, int flush)
		{
			if (flush > 4 || flush < 0)
			{
				return -2;
			}
			if (strm.next_out == null || (strm.next_in == null && strm.avail_in != 0) || (status == 666 && flush != 4))
			{
				strm.msg = z_errmsg[4];
				return -2;
			}
			if (strm.avail_out == 0)
			{
				strm.msg = z_errmsg[7];
				return -5;
			}
			this.strm = strm;
			int num = last_flush;
			last_flush = flush;
			if (status == 42)
			{
				int num2 = 8 + (w_bits - 8 << 4) << 8;
				int num3 = ((level - 1) & 0xFF) >> 1;
				if (num3 > 3)
				{
					num3 = 3;
				}
				num2 |= num3 << 6;
				if (strstart != 0)
				{
					num2 |= 0x20;
				}
				num2 += 31 - num2 % 31;
				status = 113;
				putShortMSB(num2);
				if (strstart != 0)
				{
					putShortMSB((int)SupportClass.URShift(strm.adler, 16));
					putShortMSB((int)(strm.adler & 0xFFFF));
				}
				strm.adler = strm._adler.adler32(0L, null, 0, 0);
			}
			if (pending != 0)
			{
				strm.flush_pending();
				if (strm.avail_out == 0)
				{
					last_flush = -1;
					return 0;
				}
			}
			else if (strm.avail_in == 0 && flush <= num && flush != 4)
			{
				strm.msg = z_errmsg[7];
				return -5;
			}
			if (status == 666 && strm.avail_in != 0)
			{
				strm.msg = z_errmsg[7];
				return -5;
			}
			if (strm.avail_in != 0 || lookahead != 0 || (flush != 0 && status != 666))
			{
				int num4 = -1;
				switch (config_table[level].func)
				{
				case 0:
					num4 = deflate_stored(flush);
					break;
				case 1:
					num4 = deflate_fast(flush);
					break;
				case 2:
					num4 = deflate_slow(flush);
					break;
				}
				if (num4 == 2 || num4 == 3)
				{
					status = 666;
				}
				switch (num4)
				{
				case 0:
				case 2:
					if (strm.avail_out == 0)
					{
						last_flush = -1;
					}
					return 0;
				case 1:
					if (flush == 1)
					{
						_tr_align();
					}
					else
					{
						_tr_stored_block(0, 0, eof: false);
						if (flush == 3)
						{
							for (int i = 0; i < hash_size; i++)
							{
								head[i] = 0;
							}
						}
					}
					strm.flush_pending();
					if (strm.avail_out == 0)
					{
						last_flush = -1;
						return 0;
					}
					break;
				}
			}
			if (flush != 4)
			{
				return 0;
			}
			if (noheader != 0)
			{
				return 1;
			}
			putShortMSB((int)SupportClass.URShift(strm.adler, 16));
			putShortMSB((int)(strm.adler & 0xFFFF));
			strm.flush_pending();
			noheader = -1;
			if (pending == 0)
			{
				return 1;
			}
			return 0;
		}

		static Deflate()
		{
			z_errmsg = new string[10] { "need dictionary", "stream end", "", "file error", "stream error", "data error", "insufficient memory", "buffer error", "incompatible version", "" };
			MIN_LOOKAHEAD = 262;
			L_CODES = 286;
			HEAP_SIZE = 2 * L_CODES + 1;
			config_table = new Config[10];
			config_table[0] = new Config(0, 0, 0, 0, 0);
			config_table[1] = new Config(4, 4, 8, 4, 1);
			config_table[2] = new Config(4, 5, 16, 8, 1);
			config_table[3] = new Config(4, 6, 32, 32, 1);
			config_table[4] = new Config(4, 4, 16, 16, 2);
			config_table[5] = new Config(8, 16, 32, 32, 2);
			config_table[6] = new Config(8, 16, 128, 128, 2);
			config_table[7] = new Config(8, 32, 128, 256, 2);
			config_table[8] = new Config(32, 128, 258, 1024, 2);
			config_table[9] = new Config(32, 258, 258, 4096, 2);
		}
	}
	internal sealed class InfBlocks
	{
		private const int MANY = 1440;

		private static readonly int[] inflate_mask = new int[17]
		{
			0, 1, 3, 7, 15, 31, 63, 127, 255, 511,
			1023, 2047, 4095, 8191, 16383, 32767, 65535
		};

		internal static readonly int[] border = new int[19]
		{
			16, 17, 18, 0, 8, 7, 9, 6, 10, 5,
			11, 4, 12, 3, 13, 2, 14, 1, 15
		};

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		private const int TYPE = 0;

		private const int LENS = 1;

		private const int STORED = 2;

		private const int TABLE = 3;

		private const int BTREE = 4;

		private const int DTREE = 5;

		private const int CODES = 6;

		private const int DRY = 7;

		private const int DONE = 8;

		private const int BAD = 9;

		internal int mode;

		internal int left;

		internal int table;

		internal int index;

		internal int[] blens;

		internal int[] bb = new int[1];

		internal int[] tb = new int[1];

		internal InfCodes codes;

		internal int last;

		internal int bitk;

		internal int bitb;

		internal int[] hufts;

		internal byte[] window;

		internal int end;

		internal int read;

		internal int write;

		internal object checkfn;

		internal long check;

		internal InfBlocks(ZStream z, object checkfn, int w)
		{
			hufts = new int[4320];
			window = new byte[w];
			end = w;
			this.checkfn = checkfn;
			mode = 0;
			reset(z, null);
		}

		internal void reset(ZStream z, long[] c)
		{
			if (c != null)
			{
				c[0] = check;
			}
			if (mode == 4 || mode == 5)
			{
				blens = null;
			}
			if (mode == 6)
			{
				codes.free(z);
			}
			mode = 0;
			bitk = 0;
			bitb = 0;
			read = (write = 0);
			if (checkfn != null)
			{
				z.adler = (check = z._adler.adler32(0L, null, 0, 0));
			}
		}

		internal int proc(ZStream z, int r)
		{
			int num = z.next_in_index;
			int num2 = z.avail_in;
			int num3 = bitb;
			int i = bitk;
			int num4 = write;
			int num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
			while (true)
			{
				switch (mode)
				{
				case 0:
				{
					for (; i < 3; i += 8)
					{
						if (num2 != 0)
						{
							r = 0;
							num2--;
							num3 |= (z.next_in[num++] & 0xFF) << i;
							continue;
						}
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					int num6 = num3 & 7;
					last = num6 & 1;
					switch (SupportClass.URShift(num6, 1))
					{
					case 0:
						num3 = SupportClass.URShift(num3, 3);
						i -= 3;
						num6 = i & 7;
						num3 = SupportClass.URShift(num3, num6);
						i -= num6;
						mode = 1;
						break;
					case 1:
					{
						int[] array5 = new int[1];
						int[] array6 = new int[1];
						int[][] array7 = new int[1][];
						int[][] array8 = new int[1][];
						InfTree.inflate_trees_fixed(array5, array6, array7, array8, z);
						codes = new InfCodes(array5[0], array6[0], array7[0], array8[0], z);
						num3 = SupportClass.URShift(num3, 3);
						i -= 3;
						mode = 6;
						break;
					}
					case 2:
						num3 = SupportClass.URShift(num3, 3);
						i -= 3;
						mode = 3;
						break;
					case 3:
						num3 = SupportClass.URShift(num3, 3);
						i -= 3;
						mode = 9;
						z.msg = "invalid block type";
						r = -3;
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					break;
				}
				case 1:
					for (; i < 32; i += 8)
					{
						if (num2 != 0)
						{
							r = 0;
							num2--;
							num3 |= (z.next_in[num++] & 0xFF) << i;
							continue;
						}
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					if ((SupportClass.URShift(~num3, 16) & 0xFFFF) != (num3 & 0xFFFF))
					{
						mode = 9;
						z.msg = "invalid stored block lengths";
						r = -3;
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					left = num3 & 0xFFFF;
					num3 = (i = 0);
					mode = ((left != 0) ? 2 : ((last != 0) ? 7 : 0));
					break;
				case 2:
				{
					if (num2 == 0)
					{
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					if (num5 == 0)
					{
						if (num4 == end && read != 0)
						{
							num4 = 0;
							num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
						}
						if (num5 == 0)
						{
							write = num4;
							r = inflate_flush(z, r);
							num4 = write;
							num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
							if (num4 == end && read != 0)
							{
								num4 = 0;
								num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
							}
							if (num5 == 0)
							{
								bitb = num3;
								bitk = i;
								z.avail_in = num2;
								z.total_in += num - z.next_in_index;
								z.next_in_index = num;
								write = num4;
								return inflate_flush(z, r);
							}
						}
					}
					r = 0;
					int num6 = left;
					if (num6 > num2)
					{
						num6 = num2;
					}
					if (num6 > num5)
					{
						num6 = num5;
					}
					Array.Copy(z.next_in, num, window, num4, num6);
					num += num6;
					num2 -= num6;
					num4 += num6;
					num5 -= num6;
					if ((left -= num6) == 0)
					{
						mode = ((last != 0) ? 7 : 0);
					}
					break;
				}
				case 3:
				{
					for (; i < 14; i += 8)
					{
						if (num2 != 0)
						{
							r = 0;
							num2--;
							num3 |= (z.next_in[num++] & 0xFF) << i;
							continue;
						}
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					int num6 = (table = num3 & 0x3FFF);
					if ((num6 & 0x1F) > 29 || ((num6 >> 5) & 0x1F) > 29)
					{
						mode = 9;
						z.msg = "too many length or distance symbols";
						r = -3;
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					num6 = 258 + (num6 & 0x1F) + ((num6 >> 5) & 0x1F);
					blens = new int[num6];
					num3 = SupportClass.URShift(num3, 14);
					i -= 14;
					index = 0;
					mode = 4;
					goto case 4;
				}
				case 4:
				{
					while (index < 4 + SupportClass.URShift(table, 10))
					{
						for (; i < 3; i += 8)
						{
							if (num2 != 0)
							{
								r = 0;
								num2--;
								num3 |= (z.next_in[num++] & 0xFF) << i;
								continue;
							}
							bitb = num3;
							bitk = i;
							z.avail_in = num2;
							z.total_in += num - z.next_in_index;
							z.next_in_index = num;
							write = num4;
							return inflate_flush(z, r);
						}
						blens[border[index++]] = num3 & 7;
						num3 = SupportClass.URShift(num3, 3);
						i -= 3;
					}
					while (index < 19)
					{
						blens[border[index++]] = 0;
					}
					bb[0] = 7;
					int num6 = InfTree.inflate_trees_bits(blens, bb, tb, hufts, z);
					if (num6 != 0)
					{
						r = num6;
						if (r == -3)
						{
							blens = null;
							mode = 9;
						}
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					index = 0;
					mode = 5;
					goto case 5;
				}
				case 5:
				{
					int num6;
					while (true)
					{
						num6 = table;
						if (index >= 258 + (num6 & 0x1F) + ((num6 >> 5) & 0x1F))
						{
							break;
						}
						for (num6 = bb[0]; i < num6; i += 8)
						{
							if (num2 != 0)
							{
								r = 0;
								num2--;
								num3 |= (z.next_in[num++] & 0xFF) << i;
								continue;
							}
							bitb = num3;
							bitk = i;
							z.avail_in = num2;
							z.total_in += num - z.next_in_index;
							z.next_in_index = num;
							write = num4;
							return inflate_flush(z, r);
						}
						_ = tb[0];
						_ = -1;
						num6 = hufts[(tb[0] + (num3 & inflate_mask[num6])) * 3 + 1];
						int num7 = hufts[(tb[0] + (num3 & inflate_mask[num6])) * 3 + 2];
						if (num7 < 16)
						{
							num3 = SupportClass.URShift(num3, num6);
							i -= num6;
							blens[index++] = num7;
							continue;
						}
						int num8 = ((num7 == 18) ? 7 : (num7 - 14));
						int num9 = ((num7 == 18) ? 11 : 3);
						for (; i < num6 + num8; i += 8)
						{
							if (num2 != 0)
							{
								r = 0;
								num2--;
								num3 |= (z.next_in[num++] & 0xFF) << i;
								continue;
							}
							bitb = num3;
							bitk = i;
							z.avail_in = num2;
							z.total_in += num - z.next_in_index;
							z.next_in_index = num;
							write = num4;
							return inflate_flush(z, r);
						}
						num3 = SupportClass.URShift(num3, num6);
						i -= num6;
						num9 += num3 & inflate_mask[num8];
						num3 = SupportClass.URShift(num3, num8);
						i -= num8;
						num8 = index;
						num6 = table;
						if (num8 + num9 > 258 + (num6 & 0x1F) + ((num6 >> 5) & 0x1F) || (num7 == 16 && num8 < 1))
						{
							blens = null;
							mode = 9;
							z.msg = "invalid bit length repeat";
							r = -3;
							bitb = num3;
							bitk = i;
							z.avail_in = num2;
							z.total_in += num - z.next_in_index;
							z.next_in_index = num;
							write = num4;
							return inflate_flush(z, r);
						}
						num7 = ((num7 == 16) ? blens[num8 - 1] : 0);
						do
						{
							blens[num8++] = num7;
						}
						while (--num9 != 0);
						index = num8;
					}
					tb[0] = -1;
					int[] array = new int[1];
					int[] array2 = new int[1];
					int[] array3 = new int[1];
					int[] array4 = new int[1];
					array[0] = 9;
					array2[0] = 6;
					num6 = table;
					num6 = InfTree.inflate_trees_dynamic(257 + (num6 & 0x1F), 1 + ((num6 >> 5) & 0x1F), blens, array, array2, array3, array4, hufts, z);
					if (num6 != 0)
					{
						if (num6 == -3)
						{
							blens = null;
							mode = 9;
						}
						r = num6;
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					codes = new InfCodes(array[0], array2[0], hufts, array3[0], hufts, array4[0], z);
					blens = null;
					mode = 6;
					goto case 6;
				}
				case 6:
					bitb = num3;
					bitk = i;
					z.avail_in = num2;
					z.total_in += num - z.next_in_index;
					z.next_in_index = num;
					write = num4;
					if ((r = codes.proc(this, z, r)) != 1)
					{
						return inflate_flush(z, r);
					}
					r = 0;
					codes.free(z);
					num = z.next_in_index;
					num2 = z.avail_in;
					num3 = bitb;
					i = bitk;
					num4 = write;
					num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
					if (last == 0)
					{
						mode = 0;
						break;
					}
					mode = 7;
					goto case 7;
				case 7:
					write = num4;
					r = inflate_flush(z, r);
					num4 = write;
					num5 = ((num4 < read) ? (read - num4 - 1) : (end - num4));
					if (read != write)
					{
						bitb = num3;
						bitk = i;
						z.avail_in = num2;
						z.total_in += num - z.next_in_index;
						z.next_in_index = num;
						write = num4;
						return inflate_flush(z, r);
					}
					mode = 8;
					goto case 8;
				case 8:
					r = 1;
					bitb = num3;
					bitk = i;
					z.avail_in = num2;
					z.total_in += num - z.next_in_index;
					z.next_in_index = num;
					write = num4;
					return inflate_flush(z, r);
				case 9:
					r = -3;
					bitb = num3;
					bitk = i;
					z.avail_in = num2;
					z.total_in += num - z.next_in_index;
					z.next_in_index = num;
					write = num4;
					return inflate_flush(z, r);
				default:
					r = -2;
					bitb = num3;
					bitk = i;
					z.avail_in = num2;
					z.total_in += num - z.next_in_index;
					z.next_in_index = num;
					write = num4;
					return inflate_flush(z, r);
				}
			}
		}

		internal void free(ZStream z)
		{
			reset(z, null);
			window = null;
			hufts = null;
		}

		internal void set_dictionary(byte[] d, int start, int n)
		{
			Array.Copy(d, start, window, 0, n);
			read = (write = n);
		}

		internal int sync_point()
		{
			if (mode != 1)
			{
				return 0;
			}
			return 1;
		}

		internal int inflate_flush(ZStream z, int r)
		{
			int next_out_index = z.next_out_index;
			int num = read;
			int num2 = ((num <= write) ? write : end) - num;
			if (num2 > z.avail_out)
			{
				num2 = z.avail_out;
			}
			if (num2 != 0 && r == -5)
			{
				r = 0;
			}
			z.avail_out -= num2;
			z.total_out += num2;
			if (checkfn != null)
			{
				z.adler = (check = z._adler.adler32(check, window, num, num2));
			}
			Array.Copy(window, num, z.next_out, next_out_index, num2);
			next_out_index += num2;
			num += num2;
			if (num == end)
			{
				num = 0;
				if (write == end)
				{
					write = 0;
				}
				num2 = write - num;
				if (num2 > z.avail_out)
				{
					num2 = z.avail_out;
				}
				if (num2 != 0 && r == -5)
				{
					r = 0;
				}
				z.avail_out -= num2;
				z.total_out += num2;
				if (checkfn != null)
				{
					z.adler = (check = z._adler.adler32(check, window, num, num2));
				}
				Array.Copy(window, num, z.next_out, next_out_index, num2);
				next_out_index += num2;
				num += num2;
			}
			z.next_out_index = next_out_index;
			read = num;
			return r;
		}
	}
	internal sealed class InfCodes
	{
		private static readonly int[] inflate_mask = new int[17]
		{
			0, 1, 3, 7, 15, 31, 63, 127, 255, 511,
			1023, 2047, 4095, 8191, 16383, 32767, 65535
		};

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		private const int START = 0;

		private const int LEN = 1;

		private const int LENEXT = 2;

		private const int DIST = 3;

		private const int DISTEXT = 4;

		private const int COPY = 5;

		private const int LIT = 6;

		private const int WASH = 7;

		private const int END = 8;

		private const int BADCODE = 9;

		internal int mode;

		internal int len;

		internal int[] tree;

		internal int tree_index;

		internal int need;

		internal int lit;

		internal int get_Renamed;

		internal int dist;

		internal byte lbits;

		internal byte dbits;

		internal int[] ltree;

		internal int ltree_index;

		internal int[] dtree;

		internal int dtree_index;

		internal InfCodes(int bl, int bd, int[] tl, int tl_index, int[] td, int td_index, ZStream z)
		{
			mode = 0;
			lbits = (byte)bl;
			dbits = (byte)bd;
			ltree = tl;
			ltree_index = tl_index;
			dtree = td;
			dtree_index = td_index;
		}

		internal InfCodes(int bl, int bd, int[] tl, int[] td, ZStream z)
		{
			mode = 0;
			lbits = (byte)bl;
			dbits = (byte)bd;
			ltree = tl;
			ltree_index = 0;
			dtree = td;
			dtree_index = 0;
		}

		internal int proc(InfBlocks s, ZStream z, int r)
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			num3 = z.next_in_index;
			int num4 = z.avail_in;
			num = s.bitb;
			num2 = s.bitk;
			int num5 = s.write;
			int num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
			while (true)
			{
				switch (mode)
				{
				case 0:
					if (num6 >= 258 && num4 >= 10)
					{
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						r = inflate_fast(lbits, dbits, ltree, ltree_index, dtree, dtree_index, s, z);
						num3 = z.next_in_index;
						num4 = z.avail_in;
						num = s.bitb;
						num2 = s.bitk;
						num5 = s.write;
						num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
						if (r != 0)
						{
							mode = ((r == 1) ? 7 : 9);
							break;
						}
					}
					need = lbits;
					tree = ltree;
					tree_index = ltree_index;
					mode = 1;
					goto case 1;
				case 1:
				{
					int num7;
					for (num7 = need; num2 < num7; num2 += 8)
					{
						if (num4 != 0)
						{
							r = 0;
							num4--;
							num |= (z.next_in[num3++] & 0xFF) << num2;
							continue;
						}
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						return s.inflate_flush(z, r);
					}
					int num8 = (tree_index + (num & inflate_mask[num7])) * 3;
					num = SupportClass.URShift(num, tree[num8 + 1]);
					num2 -= tree[num8 + 1];
					int num9 = tree[num8];
					if (num9 == 0)
					{
						lit = tree[num8 + 2];
						mode = 6;
						break;
					}
					if ((num9 & 0x10) != 0)
					{
						get_Renamed = num9 & 0xF;
						len = tree[num8 + 2];
						mode = 2;
						break;
					}
					if ((num9 & 0x40) == 0)
					{
						need = num9;
						tree_index = num8 / 3 + tree[num8 + 2];
						break;
					}
					if ((num9 & 0x20) != 0)
					{
						mode = 7;
						break;
					}
					mode = 9;
					z.msg = "invalid literal/length code";
					r = -3;
					s.bitb = num;
					s.bitk = num2;
					z.avail_in = num4;
					z.total_in += num3 - z.next_in_index;
					z.next_in_index = num3;
					s.write = num5;
					return s.inflate_flush(z, r);
				}
				case 2:
				{
					int num7;
					for (num7 = get_Renamed; num2 < num7; num2 += 8)
					{
						if (num4 != 0)
						{
							r = 0;
							num4--;
							num |= (z.next_in[num3++] & 0xFF) << num2;
							continue;
						}
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						return s.inflate_flush(z, r);
					}
					len += num & inflate_mask[num7];
					num >>= num7;
					num2 -= num7;
					need = dbits;
					tree = dtree;
					tree_index = dtree_index;
					mode = 3;
					goto case 3;
				}
				case 3:
				{
					int num7;
					for (num7 = need; num2 < num7; num2 += 8)
					{
						if (num4 != 0)
						{
							r = 0;
							num4--;
							num |= (z.next_in[num3++] & 0xFF) << num2;
							continue;
						}
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						return s.inflate_flush(z, r);
					}
					int num8 = (tree_index + (num & inflate_mask[num7])) * 3;
					num >>= tree[num8 + 1];
					num2 -= tree[num8 + 1];
					int num9 = tree[num8];
					if ((num9 & 0x10) != 0)
					{
						get_Renamed = num9 & 0xF;
						dist = tree[num8 + 2];
						mode = 4;
						break;
					}
					if ((num9 & 0x40) == 0)
					{
						need = num9;
						tree_index = num8 / 3 + tree[num8 + 2];
						break;
					}
					mode = 9;
					z.msg = "invalid distance code";
					r = -3;
					s.bitb = num;
					s.bitk = num2;
					z.avail_in = num4;
					z.total_in += num3 - z.next_in_index;
					z.next_in_index = num3;
					s.write = num5;
					return s.inflate_flush(z, r);
				}
				case 4:
				{
					int num7;
					for (num7 = get_Renamed; num2 < num7; num2 += 8)
					{
						if (num4 != 0)
						{
							r = 0;
							num4--;
							num |= (z.next_in[num3++] & 0xFF) << num2;
							continue;
						}
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						return s.inflate_flush(z, r);
					}
					dist += num & inflate_mask[num7];
					num >>= num7;
					num2 -= num7;
					mode = 5;
					goto case 5;
				}
				case 5:
				{
					int i;
					for (i = num5 - dist; i < 0; i += s.end)
					{
					}
					while (len != 0)
					{
						if (num6 == 0)
						{
							if (num5 == s.end && s.read != 0)
							{
								num5 = 0;
								num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
							}
							if (num6 == 0)
							{
								s.write = num5;
								r = s.inflate_flush(z, r);
								num5 = s.write;
								num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
								if (num5 == s.end && s.read != 0)
								{
									num5 = 0;
									num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
								}
								if (num6 == 0)
								{
									s.bitb = num;
									s.bitk = num2;
									z.avail_in = num4;
									z.total_in += num3 - z.next_in_index;
									z.next_in_index = num3;
									s.write = num5;
									return s.inflate_flush(z, r);
								}
							}
						}
						s.window[num5++] = s.window[i++];
						num6--;
						if (i == s.end)
						{
							i = 0;
						}
						len--;
					}
					mode = 0;
					break;
				}
				case 6:
					if (num6 == 0)
					{
						if (num5 == s.end && s.read != 0)
						{
							num5 = 0;
							num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
						}
						if (num6 == 0)
						{
							s.write = num5;
							r = s.inflate_flush(z, r);
							num5 = s.write;
							num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
							if (num5 == s.end && s.read != 0)
							{
								num5 = 0;
								num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
							}
							if (num6 == 0)
							{
								s.bitb = num;
								s.bitk = num2;
								z.avail_in = num4;
								z.total_in += num3 - z.next_in_index;
								z.next_in_index = num3;
								s.write = num5;
								return s.inflate_flush(z, r);
							}
						}
					}
					r = 0;
					s.window[num5++] = (byte)lit;
					num6--;
					mode = 0;
					break;
				case 7:
					if (num2 > 7)
					{
						num2 -= 8;
						num4++;
						num3--;
					}
					s.write = num5;
					r = s.inflate_flush(z, r);
					num5 = s.write;
					num6 = ((num5 < s.read) ? (s.read - num5 - 1) : (s.end - num5));
					if (s.read != s.write)
					{
						s.bitb = num;
						s.bitk = num2;
						z.avail_in = num4;
						z.total_in += num3 - z.next_in_index;
						z.next_in_index = num3;
						s.write = num5;
						return s.inflate_flush(z, r);
					}
					mode = 8;
					goto case 8;
				case 8:
					r = 1;
					s.bitb = num;
					s.bitk = num2;
					z.avail_in = num4;
					z.total_in += num3 - z.next_in_index;
					z.next_in_index = num3;
					s.write = num5;
					return s.inflate_flush(z, r);
				case 9:
					r = -3;
					s.bitb = num;
					s.bitk = num2;
					z.avail_in = num4;
					z.total_in += num3 - z.next_in_index;
					z.next_in_index = num3;
					s.write = num5;
					return s.inflate_flush(z, r);
				default:
					r = -2;
					s.bitb = num;
					s.bitk = num2;
					z.avail_in = num4;
					z.total_in += num3 - z.next_in_index;
					z.next_in_index = num3;
					s.write = num5;
					return s.inflate_flush(z, r);
				}
			}
		}

		internal void free(ZStream z)
		{
		}

		internal int inflate_fast(int bl, int bd, int[] tl, int tl_index, int[] td, int td_index, InfBlocks s, ZStream z)
		{
			int next_in_index = z.next_in_index;
			int num = z.avail_in;
			int num2 = s.bitb;
			int num3 = s.bitk;
			int num4 = s.write;
			int num5 = ((num4 < s.read) ? (s.read - num4 - 1) : (s.end - num4));
			int num6 = inflate_mask[bl];
			int num7 = inflate_mask[bd];
			int num11;
			while (true)
			{
				if (num3 < 20)
				{
					num--;
					num2 |= (z.next_in[next_in_index++] & 0xFF) << num3;
					num3 += 8;
					continue;
				}
				int num8 = num2 & num6;
				int[] array = tl;
				int num9 = tl_index;
				int num10;
				if ((num10 = array[(num9 + num8) * 3]) == 0)
				{
					num2 >>= array[(num9 + num8) * 3 + 1];
					num3 -= array[(num9 + num8) * 3 + 1];
					s.window[num4++] = (byte)array[(num9 + num8) * 3 + 2];
					num5--;
				}
				else
				{
					while (true)
					{
						num2 >>= array[(num9 + num8) * 3 + 1];
						num3 -= array[(num9 + num8) * 3 + 1];
						if ((num10 & 0x10) != 0)
						{
							num10 &= 0xF;
							num11 = array[(num9 + num8) * 3 + 2] + (num2 & inflate_mask[num10]);
							num2 >>= num10;
							for (num3 -= num10; num3 < 15; num3 += 8)
							{
								num--;
								num2 |= (z.next_in[next_in_index++] & 0xFF) << num3;
							}
							num8 = num2 & num7;
							array = td;
							num9 = td_index;
							num10 = array[(num9 + num8) * 3];
							while (true)
							{
								num2 >>= array[(num9 + num8) * 3 + 1];
								num3 -= array[(num9 + num8) * 3 + 1];
								if ((num10 & 0x10) != 0)
								{
									break;
								}
								if ((num10 & 0x40) == 0)
								{
									num8 += array[(num9 + num8) * 3 + 2];
									num8 += num2 & inflate_mask[num10];
									num10 = array[(num9 + num8) * 3];
									continue;
								}
								z.msg = "invalid distance code";
								num11 = z.avail_in - num;
								num11 = ((num3 >> 3 < num11) ? (num3 >> 3) : num11);
								num += num11;
								next_in_index -= num11;
								num3 -= num11 << 3;
								s.bitb = num2;
								s.bitk = num3;
								z.avail_in = num;
								z.total_in += next_in_index - z.next_in_index;
								z.next_in_index = next_in_index;
								s.write = num4;
								return -3;
							}
							for (num10 &= 0xF; num3 < num10; num3 += 8)
							{
								num--;
								num2 |= (z.next_in[next_in_index++] & 0xFF) << num3;
							}
							int num12 = array[(num9 + num8) * 3 + 2] + (num2 & inflate_mask[num10]);
							num2 >>= num10;
							num3 -= num10;
							num5 -= num11;
							int num13;
							if (num4 >= num12)
							{
								num13 = num4 - num12;
								if (num4 - num13 > 0 && 2 > num4 - num13)
								{
									s.window[num4++] = s.window[num13++];
									num11--;
									s.window[num4++] = s.window[num13++];
									num11--;
								}
								else
								{
									Array.Copy(s.window, num13, s.window, num4, 2);
									num4 += 2;
									num13 += 2;
									num11 -= 2;
								}
							}
							else
							{
								num13 = num4 - num12;
								do
								{
									num13 += s.end;
								}
								while (num13 < 0);
								num10 = s.end - num13;
								if (num11 > num10)
								{
									num11 -= num10;
									if (num4 - num13 > 0 && num10 > num4 - num13)
									{
										do
										{
											s.window[num4++] = s.window[num13++];
										}
										while (--num10 != 0);
									}
									else
									{
										Array.Copy(s.window, num13, s.window, num4, num10);
										num4 += num10;
										num13 += num10;
										num10 = 0;
									}
									num13 = 0;
								}
							}
							if (num4 - num13 > 0 && num11 > num4 - num13)
							{
								do
								{
									s.window[num4++] = s.window[num13++];
								}
								while (--num11 != 0);
								break;
							}
							Array.Copy(s.window, num13, s.window, num4, num11);
							num4 += num11;
							num13 += num11;
							num11 = 0;
							break;
						}
						if ((num10 & 0x40) == 0)
						{
							num8 += array[(num9 + num8) * 3 + 2];
							num8 += num2 & inflate_mask[num10];
							if ((num10 = array[(num9 + num8) * 3]) == 0)
							{
								num2 >>= array[(num9 + num8) * 3 + 1];
								num3 -= array[(num9 + num8) * 3 + 1];
								s.window[num4++] = (byte)array[(num9 + num8) * 3 + 2];
								num5--;
								break;
							}
							continue;
						}
						if ((num10 & 0x20) != 0)
						{
							num11 = z.avail_in - num;
							num11 = ((num3 >> 3 < num11) ? (num3 >> 3) : num11);
							num += num11;
							next_in_index -= num11;
							num3 -= num11 << 3;
							s.bitb = num2;
							s.bitk = num3;
							z.avail_in = num;
							z.total_in += next_in_index - z.next_in_index;
							z.next_in_index = next_in_index;
							s.write = num4;
							return 1;
						}
						z.msg = "invalid literal/length code";
						num11 = z.avail_in - num;
						num11 = ((num3 >> 3 < num11) ? (num3 >> 3) : num11);
						num += num11;
						next_in_index -= num11;
						num3 -= num11 << 3;
						s.bitb = num2;
						s.bitk = num3;
						z.avail_in = num;
						z.total_in += next_in_index - z.next_in_index;
						z.next_in_index = next_in_index;
						s.write = num4;
						return -3;
					}
				}
				if (num5 < 258 || num < 10)
				{
					break;
				}
			}
			num11 = z.avail_in - num;
			num11 = ((num3 >> 3 < num11) ? (num3 >> 3) : num11);
			num += num11;
			next_in_index -= num11;
			num3 -= num11 << 3;
			s.bitb = num2;
			s.bitk = num3;
			z.avail_in = num;
			z.total_in += next_in_index - z.next_in_index;
			z.next_in_index = next_in_index;
			s.write = num4;
			return 0;
		}
	}
	internal sealed class Inflate
	{
		private const int MAX_WBITS = 15;

		private const int PRESET_DICT = 32;

		internal const int Z_NO_FLUSH = 0;

		internal const int Z_PARTIAL_FLUSH = 1;

		internal const int Z_SYNC_FLUSH = 2;

		internal const int Z_FULL_FLUSH = 3;

		internal const int Z_FINISH = 4;

		private const int Z_DEFLATED = 8;

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		private const int METHOD = 0;

		private const int FLAG = 1;

		private const int DICT4 = 2;

		private const int DICT3 = 3;

		private const int DICT2 = 4;

		private const int DICT1 = 5;

		private const int DICT0 = 6;

		private const int BLOCKS = 7;

		private const int CHECK4 = 8;

		private const int CHECK3 = 9;

		private const int CHECK2 = 10;

		private const int CHECK1 = 11;

		private const int DONE = 12;

		private const int BAD = 13;

		internal int mode;

		internal int method;

		internal long[] was = new long[1];

		internal long need;

		internal int marker;

		internal int nowrap;

		internal int wbits;

		internal InfBlocks blocks;

		private static byte[] mark = new byte[4]
		{
			0,
			0,
			(byte)SupportClass.Identity(255L),
			(byte)SupportClass.Identity(255L)
		};

		internal int inflateReset(ZStream z)
		{
			if (z == null || z.istate == null)
			{
				return -2;
			}
			z.total_in = (z.total_out = 0L);
			z.msg = null;
			z.istate.mode = ((z.istate.nowrap != 0) ? 7 : 0);
			z.istate.blocks.reset(z, null);
			return 0;
		}

		internal int inflateEnd(ZStream z)
		{
			if (blocks != null)
			{
				blocks.free(z);
			}
			blocks = null;
			return 0;
		}

		internal int inflateInit(ZStream z, int w)
		{
			z.msg = null;
			blocks = null;
			nowrap = 0;
			if (w < 0)
			{
				w = -w;
				nowrap = 1;
			}
			if (w < 8 || w > 15)
			{
				inflateEnd(z);
				return -2;
			}
			wbits = w;
			z.istate.blocks = new InfBlocks(z, (z.istate.nowrap != 0) ? null : this, 1 << w);
			inflateReset(z);
			return 0;
		}

		internal int inflate(ZStream z, int f)
		{
			if (z == null || z.istate == null || z.next_in == null)
			{
				return -2;
			}
			f = ((f == 4) ? (-5) : 0);
			int num = -5;
			while (true)
			{
				switch (z.istate.mode)
				{
				case 0:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					if (((z.istate.method = z.next_in[z.next_in_index++]) & 0xF) != 8)
					{
						z.istate.mode = 13;
						z.msg = "unknown compression method";
						z.istate.marker = 5;
						break;
					}
					if ((z.istate.method >> 4) + 8 > z.istate.wbits)
					{
						z.istate.mode = 13;
						z.msg = "invalid window size";
						z.istate.marker = 5;
						break;
					}
					z.istate.mode = 1;
					goto case 1;
				case 1:
				{
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					int num2 = z.next_in[z.next_in_index++] & 0xFF;
					if (((z.istate.method << 8) + num2) % 31 != 0)
					{
						z.istate.mode = 13;
						z.msg = "incorrect header check";
						z.istate.marker = 5;
						break;
					}
					if ((num2 & 0x20) == 0)
					{
						z.istate.mode = 7;
						break;
					}
					z.istate.mode = 2;
					goto case 2;
				}
				case 2:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need = ((z.next_in[z.next_in_index++] & 0xFF) << 24) & -16777216;
					z.istate.mode = 3;
					goto case 3;
				case 3:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)((z.next_in[z.next_in_index++] & 0xFF) << 16) & 0xFF0000uL);
					z.istate.mode = 4;
					goto case 4;
				case 4:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)((z.next_in[z.next_in_index++] & 0xFF) << 8) & 0xFF00uL);
					z.istate.mode = 5;
					goto case 5;
				case 5:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)z.next_in[z.next_in_index++] & 0xFFuL);
					z.adler = z.istate.need;
					z.istate.mode = 6;
					return 2;
				case 6:
					z.istate.mode = 13;
					z.msg = "need dictionary";
					z.istate.marker = 0;
					return -2;
				case 7:
					num = z.istate.blocks.proc(z, num);
					switch (num)
					{
					case -3:
						z.istate.mode = 13;
						z.istate.marker = 0;
						goto end_IL_0031;
					case 0:
						num = f;
						break;
					}
					if (num != 1)
					{
						return num;
					}
					num = f;
					z.istate.blocks.reset(z, z.istate.was);
					if (z.istate.nowrap != 0)
					{
						z.istate.mode = 12;
						break;
					}
					z.istate.mode = 8;
					goto case 8;
				case 8:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need = ((z.next_in[z.next_in_index++] & 0xFF) << 24) & -16777216;
					z.istate.mode = 9;
					goto case 9;
				case 9:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)((z.next_in[z.next_in_index++] & 0xFF) << 16) & 0xFF0000uL);
					z.istate.mode = 10;
					goto case 10;
				case 10:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)((z.next_in[z.next_in_index++] & 0xFF) << 8) & 0xFF00uL);
					z.istate.mode = 11;
					goto case 11;
				case 11:
					if (z.avail_in == 0)
					{
						return num;
					}
					num = f;
					z.avail_in--;
					z.total_in++;
					z.istate.need += (long)((ulong)z.next_in[z.next_in_index++] & 0xFFuL);
					if ((int)z.istate.was[0] != (int)z.istate.need)
					{
						z.istate.mode = 13;
						z.msg = "incorrect data check";
						z.istate.marker = 5;
						break;
					}
					z.istate.mode = 12;
					goto case 12;
				case 12:
					return 1;
				case 13:
					return -3;
				default:
					{
						return -2;
					}
					end_IL_0031:
					break;
				}
			}
		}

		internal int inflateSetDictionary(ZStream z, byte[] dictionary, int dictLength)
		{
			int start = 0;
			int num = dictLength;
			if (z == null || z.istate == null || z.istate.mode != 6)
			{
				return -2;
			}
			if (z._adler.adler32(1L, dictionary, 0, dictLength) != z.adler)
			{
				return -3;
			}
			z.adler = z._adler.adler32(0L, null, 0, 0);
			if (num >= 1 << z.istate.wbits)
			{
				num = (1 << z.istate.wbits) - 1;
				start = dictLength - num;
			}
			z.istate.blocks.set_dictionary(dictionary, start, num);
			z.istate.mode = 7;
			return 0;
		}

		internal int inflateSync(ZStream z)
		{
			if (z == null || z.istate == null)
			{
				return -2;
			}
			if (z.istate.mode != 13)
			{
				z.istate.mode = 13;
				z.istate.marker = 0;
			}
			int num;
			if ((num = z.avail_in) == 0)
			{
				return -5;
			}
			int num2 = z.next_in_index;
			int num3 = z.istate.marker;
			while (num != 0 && num3 < 4)
			{
				num3 = ((z.next_in[num2] != mark[num3]) ? ((z.next_in[num2] == 0) ? (4 - num3) : 0) : (num3 + 1));
				num2++;
				num--;
			}
			z.total_in += num2 - z.next_in_index;
			z.next_in_index = num2;
			z.avail_in = num;
			z.istate.marker = num3;
			if (num3 != 4)
			{
				return -3;
			}
			long total_in = z.total_in;
			long total_out = z.total_out;
			inflateReset(z);
			z.total_in = total_in;
			z.total_out = total_out;
			z.istate.mode = 7;
			return 0;
		}

		internal int inflateSyncPoint(ZStream z)
		{
			if (z == null || z.istate == null || z.istate.blocks == null)
			{
				return -2;
			}
			return z.istate.blocks.sync_point();
		}
	}
	internal sealed class InfTree
	{
		private const int MANY = 1440;

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		internal const int fixed_bl = 9;

		internal const int fixed_bd = 5;

		internal static readonly int[] fixed_tl = new int[1536]
		{
			96, 7, 256, 0, 8, 80, 0, 8, 16, 84,
			8, 115, 82, 7, 31, 0, 8, 112, 0, 8,
			48, 0, 9, 192, 80, 7, 10, 0, 8, 96,
			0, 8, 32, 0, 9, 160, 0, 8, 0, 0,
			8, 128, 0, 8, 64, 0, 9, 224, 80, 7,
			6, 0, 8, 88, 0, 8, 24, 0, 9, 144,
			83, 7, 59, 0, 8, 120, 0, 8, 56, 0,
			9, 208, 81, 7, 17, 0, 8, 104, 0, 8,
			40, 0, 9, 176, 0, 8, 8, 0, 8, 136,
			0, 8, 72, 0, 9, 240, 80, 7, 4, 0,
			8, 84, 0, 8, 20, 85, 8, 227, 83, 7,
			43, 0, 8, 116, 0, 8, 52, 0, 9, 200,
			81, 7, 13, 0, 8, 100, 0, 8, 36, 0,
			9, 168, 0, 8, 4, 0, 8, 132, 0, 8,
			68, 0, 9, 232, 80, 7, 8, 0, 8, 92,
			0, 8, 28, 0, 9, 152, 84, 7, 83, 0,
			8, 124, 0, 8, 60, 0, 9, 216, 82, 7,
			23, 0, 8, 108, 0, 8, 44, 0, 9, 184,
			0, 8, 12, 0, 8, 140, 0, 8, 76, 0,
			9, 248, 80, 7, 3, 0, 8, 82, 0, 8,
			18, 85, 8, 163, 83, 7, 35, 0, 8, 114,
			0, 8, 50, 0, 9, 196, 81, 7, 11, 0,
			8, 98, 0, 8, 34, 0, 9, 164, 0, 8,
			2, 0, 8, 130, 0, 8, 66, 0, 9, 228,
			80, 7, 7, 0, 8, 90, 0, 8, 26, 0,
			9, 148, 84, 7, 67, 0, 8, 122, 0, 8,
			58, 0, 9, 212, 82, 7, 19, 0, 8, 106,
			0, 8, 42, 0, 9, 180, 0, 8, 10, 0,
			8, 138, 0, 8, 74, 0, 9, 244, 80, 7,
			5, 0, 8, 86, 0, 8, 22, 192, 8, 0,
			83, 7, 51, 0, 8, 118, 0, 8, 54, 0,
			9, 204, 81, 7, 15, 0, 8, 102, 0, 8,
			38, 0, 9, 172, 0, 8, 6, 0, 8, 134,
			0, 8, 70, 0, 9, 236, 80, 7, 9, 0,
			8, 94, 0, 8, 30, 0, 9, 156, 84, 7,
			99, 0, 8, 126, 0, 8, 62, 0, 9, 220,
			82, 7, 27, 0, 8, 110, 0, 8, 46, 0,
			9, 188, 0, 8, 14, 0, 8, 142, 0, 8,
			78, 0, 9, 252, 96, 7, 256, 0, 8, 81,
			0, 8, 17, 85, 8, 131, 82, 7, 31, 0,
			8, 113, 0, 8, 49, 0, 9, 194, 80, 7,
			10, 0, 8, 97, 0, 8, 33, 0, 9, 162,
			0, 8, 1, 0, 8, 129, 0, 8, 65, 0,
			9, 226, 80, 7, 6, 0, 8, 89, 0, 8,
			25, 0, 9, 146, 83, 7, 59, 0, 8, 121,
			0, 8, 57, 0, 9, 210, 81, 7, 17, 0,
			8, 105, 0, 8, 41, 0, 9, 178, 0, 8,
			9, 0, 8, 137, 0, 8, 73, 0, 9, 242,
			80, 7, 4, 0, 8, 85, 0, 8, 21, 80,
			8, 258, 83, 7, 43, 0, 8, 117, 0, 8,
			53, 0, 9, 202, 81, 7, 13, 0, 8, 101,
			0, 8, 37, 0, 9, 170, 0, 8, 5, 0,
			8, 133, 0, 8, 69, 0, 9, 234, 80, 7,
			8, 0, 8, 93, 0, 8, 29, 0, 9, 154,
			84, 7, 83, 0, 8, 125, 0, 8, 61, 0,
			9, 218, 82, 7, 23, 0, 8, 109, 0, 8,
			45, 0, 9, 186, 0, 8, 13, 0, 8, 141,
			0, 8, 77, 0, 9, 250, 80, 7, 3, 0,
			8, 83, 0, 8, 19, 85, 8, 195, 83, 7,
			35, 0, 8, 115, 0, 8, 51, 0, 9, 198,
			81, 7, 11, 0, 8, 99, 0, 8, 35, 0,
			9, 166, 0, 8, 3, 0, 8, 131, 0, 8,
			67, 0, 9, 230, 80, 7, 7, 0, 8, 91,
			0, 8, 27, 0, 9, 150, 84, 7, 67, 0,
			8, 123, 0, 8, 59, 0, 9, 214, 82, 7,
			19, 0, 8, 107, 0, 8, 43, 0, 9, 182,
			0, 8, 11, 0, 8, 139, 0, 8, 75, 0,
			9, 246, 80, 7, 5, 0, 8, 87, 0, 8,
			23, 192, 8, 0, 83, 7, 51, 0, 8, 119,
			0, 8, 55, 0, 9, 206, 81, 7, 15, 0,
			8, 103, 0, 8, 39, 0, 9, 174, 0, 8,
			7, 0, 8, 135, 0, 8, 71, 0, 9, 238,
			80, 7, 9, 0, 8, 95, 0, 8, 31, 0,
			9, 158, 84, 7, 99, 0, 8, 127, 0, 8,
			63, 0, 9, 222, 82, 7, 27, 0, 8, 111,
			0, 8, 47, 0, 9, 190, 0, 8, 15, 0,
			8, 143, 0, 8, 79, 0, 9, 254, 96, 7,
			256, 0, 8, 80, 0, 8, 16, 84, 8, 115,
			82, 7, 31, 0, 8, 112, 0, 8, 48, 0,
			9, 193, 80, 7, 10, 0, 8, 96, 0, 8,
			32, 0, 9, 161, 0, 8, 0, 0, 8, 128,
			0, 8, 64, 0, 9, 225, 80, 7, 6, 0,
			8, 88, 0, 8, 24, 0, 9, 145, 83, 7,
			59, 0, 8, 120, 0, 8, 56, 0, 9, 209,
			81, 7, 17, 0, 8, 104, 0, 8, 40, 0,
			9, 177, 0, 8, 8, 0, 8, 136, 0, 8,
			72, 0, 9, 241, 80, 7, 4, 0, 8, 84,
			0, 8, 20, 85, 8, 227, 83, 7, 43, 0,
			8, 116, 0, 8, 52, 0, 9, 201, 81, 7,
			13, 0, 8, 100, 0, 8, 36, 0, 9, 169,
			0, 8, 4, 0, 8, 132, 0, 8, 68, 0,
			9, 233, 80, 7, 8, 0, 8, 92, 0, 8,
			28, 0, 9, 153, 84, 7, 83, 0, 8, 124,
			0, 8, 60, 0, 9, 217, 82, 7, 23, 0,
			8, 108, 0, 8, 44, 0, 9, 185, 0, 8,
			12, 0, 8, 140, 0, 8, 76, 0, 9, 249,
			80, 7, 3, 0, 8, 82, 0, 8, 18, 85,
			8, 163, 83, 7, 35, 0, 8, 114, 0, 8,
			50, 0, 9, 197, 81, 7, 11, 0, 8, 98,
			0, 8, 34, 0, 9, 165, 0, 8, 2, 0,
			8, 130, 0, 8, 66, 0, 9, 229, 80, 7,
			7, 0, 8, 90, 0, 8, 26, 0, 9, 149,
			84, 7, 67, 0, 8, 122, 0, 8, 58, 0,
			9, 213, 82, 7, 19, 0, 8, 106, 0, 8,
			42, 0, 9, 181, 0, 8, 10, 0, 8, 138,
			0, 8, 74, 0, 9, 245, 80, 7, 5, 0,
			8, 86, 0, 8, 22, 192, 8, 0, 83, 7,
			51, 0, 8, 118, 0, 8, 54, 0, 9, 205,
			81, 7, 15, 0, 8, 102, 0, 8, 38, 0,
			9, 173, 0, 8, 6, 0, 8, 134, 0, 8,
			70, 0, 9, 237, 80, 7, 9, 0, 8, 94,
			0, 8, 30, 0, 9, 157, 84, 7, 99, 0,
			8, 126, 0, 8, 62, 0, 9, 221, 82, 7,
			27, 0, 8, 110, 0, 8, 46, 0, 9, 189,
			0, 8, 14, 0, 8, 142, 0, 8, 78, 0,
			9, 253, 96, 7, 256, 0, 8, 81, 0, 8,
			17, 85, 8, 131, 82, 7, 31, 0, 8, 113,
			0, 8, 49, 0, 9, 195, 80, 7, 10, 0,
			8, 97, 0, 8, 33, 0, 9, 163, 0, 8,
			1, 0, 8, 129, 0, 8, 65, 0, 9, 227,
			80, 7, 6, 0, 8, 89, 0, 8, 25, 0,
			9, 147, 83, 7, 59, 0, 8, 121, 0, 8,
			57, 0, 9, 211, 81, 7, 17, 0, 8, 105,
			0, 8, 41, 0, 9, 179, 0, 8, 9, 0,
			8, 137, 0, 8, 73, 0, 9, 243, 80, 7,
			4, 0, 8, 85, 0, 8, 21, 80, 8, 258,
			83, 7, 43, 0, 8, 117, 0, 8, 53, 0,
			9, 203, 81, 7, 13, 0, 8, 101, 0, 8,
			37, 0, 9, 171, 0, 8, 5, 0, 8, 133,
			0, 8, 69, 0, 9, 235, 80, 7, 8, 0,
			8, 93, 0, 8, 29, 0, 9, 155, 84, 7,
			83, 0, 8, 125, 0, 8, 61, 0, 9, 219,
			82, 7, 23, 0, 8, 109, 0, 8, 45, 0,
			9, 187, 0, 8, 13, 0, 8, 141, 0, 8,
			77, 0, 9, 251, 80, 7, 3, 0, 8, 83,
			0, 8, 19, 85, 8, 195, 83, 7, 35, 0,
			8, 115, 0, 8, 51, 0, 9, 199, 81, 7,
			11, 0, 8, 99, 0, 8, 35, 0, 9, 167,
			0, 8, 3, 0, 8, 131, 0, 8, 67, 0,
			9, 231, 80, 7, 7, 0, 8, 91, 0, 8,
			27, 0, 9, 151, 84, 7, 67, 0, 8, 123,
			0, 8, 59, 0, 9, 215, 82, 7, 19, 0,
			8, 107, 0, 8, 43, 0, 9, 183, 0, 8,
			11, 0, 8, 139, 0, 8, 75, 0, 9, 247,
			80, 7, 5, 0, 8, 87, 0, 8, 23, 192,
			8, 0, 83, 7, 51, 0, 8, 119, 0, 8,
			55, 0, 9, 207, 81, 7, 15, 0, 8, 103,
			0, 8, 39, 0, 9, 175, 0, 8, 7, 0,
			8, 135, 0, 8, 71, 0, 9, 239, 80, 7,
			9, 0, 8, 95, 0, 8, 31, 0, 9, 159,
			84, 7, 99, 0, 8, 127, 0, 8, 63, 0,
			9, 223, 82, 7, 27, 0, 8, 111, 0, 8,
			47, 0, 9, 191, 0, 8, 15, 0, 8, 143,
			0, 8, 79, 0, 9, 255
		};

		internal static readonly int[] fixed_td = new int[96]
		{
			80, 5, 1, 87, 5, 257, 83, 5, 17, 91,
			5, 4097, 81, 5, 5, 89, 5, 1025, 85, 5,
			65, 93, 5, 16385, 80, 5, 3, 88, 5, 513,
			84, 5, 33, 92, 5, 8193, 82, 5, 9, 90,
			5, 2049, 86, 5, 129, 192, 5, 24577, 80, 5,
			2, 87, 5, 385, 83, 5, 25, 91, 5, 6145,
			81, 5, 7, 89, 5, 1537, 85, 5, 97, 93,
			5, 24577, 80, 5, 4, 88, 5, 769, 84, 5,
			49, 92, 5, 12289, 82, 5, 13, 90, 5, 3073,
			86, 5, 193, 192, 5, 24577
		};

		internal static readonly int[] cplens = new int[31]
		{
			3, 4, 5, 6, 7, 8, 9, 10, 11, 13,
			15, 17, 19, 23, 27, 31, 35, 43, 51, 59,
			67, 83, 99, 115, 131, 163, 195, 227, 258, 0,
			0
		};

		internal static readonly int[] cplext = new int[31]
		{
			0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
			1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
			4, 4, 4, 4, 5, 5, 5, 5, 0, 112,
			112
		};

		internal static readonly int[] cpdist = new int[30]
		{
			1, 2, 3, 4, 5, 7, 9, 13, 17, 25,
			33, 49, 65, 97, 129, 193, 257, 385, 513, 769,
			1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
		};

		internal static readonly int[] cpdext = new int[30]
		{
			0, 0, 0, 0, 1, 1, 2, 2, 3, 3,
			4, 4, 5, 5, 6, 6, 7, 7, 8, 8,
			9, 9, 10, 10, 11, 11, 12, 12, 13, 13
		};

		internal const int BMAX = 15;

		internal static int huft_build(int[] b, int bindex, int n, int s, int[] d, int[] e, int[] t, int[] m, int[] hp, int[] hn, int[] v)
		{
			int[] array = new int[16];
			int[] array2 = new int[3];
			int[] array3 = new int[15];
			int[] array4 = new int[16];
			int num = 0;
			int num2 = n;
			do
			{
				array[b[bindex + num]]++;
				num++;
				num2--;
			}
			while (num2 != 0);
			if (array[0] == n)
			{
				t[0] = -1;
				m[0] = 0;
				return 0;
			}
			int num3 = m[0];
			int i;
			for (i = 1; i <= 15 && array[i] == 0; i++)
			{
			}
			int j = i;
			if (num3 < i)
			{
				num3 = i;
			}
			num2 = 15;
			while (num2 != 0 && array[num2] == 0)
			{
				num2--;
			}
			int num4 = num2;
			if (num3 > num2)
			{
				num3 = num2;
			}
			m[0] = num3;
			int num5 = 1 << i;
			while (i < num2)
			{
				if ((num5 -= array[i]) < 0)
				{
					return -3;
				}
				i++;
				num5 <<= 1;
			}
			if ((num5 -= array[num2]) < 0)
			{
				return -3;
			}
			array[num2] += num5;
			i = (array4[1] = 0);
			num = 1;
			int num6 = 2;
			while (--num2 != 0)
			{
				i = (array4[num6] = i + array[num]);
				num6++;
				num++;
			}
			num2 = 0;
			num = 0;
			do
			{
				if ((i = b[bindex + num]) != 0)
				{
					v[array4[i]++] = num2;
				}
				num++;
			}
			while (++num2 < n);
			n = array4[num4];
			num2 = (array4[0] = 0);
			num = 0;
			int num7 = -1;
			int num8 = -num3;
			array3[0] = 0;
			int num9 = 0;
			int num10 = 0;
			for (; j <= num4; j++)
			{
				int num11 = array[j];
				while (num11-- != 0)
				{
					int num12;
					while (j > num8 + num3)
					{
						num7++;
						num8 += num3;
						num10 = num4 - num8;
						num10 = ((num10 > num3) ? num3 : num10);
						if ((num12 = 1 << (i = j - num8)) > num11 + 1)
						{
							num12 -= num11 + 1;
							num6 = j;
							if (i < num10)
							{
								while (++i < num10 && (num12 <<= 1) > array[++num6])
								{
									num12 -= array[num6];
								}
							}
						}
						num10 = 1 << i;
						if (hn[0] + num10 > 1440)
						{
							return -3;
						}
						num9 = (array3[num7] = hn[0]);
						hn[0] += num10;
						if (num7 != 0)
						{
							array4[num7] = num2;
							array2[0] = (byte)i;
							array2[1] = (byte)num3;
							i = SupportClass.URShift(num2, num8 - num3);
							array2[2] = num9 - array3[num7 - 1] - i;
							Array.Copy(array2, 0, hp, (array3[num7 - 1] + i) * 3, 3);
						}
						else
						{
							t[0] = num9;
						}
					}
					array2[1] = (byte)(j - num8);
					if (num >= n)
					{
						array2[0] = 192;
					}
					else if (v[num] < s)
					{
						array2[0] = (byte)((v[num] >= 256) ? 96 : 0);
						array2[2] = v[num++];
					}
					else
					{
						array2[0] = (byte)(e[v[num] - s] + 16 + 64);
						array2[2] = d[v[num++] - s];
					}
					num12 = 1 << j - num8;
					for (i = SupportClass.URShift(num2, num8); i < num10; i += num12)
					{
						Array.Copy(array2, 0, hp, (num9 + i) * 3, 3);
					}
					i = 1 << j - 1;
					while ((num2 & i) != 0)
					{
						num2 ^= i;
						i = SupportClass.URShift(i, 1);
					}
					num2 ^= i;
					int num13 = (1 << num8) - 1;
					while ((num2 & num13) != array4[num7])
					{
						num7--;
						num8 -= num3;
						num13 = (1 << num8) - 1;
					}
				}
			}
			if (num5 == 0 || num4 == 1)
			{
				return 0;
			}
			return -5;
		}

		internal static int inflate_trees_bits(int[] c, int[] bb, int[] tb, int[] hp, ZStream z)
		{
			int[] hn = new int[1];
			int[] v = new int[19];
			int num = huft_build(c, 0, 19, 19, null, null, tb, bb, hp, hn, v);
			if (num == -3)
			{
				z.msg = "oversubscribed dynamic bit lengths tree";
			}
			else if (num == -5 || bb[0] == 0)
			{
				z.msg = "incomplete dynamic bit lengths tree";
				num = -3;
			}
			return num;
		}

		internal static int inflate_trees_dynamic(int nl, int nd, int[] c, int[] bl, int[] bd, int[] tl, int[] td, int[] hp, ZStream z)
		{
			int[] hn = new int[1];
			int[] v = new int[288];
			int num = huft_build(c, 0, nl, 257, cplens, cplext, tl, bl, hp, hn, v);
			if (num != 0 || bl[0] == 0)
			{
				switch (num)
				{
				case -3:
					z.msg = "oversubscribed literal/length tree";
					break;
				default:
					z.msg = "incomplete literal/length tree";
					num = -3;
					break;
				case -4:
					break;
				}
				return num;
			}
			num = huft_build(c, nl, nd, 0, cpdist, cpdext, td, bd, hp, hn, v);
			if (num != 0 || (bd[0] == 0 && nl > 257))
			{
				switch (num)
				{
				case -3:
					z.msg = "oversubscribed distance tree";
					break;
				case -5:
					z.msg = "incomplete distance tree";
					num = -3;
					break;
				default:
					z.msg = "empty distance tree with lengths";
					num = -3;
					break;
				case -4:
					break;
				}
				return num;
			}
			return 0;
		}

		internal static int inflate_trees_fixed(int[] bl, int[] bd, int[][] tl, int[][] td, ZStream z)
		{
			bl[0] = 9;
			bd[0] = 5;
			tl[0] = fixed_tl;
			td[0] = fixed_td;
			return 0;
		}
	}
	internal sealed class StaticTree
	{
		private const int MAX_BITS = 15;

		private const int BL_CODES = 19;

		private const int D_CODES = 30;

		private const int LITERALS = 256;

		private const int LENGTH_CODES = 29;

		private static readonly int L_CODES;

		internal const int MAX_BL_BITS = 7;

		internal static readonly short[] static_ltree;

		internal static readonly short[] static_dtree;

		internal static StaticTree static_l_desc;

		internal static StaticTree static_d_desc;

		internal static StaticTree static_bl_desc;

		internal short[] static_tree;

		internal int[] extra_bits;

		internal int extra_base;

		internal int elems;

		internal int max_length;

		internal StaticTree(short[] static_tree, int[] extra_bits, int extra_base, int elems, int max_length)
		{
			this.static_tree = static_tree;
			this.extra_bits = extra_bits;
			this.extra_base = extra_base;
			this.elems = elems;
			this.max_length = max_length;
		}

		static StaticTree()
		{
			L_CODES = 286;
			static_ltree = new short[576]
			{
				12, 8, 140, 8, 76, 8, 204, 8, 44, 8,
				172, 8, 108, 8, 236, 8, 28, 8, 156, 8,
				92, 8, 220, 8, 60, 8, 188, 8, 124, 8,
				252, 8, 2, 8, 130, 8, 66, 8, 194, 8,
				34, 8, 162, 8, 98, 8, 226, 8, 18, 8,
				146, 8, 82, 8, 210, 8, 50, 8, 178, 8,
				114, 8, 242, 8, 10, 8, 138, 8, 74, 8,
				202, 8, 42, 8, 170, 8, 106, 8, 234, 8,
				26, 8, 154, 8, 90, 8, 218, 8, 58, 8,
				186, 8, 122, 8, 250, 8, 6, 8, 134, 8,
				70, 8, 198, 8, 38, 8, 166, 8, 102, 8,
				230, 8, 22, 8, 150, 8, 86, 8, 214, 8,
				54, 8, 182, 8, 118, 8, 246, 8, 14, 8,
				142, 8, 78, 8, 206, 8, 46, 8, 174, 8,
				110, 8, 238, 8, 30, 8, 158, 8, 94, 8,
				222, 8, 62, 8, 190, 8, 126, 8, 254, 8,
				1, 8, 129, 8, 65, 8, 193, 8, 33, 8,
				161, 8, 97, 8, 225, 8, 17, 8, 145, 8,
				81, 8, 209, 8, 49, 8, 177, 8, 113, 8,
				241, 8, 9, 8, 137, 8, 73, 8, 201, 8,
				41, 8, 169, 8, 105, 8, 233, 8, 25, 8,
				153, 8, 89, 8, 217, 8, 57, 8, 185, 8,
				121, 8, 249, 8, 5, 8, 133, 8, 69, 8,
				197, 8, 37, 8, 165, 8, 101, 8, 229, 8,
				21, 8, 149, 8, 85, 8, 213, 8, 53, 8,
				181, 8, 117, 8, 245, 8, 13, 8, 141, 8,
				77, 8, 205, 8, 45, 8, 173, 8, 109, 8,
				237, 8, 29, 8, 157, 8, 93, 8, 221, 8,
				61, 8, 189, 8, 125, 8, 253, 8, 19, 9,
				275, 9, 147, 9, 403, 9, 83, 9, 339, 9,
				211, 9, 467, 9, 51, 9, 307, 9, 179, 9,
				435, 9, 115, 9, 371, 9, 243, 9, 499, 9,
				11, 9, 267, 9, 139, 9, 395, 9, 75, 9,
				331, 9, 203, 9, 459, 9, 43, 9, 299, 9,
				171, 9, 427, 9, 107, 9, 363, 9, 235, 9,
				491, 9, 27, 9, 283, 9, 155, 9, 411, 9,
				91, 9, 347, 9, 219, 9, 475, 9, 59, 9,
				315, 9, 187, 9, 443, 9, 123, 9, 379, 9,
				251, 9, 507, 9, 7, 9, 263, 9, 135, 9,
				391, 9, 71, 9, 327, 9, 199, 9, 455, 9,
				39, 9, 295, 9, 167, 9, 423, 9, 103, 9,
				359, 9, 231, 9, 487, 9, 23, 9, 279, 9,
				151, 9, 407, 9, 87, 9, 343, 9, 215, 9,
				471, 9, 55, 9, 311, 9, 183, 9, 439, 9,
				119, 9, 375, 9, 247, 9, 503, 9, 15, 9,
				271, 9, 143, 9, 399, 9, 79, 9, 335, 9,
				207, 9, 463, 9, 47, 9, 303, 9, 175, 9,
				431, 9, 111, 9, 367, 9, 239, 9, 495, 9,
				31, 9, 287, 9, 159, 9, 415, 9, 95, 9,
				351, 9, 223, 9, 479, 9, 63, 9, 319, 9,
				191, 9, 447, 9, 127, 9, 383, 9, 255, 9,
				511, 9, 0, 7, 64, 7, 32, 7, 96, 7,
				16, 7, 80, 7, 48, 7, 112, 7, 8, 7,
				72, 7, 40, 7, 104, 7, 24, 7, 88, 7,
				56, 7, 120, 7, 4, 7, 68, 7, 36, 7,
				100, 7, 20, 7, 84, 7, 52, 7, 116, 7,
				3, 8, 131, 8, 67, 8, 195, 8, 35, 8,
				163, 8, 99, 8, 227, 8
			};
			static_dtree = new short[60]
			{
				0, 5, 16, 5, 8, 5, 24, 5, 4, 5,
				20, 5, 12, 5, 28, 5, 2, 5, 18, 5,
				10, 5, 26, 5, 6, 5, 22, 5, 14, 5,
				30, 5, 1, 5, 17, 5, 9, 5, 25, 5,
				5, 5, 21, 5, 13, 5, 29, 5, 3, 5,
				19, 5, 11, 5, 27, 5, 7, 5, 23, 5
			};
			static_l_desc = new StaticTree(static_ltree, Tree.extra_lbits, 257, L_CODES, 15);
			static_d_desc = new StaticTree(static_dtree, Tree.extra_dbits, 0, 30, 15);
			static_bl_desc = new StaticTree(null, Tree.extra_blbits, 0, 19, 7);
		}
	}
	public class SupportClass
	{
		public static long Identity(long literal)
		{
			return literal;
		}

		public static ulong Identity(ulong literal)
		{
			return literal;
		}

		public static float Identity(float literal)
		{
			return literal;
		}

		public static double Identity(double literal)
		{
			return literal;
		}

		public static int URShift(int number, int bits)
		{
			if (number >= 0)
			{
				return number >> bits;
			}
			return (number >> bits) + (2 << ~bits);
		}

		public static int URShift(int number, long bits)
		{
			return URShift(number, (int)bits);
		}

		public static long URShift(long number, int bits)
		{
			if (number >= 0)
			{
				return number >> bits;
			}
			return (number >> bits) + (2L << ~bits);
		}

		public static long URShift(long number, long bits)
		{
			return URShift(number, (int)bits);
		}

		public static int ReadInput(Stream sourceStream, byte[] target, int start, int count)
		{
			if (target.Length == 0)
			{
				return 0;
			}
			byte[] array = new byte[target.Length];
			int num = sourceStream.Read(array, start, count);
			if (num == 0)
			{
				return -1;
			}
			for (int i = start; i < start + num; i++)
			{
				target[i] = array[i];
			}
			return num;
		}

		public static int ReadInput(TextReader sourceTextReader, byte[] target, int start, int count)
		{
			if (target.Length == 0)
			{
				return 0;
			}
			char[] array = new char[target.Length];
			int num = sourceTextReader.Read(array, start, count);
			if (num == 0)
			{
				return -1;
			}
			for (int i = start; i < start + num; i++)
			{
				target[i] = (byte)array[i];
			}
			return num;
		}

		public static byte[] ToByteArray(string sourceString)
		{
			return Encoding.UTF8.GetBytes(sourceString);
		}

		public static char[] ToCharArray(byte[] byteArray)
		{
			return Encoding.UTF8.GetChars(byteArray);
		}
	}
	internal sealed class Tree
	{
		private const int MAX_BITS = 15;

		private const int BL_CODES = 19;

		private const int D_CODES = 30;

		private const int LITERALS = 256;

		private const int LENGTH_CODES = 29;

		private static readonly int L_CODES = 286;

		private static readonly int HEAP_SIZE = 2 * L_CODES + 1;

		internal const int MAX_BL_BITS = 7;

		internal const int END_BLOCK = 256;

		internal const int REP_3_6 = 16;

		internal const int REPZ_3_10 = 17;

		internal const int REPZ_11_138 = 18;

		internal static readonly int[] extra_lbits = new int[29]
		{
			0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
			1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
			4, 4, 4, 4, 5, 5, 5, 5, 0
		};

		internal static readonly int[] extra_dbits = new int[30]
		{
			0, 0, 0, 0, 1, 1, 2, 2, 3, 3,
			4, 4, 5, 5, 6, 6, 7, 7, 8, 8,
			9, 9, 10, 10, 11, 11, 12, 12, 13, 13
		};

		internal static readonly int[] extra_blbits = new int[19]
		{
			0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
			0, 0, 0, 0, 0, 0, 2, 3, 7
		};

		internal static readonly byte[] bl_order = new byte[19]
		{
			16, 17, 18, 0, 8, 7, 9, 6, 10, 5,
			11, 4, 12, 3, 13, 2, 14, 1, 15
		};

		internal const int Buf_size = 16;

		internal const int DIST_CODE_LEN = 512;

		internal static readonly byte[] _dist_code = new byte[512]
		{
			0, 1, 2, 3, 4, 4, 5, 5, 6, 6,
			6, 6, 7, 7, 7, 7, 8, 8, 8, 8,
			8, 8, 8, 8, 9, 9, 9, 9, 9, 9,
			9, 9, 10, 10, 10, 10, 10, 10, 10, 10,
			10, 10, 10, 10, 10, 10, 10, 10, 11, 11,
			11, 11, 11, 11, 11, 11, 11, 11, 11, 11,
			11, 11, 11, 11, 12, 12, 12, 12, 12, 12,
			12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
			12, 12, 12, 12, 12, 12, 12, 12, 12, 12,
			12, 12, 12, 12, 12, 12, 13, 13, 13, 13,
			13, 13, 13, 13, 13, 13, 13, 13, 13, 13,
			13, 13, 13, 13, 13, 13, 13, 13, 13, 13,
			13, 13, 13, 13, 13, 13, 13, 13, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
			14, 14, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
			15, 15, 15, 15, 15, 15, 0, 0, 16, 17,
			18, 18, 19, 19, 20, 20, 20, 20, 21, 21,
			21, 21, 22, 22, 22, 22, 22, 22, 22, 22,
			23, 23, 23, 23, 23, 23, 23, 23, 24, 24,
			24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
			24, 24, 24, 24, 25, 25, 25, 25, 25, 25,
			25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
			26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 27, 27, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
			28, 28, 28, 28, 28, 28, 28, 28, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29, 29, 29, 29, 29, 29, 29, 29, 29,
			29, 29
		};

		internal static readonly byte[] _length_code = new byte[256]
		{
			0, 1, 2, 3, 4, 5, 6, 7, 8, 8,
			9, 9, 10, 10, 11, 11, 12, 12, 12, 12,
			13, 13, 13, 13, 14, 14, 14, 14, 15, 15,
			15, 15, 16, 16, 16, 16, 16, 16, 16, 16,
			17, 17, 17, 17, 17, 17, 17, 17, 18, 18,
			18, 18, 18, 18, 18, 18, 19, 19, 19, 19,
			19, 19, 19, 19, 20, 20, 20, 20, 20, 20,
			20, 20, 20, 20, 20, 20, 20, 20, 20, 20,
			21, 21, 21, 21, 21, 21, 21, 21, 21, 21,
			21, 21, 21, 21, 21, 21, 22, 22, 22, 22,
			22, 22, 22, 22, 22, 22, 22, 22, 22, 22,
			22, 22, 23, 23, 23, 23, 23, 23, 23, 23,
			23, 23, 23, 23, 23, 23, 23, 23, 24, 24,
			24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
			24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
			24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
			25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
			25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
			25, 25, 25, 25, 25, 25, 25, 25, 25, 25,
			25, 25, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 26, 26, 26, 26, 26, 26, 26, 26,
			26, 26, 26, 26, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 27, 27, 27, 27, 27, 27,
			27, 27, 27, 27, 27, 28
		};

		internal static readonly int[] base_length = new int[29]
		{
			0, 1, 2, 3, 4, 5, 6, 7, 8, 10,
			12, 14, 16, 20, 24, 28, 32, 40, 48, 56,
			64, 80, 96, 112, 128, 160, 192, 224, 0
		};

		internal static readonly int[] base_dist = new int[30]
		{
			0, 1, 2, 3, 4, 6, 8, 12, 16, 24,
			32, 48, 64, 96, 128, 192, 256, 384, 512, 768,
			1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576
		};

		internal short[] dyn_tree;

		internal int max_code;

		internal StaticTree stat_desc;

		internal static int d_code(int dist)
		{
			if (dist >= 256)
			{
				return _dist_code[256 + SupportClass.URShift(dist, 7)];
			}
			return _dist_code[dist];
		}

		internal void gen_bitlen(Deflate s)
		{
			short[] array = dyn_tree;
			short[] static_tree = stat_desc.static_tree;
			int[] extra_bits = stat_desc.extra_bits;
			int extra_base = stat_desc.extra_base;
			int max_length = stat_desc.max_length;
			int num = 0;
			for (int i = 0; i <= 15; i++)
			{
				s.bl_count[i] = 0;
			}
			array[s.heap[s.heap_max] * 2 + 1] = 0;
			int j;
			for (j = s.heap_max + 1; j < HEAP_SIZE; j++)
			{
				int num2 = s.heap[j];
				int i = array[array[num2 * 2 + 1] * 2 + 1] + 1;
				if (i > max_length)
				{
					i = max_length;
					num++;
				}
				array[num2 * 2 + 1] = (short)i;
				if (num2 <= max_code)
				{
					s.bl_count[i]++;
					int num3 = 0;
					if (num2 >= extra_base)
					{
						num3 = extra_bits[num2 - extra_base];
					}
					short num4 = array[num2 * 2];
					s.opt_len += num4 * (i + num3);
					if (static_tree != null)
					{
						s.static_len += num4 * (static_tree[num2 * 2 + 1] + num3);
					}
				}
			}
			if (num == 0)
			{
				return;
			}
			do
			{
				int i = max_length - 1;
				while (s.bl_count[i] == 0)
				{
					i--;
				}
				s.bl_count[i]--;
				s.bl_count[i + 1] = (short)(s.bl_count[i + 1] + 2);
				s.bl_count[max_length]--;
				num -= 2;
			}
			while (num > 0);
			for (int i = max_length; i != 0; i--)
			{
				int num2 = s.bl_count[i];
				while (num2 != 0)
				{
					int num5 = s.heap[--j];
					if (num5 <= max_code)
					{
						if (array[num5 * 2 + 1] != i)
						{
							s.opt_len = (int)(s.opt_len + ((long)i - (long)array[num5 * 2 + 1]) * array[num5 * 2]);
							array[num5 * 2 + 1] = (short)i;
						}
						num2--;
					}
				}
			}
		}

		internal void build_tree(Deflate s)
		{
			short[] array = dyn_tree;
			short[] static_tree = stat_desc.static_tree;
			int elems = stat_desc.elems;
			int num = -1;
			s.heap_len = 0;
			s.heap_max = HEAP_SIZE;
			for (int i = 0; i < elems; i++)
			{
				if (array[i * 2] != 0)
				{
					num = (s.heap[++s.heap_len] = i);
					s.depth[i] = 0;
				}
				else
				{
					array[i * 2 + 1] = 0;
				}
			}
			int num2;
			while (s.heap_len < 2)
			{
				num2 = (s.heap[++s.heap_len] = ((num < 2) ? (++num) : 0));
				array[num2 * 2] = 1;
				s.depth[num2] = 0;
				s.opt_len--;
				if (static_tree != null)
				{
					s.static_len -= static_tree[num2 * 2 + 1];
				}
			}
			max_code = num;
			for (int i = s.heap_len / 2; i >= 1; i--)
			{
				s.pqdownheap(array, i);
			}
			num2 = elems;
			do
			{
				int i = s.heap[1];
				s.heap[1] = s.heap[s.heap_len--];
				s.pqdownheap(array, 1);
				int num3 = s.heap[1];
				s.heap[--s.heap_max] = i;
				s.heap[--s.heap_max] = num3;
				array[num2 * 2] = (short)(array[i * 2] + array[num3 * 2]);
				s.depth[num2] = (byte)(Math.Max(s.depth[i], s.depth[num3]) + 1);
				array[i * 2 + 1] = (array[num3 * 2 + 1] = (short)num2);
				s.heap[1] = num2++;
				s.pqdownheap(array, 1);
			}
			while (s.heap_len >= 2);
			s.heap[--s.heap_max] = s.heap[1];
			gen_bitlen(s);
			gen_codes(array, num, s.bl_count);
		}

		internal static void gen_codes(short[] tree, int max_code, short[] bl_count)
		{
			short[] array = new short[16];
			short num = 0;
			for (int i = 1; i <= 15; i++)
			{
				num = (array[i] = (short)(num + bl_count[i - 1] << 1));
			}
			for (int j = 0; j <= max_code; j++)
			{
				int num2 = tree[j * 2 + 1];
				if (num2 != 0)
				{
					tree[j * 2] = (short)bi_reverse(array[num2]++, num2);
				}
			}
		}

		internal static int bi_reverse(int code, int len)
		{
			int num = 0;
			do
			{
				num |= code & 1;
				code = SupportClass.URShift(code, 1);
				num <<= 1;
			}
			while (--len > 0);
			return SupportClass.URShift(num, 1);
		}
	}
	public class ZInputStream : BinaryReader
	{
		protected ZStream z = new ZStream();

		protected int bufsize = 512;

		protected int flush;

		protected byte[] buf;

		protected byte[] buf1 = new byte[1];

		protected bool compress;

		internal Stream in_Renamed;

		internal bool nomoreinput;

		public virtual int FlushMode
		{
			get
			{
				return flush;
			}
			set
			{
				flush = value;
			}
		}

		public virtual long TotalIn => z.total_in;

		public virtual long TotalOut => z.total_out;

		internal void InitBlock()
		{
			flush = 0;
			buf = new byte[bufsize];
		}

		public ZInputStream(Stream in_Renamed)
			: base(in_Renamed)
		{
			InitBlock();
			this.in_Renamed = in_Renamed;
			z.inflateInit();
			compress = false;
			z.next_in = buf;
			z.next_in_index = 0;
			z.avail_in = 0;
		}

		public ZInputStream(Stream in_Renamed, int level)
			: base(in_Renamed)
		{
			InitBlock();
			this.in_Renamed = in_Renamed;
			z.deflateInit(level);
			compress = true;
			z.next_in = buf;
			z.next_in_index = 0;
			z.avail_in = 0;
		}

		public override int Read()
		{
			if (read(buf1, 0, 1) == -1)
			{
				return -1;
			}
			return buf1[0] & 0xFF;
		}

		public int read(byte[] b, int off, int len)
		{
			if (len == 0)
			{
				return 0;
			}
			z.next_out = b;
			z.next_out_index = off;
			z.avail_out = len;
			int num;
			do
			{
				if (z.avail_in == 0 && !nomoreinput)
				{
					z.next_in_index = 0;
					z.avail_in = SupportClass.ReadInput(in_Renamed, buf, 0, bufsize);
					if (z.avail_in == -1)
					{
						z.avail_in = 0;
						nomoreinput = true;
					}
				}
				num = ((!compress) ? z.inflate(flush) : z.deflate(flush));
				if (nomoreinput && num == -5)
				{
					return -1;
				}
				if (num != 0 && num != 1)
				{
					throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
				}
				if (nomoreinput && z.avail_out == len)
				{
					return -1;
				}
			}
			while (z.avail_out == len && num == 0);
			return len - z.avail_out;
		}

		public long skip(long n)
		{
			int num = 512;
			if (n < num)
			{
				num = (int)n;
			}
			byte[] array = new byte[num];
			return SupportClass.ReadInput(BaseStream, array, 0, array.Length);
		}

		public override void Close()
		{
			in_Renamed.Close();
		}
	}
	public sealed class zlibConst
	{
		private const string version_Renamed_Field = "1.0.2";

		public const int Z_NO_COMPRESSION = 0;

		public const int Z_BEST_SPEED = 1;

		public const int Z_BEST_COMPRESSION = 9;

		public const int Z_DEFAULT_COMPRESSION = -1;

		public const int Z_FILTERED = 1;

		public const int Z_HUFFMAN_ONLY = 2;

		public const int Z_DEFAULT_STRATEGY = 0;

		public const int Z_NO_FLUSH = 0;

		public const int Z_PARTIAL_FLUSH = 1;

		public const int Z_SYNC_FLUSH = 2;

		public const int Z_FULL_FLUSH = 3;

		public const int Z_FINISH = 4;

		public const int Z_OK = 0;

		public const int Z_STREAM_END = 1;

		public const int Z_NEED_DICT = 2;

		public const int Z_ERRNO = -1;

		public const int Z_STREAM_ERROR = -2;

		public const int Z_DATA_ERROR = -3;

		public const int Z_MEM_ERROR = -4;

		public const int Z_BUF_ERROR = -5;

		public const int Z_VERSION_ERROR = -6;

		public static string version()
		{
			return "1.0.2";
		}
	}
	public class ZOutputStream : Stream
	{
		protected internal ZStream z = new ZStream();

		protected internal int bufsize = 4096;

		protected internal int flush_Renamed_Field;

		protected internal byte[] buf;

		protected internal byte[] buf1 = new byte[1];

		protected internal bool compress;

		private Stream out_Renamed;

		public virtual int FlushMode
		{
			get
			{
				return flush_Renamed_Field;
			}
			set
			{
				flush_Renamed_Field = value;
			}
		}

		public virtual long TotalIn => z.total_in;

		public virtual long TotalOut => z.total_out;

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => 0L;

		public override long Position
		{
			get
			{
				return 0L;
			}
			set
			{
			}
		}

		private void InitBlock()
		{
			flush_Renamed_Field = 0;
			buf = new byte[bufsize];
		}

		public ZOutputStream(Stream out_Renamed)
		{
			InitBlock();
			this.out_Renamed = out_Renamed;
			z.inflateInit();
			compress = false;
		}

		public ZOutputStream(Stream out_Renamed, int level)
		{
			InitBlock();
			this.out_Renamed = out_Renamed;
			z.deflateInit(level);
			compress = true;
		}

		public void WriteByte(int b)
		{
			buf1[0] = (byte)b;
			Write(buf1, 0, 1);
		}

		public override void WriteByte(byte b)
		{
			WriteByte(b);
		}

		public override void Write(byte[] b1, int off, int len)
		{
			if (len == 0)
			{
				return;
			}
			byte[] array = new byte[b1.Length];
			Array.Copy(b1, 0, array, 0, b1.Length);
			z.next_in = array;
			z.next_in_index = off;
			z.avail_in = len;
			do
			{
				z.next_out = buf;
				z.next_out_index = 0;
				z.avail_out = bufsize;
				int num = ((!compress) ? z.inflate(flush_Renamed_Field) : z.deflate(flush_Renamed_Field));
				if (num != 0 && num != 1)
				{
					throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
				}
				out_Renamed.Write(buf, 0, bufsize - z.avail_out);
			}
			while (z.avail_in > 0 || z.avail_out == 0);
		}

		public virtual void finish()
		{
			do
			{
				z.next_out = buf;
				z.next_out_index = 0;
				z.avail_out = bufsize;
				int num = ((!compress) ? z.inflate(4) : z.deflate(4));
				if (num != 1 && num != 0)
				{
					throw new ZStreamException((compress ? "de" : "in") + "flating: " + z.msg);
				}
				if (bufsize - z.avail_out > 0)
				{
					out_Renamed.Write(buf, 0, bufsize - z.avail_out);
				}
			}
			while (z.avail_in > 0 || z.avail_out == 0);
			try
			{
				Flush();
			}
			catch
			{
			}
		}

		public virtual void end()
		{
			if (compress)
			{
				z.deflateEnd();
			}
			else
			{
				z.inflateEnd();
			}
			z.free();
			z = null;
		}

		public override void Close()
		{
			try
			{
				finish();
			}
			catch
			{
			}
			finally
			{
				end();
				out_Renamed.Close();
				out_Renamed = null;
			}
		}

		public override void Flush()
		{
			out_Renamed.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return 0;
		}

		public override void SetLength(long value)
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return 0L;
		}
	}
	public sealed class ZStream
	{
		private const int MAX_WBITS = 15;

		private static readonly int DEF_WBITS = 15;

		private const int Z_NO_FLUSH = 0;

		private const int Z_PARTIAL_FLUSH = 1;

		private const int Z_SYNC_FLUSH = 2;

		private const int Z_FULL_FLUSH = 3;

		private const int Z_FINISH = 4;

		private const int MAX_MEM_LEVEL = 9;

		private const int Z_OK = 0;

		private const int Z_STREAM_END = 1;

		private const int Z_NEED_DICT = 2;

		private const int Z_ERRNO = -1;

		private const int Z_STREAM_ERROR = -2;

		private const int Z_DATA_ERROR = -3;

		private const int Z_MEM_ERROR = -4;

		private const int Z_BUF_ERROR = -5;

		private const int Z_VERSION_ERROR = -6;

		public byte[] next_in;

		public int next_in_index;

		public int avail_in;

		public long total_in;

		public byte[] next_out;

		public int next_out_index;

		public int avail_out;

		public long total_out;

		public string msg;

		internal Deflate dstate;

		internal Inflate istate;

		internal int data_type;

		public long adler;

		internal Adler32 _adler = new Adler32();

		public int inflateInit()
		{
			return inflateInit(DEF_WBITS);
		}

		public int inflateInit(int w)
		{
			istate = new Inflate();
			return istate.inflateInit(this, w);
		}

		public int inflate(int f)
		{
			if (istate == null)
			{
				return -2;
			}
			return istate.inflate(this, f);
		}

		public int inflateEnd()
		{
			if (istate == null)
			{
				return -2;
			}
			int result = istate.inflateEnd(this);
			istate = null;
			return result;
		}

		public int inflateSync()
		{
			if (istate == null)
			{
				return -2;
			}
			return istate.inflateSync(this);
		}

		public int inflateSetDictionary(byte[] dictionary, int dictLength)
		{
			if (istate == null)
			{
				return -2;
			}
			return istate.inflateSetDictionary(this, dictionary, dictLength);
		}

		public int deflateInit(int level)
		{
			return deflateInit(level, 15);
		}

		public int deflateInit(int level, int bits)
		{
			dstate = new Deflate();
			return dstate.deflateInit(this, level, bits);
		}

		public int deflate(int flush)
		{
			if (dstate == null)
			{
				return -2;
			}
			return dstate.deflate(this, flush);
		}

		public int deflateEnd()
		{
			if (dstate == null)
			{
				return -2;
			}
			int result = dstate.deflateEnd();
			dstate = null;
			return result;
		}

		public int deflateParams(int level, int strategy)
		{
			if (dstate == null)
			{
				return -2;
			}
			return dstate.deflateParams(this, level, strategy);
		}

		public int deflateSetDictionary(byte[] dictionary, int dictLength)
		{
			if (dstate == null)
			{
				return -2;
			}
			return dstate.deflateSetDictionary(this, dictionary, dictLength);
		}

		internal void flush_pending()
		{
			int pending = dstate.pending;
			if (pending > avail_out)
			{
				pending = avail_out;
			}
			if (pending != 0)
			{
				if (dstate.pending_buf.Length > dstate.pending_out && next_out.Length > next_out_index && dstate.pending_buf.Length >= dstate.pending_out + pending)
				{
					_ = next_out.Length;
					_ = next_out_index + pending;
				}
				Array.Copy(dstate.pending_buf, dstate.pending_out, next_out, next_out_index, pending);
				next_out_index += pending;
				dstate.pending_out += pending;
				total_out += pending;
				avail_out -= pending;
				dstate.pending -= pending;
				if (dstate.pending == 0)
				{
					dstate.pending_out = 0;
				}
			}
		}

		internal int read_buf(byte[] buf, int start, int size)
		{
			int num = avail_in;
			if (num > size)
			{
				num = size;
			}
			if (num == 0)
			{
				return 0;
			}
			avail_in -= num;
			if (dstate.noheader == 0)
			{
				adler = _adler.adler32(adler, next_in, next_in_index, num);
			}
			Array.Copy(next_in, next_in_index, buf, start, num);
			next_in_index += num;
			total_in += num;
			return num;
		}

		public void free()
		{
			next_in = null;
			next_out = null;
			msg = null;
			_adler = null;
		}
	}
	public class ZStreamException : IOException
	{
		public ZStreamException()
		{
		}

		public ZStreamException(string s)
			: base(s)
		{
		}
	}
}
namespace VncSharpCore.Properties
{
	[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
	[DebuggerNonUserCode]
	[CompilerGenerated]
	internal class Resources
	{
		private static ResourceManager resourceMan;

		private static CultureInfo resourceCulture;

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static ResourceManager ResourceManager
		{
			get
			{
				if (resourceMan == null)
				{
					ResourceManager resourceManager = new ResourceManager("VncSharpCore.Properties.Resources", typeof(Resources).Assembly);
					resourceMan = resourceManager;
				}
				return resourceMan;
			}
		}

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		internal static CultureInfo Culture
		{
			get
			{
				return resourceCulture;
			}
			set
			{
				resourceCulture = value;
			}
		}

		internal static Icon vncviewer
		{
			get
			{
				object obj = ResourceManager.GetObject("vncviewer", resourceCulture);
				return (Icon)obj;
			}
		}

		internal Resources()
		{
		}
	}
}
namespace VncSharpCore.Encodings
{
	public sealed class CopyRectRectangle : EncodedRectangle
	{
		private Point source;

		public CopyRectRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle)
			: base(rfb, framebuffer, rectangle, 1)
		{
		}

		public override void Decode()
		{
			source = default(Point);
			source.X = rfb.ReadUInt16();
			source.Y = rfb.ReadUInt16();
		}

		public unsafe override void Draw(Bitmap desktop)
		{
			BitmapData bitmapData = desktop.LockBits(new Rectangle(new Point(0, 0), desktop.Size), ImageLockMode.ReadWrite, desktop.PixelFormat);
			if (rectangle.Top + rectangle.Height >= framebuffer.Height)
			{
				rectangle.Height = framebuffer.Height - rectangle.Top - 1;
			}
			try
			{
				int* ptr = (int*)(void*)bitmapData.Scan0;
				int* ptr2 = (int*)(void*)bitmapData.Scan0;
				int num = desktop.Width - rectangle.Width;
				ptr += source.Y * desktop.Width + source.X;
				ptr2 += rectangle.Y * desktop.Width + rectangle.X;
				if (ptr2 < ptr)
				{
					for (int i = 0; i < rectangle.Height; i++)
					{
						for (int j = 0; j < rectangle.Width; j++)
						{
							*(ptr2++) = *(ptr++);
						}
						ptr += num;
						ptr2 += num;
					}
					return;
				}
				ptr += rectangle.Height * desktop.Width + rectangle.Width;
				ptr2 += rectangle.Height * desktop.Width + rectangle.Width;
				for (int k = 0; k < rectangle.Height; k++)
				{
					for (int l = 0; l < rectangle.Width; l++)
					{
						*(--ptr2) = *(--ptr);
					}
					ptr -= num;
					ptr2 -= num;
				}
			}
			finally
			{
				desktop.UnlockBits(bitmapData);
				bitmapData = null;
			}
		}
	}
	public sealed class CoRreRectangle : EncodedRectangle
	{
		public CoRreRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle)
			: base(rfb, framebuffer, rectangle, 4)
		{
		}

		public override void Decode()
		{
			int num = (int)rfb.ReadUint32();
			int colour = preader.ReadPixel();
			int num2 = 0;
			FillRectangle(rectangle, colour);
			for (int i = 0; i < num; i++)
			{
				num2 = preader.ReadPixel();
				int x = rfb.ReadByte();
				int y = rfb.ReadByte();
				int width = rfb.ReadByte();
				int height = rfb.ReadByte();
				FillRectangle(new Rectangle(x, y, width, height), num2);
			}
		}
	}
	public sealed class CPixelReader : PixelReader
	{
		public CPixelReader(BinaryReader reader, Framebuffer framebuffer)
			: base(reader, framebuffer)
		{
		}

		public override int ReadPixel()
		{
			byte[] array = reader.ReadBytes(3);
			return ToGdiPlusOrder(array[2], array[1], array[0]);
		}
	}
	public abstract class EncodedRectangle : IDesktopUpdater
	{
		protected RfbProtocol rfb;

		protected Rectangle rectangle;

		protected Framebuffer framebuffer;

		protected PixelReader preader;

		public Rectangle UpdateRectangle => rectangle;

		public EncodedRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle, int encoding)
		{
			this.rfb = rfb;
			this.framebuffer = framebuffer;
			this.rectangle = rectangle;
			BinaryReader reader = ((encoding == 16) ? rfb.ZrleReader : rfb.Reader);
			switch (framebuffer.BitsPerPixel)
			{
			case 32:
				if (encoding == 16)
				{
					preader = new CPixelReader(reader, framebuffer);
				}
				else
				{
					preader = new PixelReader32(reader, framebuffer);
				}
				break;
			case 16:
				preader = new PixelReader16(reader, framebuffer);
				break;
			case 8:
				preader = new PixelReader8(reader, framebuffer, rfb);
				break;
			default:
				throw new ArgumentOutOfRangeException("BitsPerPixel", framebuffer.BitsPerPixel, "Valid VNC Pixel Widths are 8, 16 or 32 bits.");
			}
		}

		public abstract void Decode();

		public unsafe virtual void Draw(Bitmap desktop)
		{
			BitmapData bitmapData = desktop.LockBits(new Rectangle(new Point(0, 0), desktop.Size), ImageLockMode.ReadWrite, desktop.PixelFormat);
			try
			{
				int* ptr = (int*)(void*)bitmapData.Scan0;
				ptr += rectangle.Y * desktop.Width + rectangle.X;
				int num = desktop.Width - rectangle.Width;
				int num2 = 0;
				for (int i = 0; i < rectangle.Height; i++)
				{
					num2 = i * rectangle.Width;
					for (int j = 0; j < rectangle.Width; j++)
					{
						*(ptr++) = framebuffer[num2 + j];
					}
					ptr += num;
				}
			}
			finally
			{
				desktop.UnlockBits(bitmapData);
				bitmapData = null;
			}
		}

		protected void FillRectangle(Rectangle rect, int colour)
		{
			int num = 0;
			int num2 = 0;
			if (rect != rectangle)
			{
				num = rect.Y * rectangle.Width + rect.X;
				num2 = rectangle.Width - rect.Width;
			}
			for (int i = 0; i < rect.Height; i++)
			{
				for (int j = 0; j < rect.Width; j++)
				{
					framebuffer[num++] = colour;
				}
				num += num2;
			}
		}

		protected void FillRectangle(Rectangle rect, int[] tile)
		{
			int num = 0;
			int num2 = 0;
			if (rect != rectangle)
			{
				num = rect.Y * rectangle.Width + rect.X;
				num2 = rectangle.Width - rect.Width;
			}
			int num3 = 0;
			for (int i = 0; i < rect.Height; i++)
			{
				for (int j = 0; j < rect.Width; j++)
				{
					framebuffer[num++] = tile[num3++];
				}
				num += num2;
			}
		}

		protected void FillRectangle(Rectangle rect)
		{
			int num = 0;
			int num2 = 0;
			if (rect != rectangle)
			{
				num = rect.Y * rectangle.Width + rect.X;
				num2 = rectangle.Width - rect.Width;
			}
			for (int i = 0; i < rect.Height; i++)
			{
				for (int j = 0; j < rect.Width; j++)
				{
					framebuffer[num++] = preader.ReadPixel();
				}
				num += num2;
			}
		}
	}
	public sealed class HextileRectangle : EncodedRectangle
	{
		private const int RAW = 1;

		private const int BACKGROUND_SPECIFIED = 2;

		private const int FOREGROUND_SPECIFIED = 4;

		private const int ANY_SUBRECTS = 8;

		private const int SUBRECTS_COLOURED = 16;

		public HextileRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle)
			: base(rfb, framebuffer, rectangle, 5)
		{
		}

		public override void Decode()
		{
			int num = 0;
			int colour = 0;
			int colour2 = 0;
			for (int i = 0; i < rectangle.Height; i += 16)
			{
				int height = ((rectangle.Height - i < 16) ? (rectangle.Height - i) : 16);
				for (int j = 0; j < rectangle.Width; j += 16)
				{
					int num2 = ((rectangle.Width - j < 16) ? (rectangle.Width - j) : 16);
					int num3 = i * rectangle.Width + j;
					int num4 = rectangle.Width - num2;
					byte b = rfb.ReadByte();
					if ((b & 1) != 0)
					{
						FillRectangle(new Rectangle(j, i, num2, height));
						continue;
					}
					if ((b & 2) != 0)
					{
						colour = preader.ReadPixel();
					}
					FillRectangle(new Rectangle(j, i, num2, height), colour);
					if ((b & 4) != 0)
					{
						colour2 = preader.ReadPixel();
					}
					if ((b & 8) == 0)
					{
						continue;
					}
					num = rfb.ReadByte();
					for (int k = 0; k < num; k++)
					{
						if ((b & 0x10) != 0)
						{
							colour2 = preader.ReadPixel();
						}
						int num5 = rfb.ReadByte();
						int num6 = rfb.ReadByte();
						int num7 = (num5 >> 4) & 0xF;
						int num8 = num5 & 0xF;
						int width = ((num6 >> 4) & 0xF) + 1;
						int height2 = (num6 & 0xF) + 1;
						FillRectangle(new Rectangle(j + num7, i + num8, width, height2), colour2);
					}
				}
			}
		}
	}
	public abstract class PixelReader
	{
		protected BinaryReader reader;

		protected Framebuffer framebuffer;

		protected PixelReader(BinaryReader reader, Framebuffer framebuffer)
		{
			this.reader = reader;
			this.framebuffer = framebuffer;
		}

		public abstract int ReadPixel();

		protected int ToGdiPlusOrder(byte red, byte green, byte blue)
		{
			return (blue & 0xFF) | (green << 8) | (red << 16) | -16777216;
		}
	}
	public sealed class PixelReader16 : PixelReader
	{
		public PixelReader16(BinaryReader reader, Framebuffer framebuffer)
			: base(reader, framebuffer)
		{
		}

		public override int ReadPixel()
		{
			byte[] array = reader.ReadBytes(2);
			ushort num = (ushort)((array[0] & 0xFF) | (array[1] << 8));
			byte red = (byte)(((num >> framebuffer.RedShift) & framebuffer.RedMax) * 255 / framebuffer.RedMax);
			byte green = (byte)(((num >> framebuffer.GreenShift) & framebuffer.GreenMax) * 255 / framebuffer.GreenMax);
			byte blue = (byte)(((num >> framebuffer.BlueShift) & framebuffer.BlueMax) * 255 / framebuffer.BlueMax);
			return ToGdiPlusOrder(red, green, blue);
		}
	}
	public sealed class PixelReader32 : PixelReader
	{
		public PixelReader32(BinaryReader reader, Framebuffer framebuffer)
			: base(reader, framebuffer)
		{
		}

		public override int ReadPixel()
		{
			byte[] array = reader.ReadBytes(4);
			uint num = (uint)((array[0] & 0xFF) | (array[1] << 8) | (array[2] << 16) | (array[3] << 24));
			byte red = (byte)((num >> framebuffer.RedShift) & framebuffer.RedMax);
			byte green = (byte)((num >> framebuffer.GreenShift) & framebuffer.GreenMax);
			byte blue = (byte)((num >> framebuffer.BlueShift) & framebuffer.BlueMax);
			return ToGdiPlusOrder(red, green, blue);
		}
	}
	public sealed class PixelReader8 : PixelReader
	{
		private RfbProtocol rfb;

		public PixelReader8(BinaryReader reader, Framebuffer framebuffer, RfbProtocol rfb)
			: base(reader, framebuffer)
		{
			this.rfb = rfb;
		}

		public override int ReadPixel()
		{
			byte b = reader.ReadByte();
			return ToGdiPlusOrder((byte)rfb.MapEntries[b, 0], (byte)rfb.MapEntries[b, 1], (byte)rfb.MapEntries[b, 2]);
		}
	}
	public sealed class RawRectangle : EncodedRectangle
	{
		public RawRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle)
			: base(rfb, framebuffer, rectangle, 0)
		{
		}

		public override void Decode()
		{
			for (int i = 0; i < rectangle.Width * rectangle.Height; i++)
			{
				framebuffer[i] = preader.ReadPixel();
			}
		}
	}
	public sealed class RreRectangle : EncodedRectangle
	{
		public RreRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle)
			: base(rfb, framebuffer, rectangle, 2)
		{
		}

		public override void Decode()
		{
			int num = (int)rfb.ReadUint32();
			int colour = preader.ReadPixel();
			int num2 = 0;
			FillRectangle(rectangle, colour);
			for (int i = 0; i < num; i++)
			{
				num2 = preader.ReadPixel();
				int x = rfb.ReadUInt16();
				int y = rfb.ReadUInt16();
				int width = rfb.ReadUInt16();
				int height = rfb.ReadUInt16();
				FillRectangle(new Rectangle(x, y, width, height), num2);
			}
		}
	}
	public sealed class ZrleRectangle(RfbProtocol rfb, Framebuffer framebuffer, Rectangle rectangle) : EncodedRectangle(rfb, framebuffer, rectangle, 16)
	{
		private const int TILE_WIDTH = 64;

		private const int TILE_HEIGHT = 64;

		private readonly int[] palette = new int[128];

		private readonly int[] tileBuffer = new int[4096];

		public override void Decode()
		{
			rfb.ZrleReader.DecodeStream();
			for (int i = 0; i < rectangle.Height; i += 64)
			{
				int num = Math.Min(rectangle.Height - i, 64);
				for (int j = 0; j < rectangle.Width; j += 64)
				{
					int num2 = Math.Min(rectangle.Width - j, 64);
					byte b = rfb.ZrleReader.ReadByte();
					if ((b >= 17 && b <= 127) || b == 129)
					{
						throw new Exception("Invalid subencoding value");
					}
					bool flag = (b & 0x80) != 0;
					int num3 = b & 0x7F;
					for (int k = 0; k < num3; k++)
					{
						palette[k] = preader.ReadPixel();
					}
					if (num3 == 1)
					{
						FillRectangle(new Rectangle(j, i, num2, num), palette[0]);
					}
					else if (!flag)
					{
						if (num3 == 0)
						{
							FillRectangle(new Rectangle(j, i, num2, num));
							continue;
						}
						ReadZrlePackedPixels(num2, num, palette, num3, tileBuffer);
						FillRectangle(new Rectangle(j, i, num2, num), tileBuffer);
					}
					else if (num3 == 0)
					{
						ReadZrlePlainRLEPixels(num2, num, tileBuffer);
						FillRectangle(new Rectangle(j, i, num2, num), tileBuffer);
					}
					else
					{
						ReadZrlePackedRLEPixels(j, i, num2, num, palette, tileBuffer);
						FillRectangle(new Rectangle(j, i, num2, num), tileBuffer);
					}
				}
			}
		}

		private void ReadZrlePackedPixels(int tw, int th, int[] palette, int palSize, int[] tile)
		{
			int num = ((palSize > 16) ? 8 : ((palSize > 4) ? 4 : ((palSize <= 2) ? 1 : 2)));
			int num2 = 0;
			for (int i = 0; i < th; i++)
			{
				int num3 = num2 + tw;
				int num4 = 0;
				int num5 = 0;
				while (num2 < num3)
				{
					if (num5 == 0)
					{
						num4 = rfb.ZrleReader.ReadByte();
						num5 = 8;
					}
					num5 -= num;
					int num6 = (num4 >> num5) & ((1 << num) - 1) & 0x7F;
					tile[num2++] = palette[num6];
				}
			}
		}

		private void ReadZrlePlainRLEPixels(int tw, int th, int[] tileBuffer)
		{
			int num = 0;
			int num2 = num + tw * th;
			while (num < num2)
			{
				int num3 = preader.ReadPixel();
				int num4 = 1;
				int num5;
				do
				{
					num5 = rfb.ZrleReader.ReadByte();
					num4 += num5;
				}
				while (num5 == 255);
				while (num4-- > 0)
				{
					tileBuffer[num++] = num3;
				}
			}
		}

		private void ReadZrlePackedRLEPixels(int tx, int ty, int tw, int th, int[] palette, int[] tile)
		{
			int num = 0;
			int num2 = num + tw * th;
			while (num < num2)
			{
				int num3 = rfb.ZrleReader.ReadByte();
				int num4 = 1;
				if ((num3 & 0x80) != 0)
				{
					int num5;
					do
					{
						num5 = rfb.ZrleReader.ReadByte();
						num4 += num5;
					}
					while (num5 == 255);
				}
				num3 &= 0x7F;
				while (num4-- > 0)
				{
					tile[num++] = palette[num3];
				}
			}
		}
	}
}
