using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;

namespace MarmotAdobeVimTray
{
    static class Program
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int KEYEVENTF_KEYUP = 0x0002;
        
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static NotifyIcon _trayIcon = null!;
        
        private static DateTime _lastGTime = DateTime.MinValue;
        private static DateTime _lastColonTime = DateTime.MinValue; 
        private const int G_TIMEOUT_MS = 600;
        private const int COLON_TIMEOUT_MS = 1000; 

        private static bool _isGWaiting = false;
        private static bool _isColonWaiting = false; 

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _trayIcon = new NotifyIcon()
            {
                Icon = GetAppIcon(),
                ContextMenuStrip = CreateContextMenu(),
                Text = "MarmotAdobeVimTray",
                Visible = true
            };

            _hookID = SetHook(_proc);
            Application.Run();

            UnhookWindowsHookEx(_hookID);
            _trayIcon.Dispose();
        }

        private static Icon GetAppIcon()
        {
            try { if (System.IO.File.Exists("marmot.ico")) return new Icon("marmot.ico"); }
            catch { }
            return SystemIcons.Application;
        }

        private static ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("操作指南", null, (s, e) => {
                MessageBox.Show("Vim 智慧關閉版：\n\n" +
                    ":q  -> 關閉分頁；若無分頁則退出程式\n" +
                    "h / l : 左右分頁\n" +
                    "j / k : 上下捲動\n" +
                    "d / y : 上下翻頁\n" +
                    "gg / G : 首尾\n" +
                    "Esc : 重設狀態", "MarmotAdobeVimTray");
            });
            menu.Items.Add("-");
            menu.Items.Add("退出本工具", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); });
            return menu;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                IntPtr hwnd = GetForegroundWindow();
                string activeProcess = GetProcessName(hwnd);

                if (activeProcess.Contains("acrobat") || activeProcess.Contains("acrord32"))
                {
                    if (IsTyping(hwnd))
                    {
                        if (vkCode == (int)Keys.Escape) ResetStates();
                        return CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    bool isShiftDown = (GetAsyncKeyState(Keys.ShiftKey) & 0x8000) != 0;
                    if (IsModifierKey(vkCode)) return CallNextHookEx(_hookID, nCode, wParam, lParam);

                    // --- 處理 :q 智慧關閉 ---
                    if (_isColonWaiting)
                    {
                        if ((DateTime.Now - _lastColonTime).TotalMilliseconds < COLON_TIMEOUT_MS && vkCode == (int)Keys.Q)
                        {
                            HandleVimQuit(hwnd); // 執行智慧退出邏輯
                            ResetStates();
                            return (IntPtr)1;
                        }
                        ResetStates();
                    }

                    // 觸發冒號
                    if (vkCode == (int)Keys.OemSemicolon && isShiftDown)
                    {
                        _isColonWaiting = true;
                        _lastColonTime = DateTime.Now;
                        _isGWaiting = false;
                        return (IntPtr)1;
                    }

                    // --- 處理 G 狀態 ---
                    if (_isGWaiting)
                    {
                        if ((DateTime.Now - _lastGTime).TotalMilliseconds < G_TIMEOUT_MS)
                        {
                            if (vkCode == (int)Keys.G && !isShiftDown) { SendKeys.SendWait("^{HOME}"); ResetStates(); return (IntPtr)1; }
                            if (vkCode == (int)Keys.T) {
                                if (isShiftDown) { keybd_event((byte)Keys.ShiftKey, 0, KEYEVENTF_KEYUP, 0); SendKeys.SendWait("^+{TAB}"); }
                                else { SendKeys.SendWait("^{TAB}"); }
                                ResetStates(); return (IntPtr)1;
                            }
                        }
                        ResetStates();
                    }

                    if (vkCode == (int)Keys.G && !isShiftDown)
                    {
                        _isGWaiting = true;
                        _lastGTime = DateTime.Now;
                        return (IntPtr)1;
                    }

                    // --- 基礎按鍵 ---
                    if (vkCode == (int)Keys.H) { SendKeys.SendWait("^+{TAB}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.L) { SendKeys.SendWait("^{TAB}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.G && isShiftDown) { keybd_event((byte)Keys.ShiftKey, 0, KEYEVENTF_KEYUP, 0); SendKeys.SendWait("^{END}{END}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.J) { SendKeys.SendWait("{DOWN}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.K) { SendKeys.SendWait("{UP}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.D) { SendKeys.SendWait("{PGDN}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.U) { SendKeys.SendWait("{PGUP}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.Escape) ResetStates();
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // --- 核心邏輯：智慧退出 ---
        private static async void HandleVimQuit(IntPtr hwnd)
        {
            // 1. 先關閉目前分頁
            SendKeys.SendWait("^w");

            // 2. 等待 Adobe 更新 UI 標題
            await Task.Delay(350);

            // 3. 獲取關閉分頁後的最新標題
            string currentTitle = GetWindowTitle(hwnd);

            // 4. 判斷標題。如果沒有檔案特有的符號 " - " 或 ".pdf"，通常代表回到了首頁（無文件狀態）
            if (!currentTitle.Contains(" - ") && !currentTitle.ToLower().Contains(".pdf"))
            {
                // 送出 Ctrl + Q 關閉整個程式
                SendKeys.SendWait("^q");
            }
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);
            if (GetWindowText(hwnd, buff, nChars) > 0)
            {
                return buff.ToString();
            }
            return "";
        }

        private static void ResetStates() { _isGWaiting = false; _isColonWaiting = false; }

        private static bool IsTyping(IntPtr hwnd)
        {
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            GUITHREADINFO guiInfo = new GUITHREADINFO();
            guiInfo.cbSize = Marshal.SizeOf(guiInfo);
            if (GetGUIThreadInfo(threadId, ref guiInfo))
                return (guiInfo.flags & 0x1) != 0 || guiInfo.hwndCaret != IntPtr.Zero;
            return false;
        }

        private static bool IsModifierKey(int vkCode) => 
            vkCode == (int)Keys.ShiftKey || vkCode == (int)Keys.LShiftKey || vkCode == (int)Keys.RShiftKey ||
            vkCode == (int)Keys.ControlKey || vkCode == (int)Keys.LControlKey || vkCode == (int)Keys.RControlKey;

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private static string GetProcessName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            try { return Process.GetProcessById((int)pid).ProcessName.ToLower(); }
            catch { return ""; }
        }

        // --- Win32 API ---
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO { public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus; public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize; public IntPtr hwndCaret; public Rectangle rcCaret; }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(Keys vKey);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
