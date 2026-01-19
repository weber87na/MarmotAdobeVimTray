using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace MarmotAdobeVimTray
{
    static class Program
    {
        // --- Win32 常數 ---
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int KEYEVENTF_KEYUP = 0x0002;
        
        // --- 全域變數 ---
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static NotifyIcon _trayIcon = null!;
        
        private static DateTime _lastGTime = DateTime.MinValue;
        private const int G_TIMEOUT_MS = 600;
        private static bool _isGWaiting = false;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. 初始化系統匣圖示 (使用 marmot.ico)
            _trayIcon = new NotifyIcon()
            {
                Icon = GetAppIcon(),
                ContextMenuStrip = CreateContextMenu(),
                Text = "Adobe Vim 導航器 (Marmot 版)",
                Visible = true
            };

            // 2. 設定鍵盤鉤子
            _hookID = SetHook(_proc);

            // 3. 執行程式 (背景運作)
            Application.Run();

            // 4. 釋放資源
            UnhookWindowsHookEx(_hookID);
            _trayIcon.Dispose();
        }

        private static Icon GetAppIcon()
        {
            try
            {
                if (System.IO.File.Exists("marmot.ico"))
                    return new Icon("marmot.ico");
            }
            catch { }
            return SystemIcons.Application; // 找不到檔案則退回預設
        }

        private static ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("操作指南", null, (s, e) => {
                MessageBox.Show("Vim 導航快速鍵：\n\n" +
                    "h / l : 左右切換分頁\n" +
                    "j / k : 下/上捲動\n" +
                    "d / u : 下/上翻頁\n" +
                    "gg / G : 跳至首/尾\n" +
                    "gt / gT : 切換下/上分頁\n" +
                    "Esc : 重設狀態\n\n" +
                    "智慧偵測：開啟搜尋框時會自動暫停攔截。", "Marmot Adobe Vim");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出程式", null, (s, e) => {
                _trayIcon.Visible = false;
                Application.Exit();
            });
            return menu;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                IntPtr hwnd = GetForegroundWindow();
                string activeProcess = GetProcessName(hwnd);

                // 檢查是否為 Adobe 進程
                if (activeProcess.Contains("acrobat") || activeProcess.Contains("acrord32"))
                {
                    // --- 智慧偵測：搜尋框輸入中則放行 ---
                    if (IsTyping(hwnd))
                    {
                        if (vkCode == (int)Keys.Escape) _isGWaiting = false;
                        return CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    bool isShiftDown = (GetAsyncKeyState(Keys.ShiftKey) & 0x8000) != 0;
                    TimeSpan timeSinceLastG = DateTime.Now - _lastGTime;
                    bool isWithinTimeout = timeSinceLastG.TotalMilliseconds < G_TIMEOUT_MS;

                    // 忽略單獨按下修飾鍵，避免中斷 g 狀態
                    if (IsModifierKey(vkCode)) return CallNextHookEx(_hookID, nCode, wParam, lParam);

                    // 1. 處理 g 系列 (gg, gt, gT)
                    if (vkCode == (int)Keys.G && !isShiftDown)
                    {
                        if (_isGWaiting && isWithinTimeout)
                        {
                            SendKeys.SendWait("^{HOME}"); // gg
                            _isGWaiting = false;
                            return (IntPtr)1;
                        }
                        _isGWaiting = true;
                        _lastGTime = DateTime.Now;
                        return (IntPtr)1;
                    }

                    // 處理 gt / gT
                    if (_isGWaiting && isWithinTimeout && vkCode == (int)Keys.T)
                    {
                        if (isShiftDown) {
                            keybd_event((byte)Keys.ShiftKey, 0, KEYEVENTF_KEYUP, 0); // 解放實體 Shift
                            SendKeys.SendWait("^+{TAB}"); // gT
                        }
                        else { SendKeys.SendWait("^{TAB}"); } // gt
                        _isGWaiting = false;
                        return (IntPtr)1;
                    }

                    _isGWaiting = false; // 按了其他鍵，重設 g 狀態

                    // 2. 處理 h / l (左右切換分頁)
                    if (vkCode == (int)Keys.H) { SendKeys.SendWait("^+{TAB}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.L) { SendKeys.SendWait("^{TAB}"); return (IntPtr)1; }

                    // 3. 處理 G (Shift + G)
                    if (vkCode == (int)Keys.G && isShiftDown)
                    {
                        keybd_event((byte)Keys.ShiftKey, 0, KEYEVENTF_KEYUP, 0);
                        SendKeys.SendWait("^{END}{END}");
                        return (IntPtr)1;
                    }

                    // 4. 其他捲動
                    if (vkCode == (int)Keys.J) { SendKeys.SendWait("{DOWN}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.K) { SendKeys.SendWait("{UP}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.D) { SendKeys.SendWait("{PGDN}"); return (IntPtr)1; }
                    if (vkCode == (int)Keys.U) { SendKeys.SendWait("{PGUP}"); return (IntPtr)1; }

                    if (vkCode == (int)Keys.Escape) _isGWaiting = false;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // --- 輔助工具函數 ---

        private static bool IsTyping(IntPtr hwnd)
        {
            uint threadId = GetWindowThreadProcessId(hwnd, out _);
            GUITHREADINFO guiInfo = new GUITHREADINFO();
            guiInfo.cbSize = Marshal.SizeOf(guiInfo);
            if (GetGUIThreadInfo(threadId, ref guiInfo))
            {
                // 檢查是否有閃爍游標或焦點在輸入控制項上
                return (guiInfo.flags & 0x1) != 0 || guiInfo.hwndCaret != IntPtr.Zero;
            }
            return false;
        }

        private static bool IsModifierKey(int vkCode)
        {
            return vkCode == (int)Keys.ShiftKey || vkCode == (int)Keys.LShiftKey || vkCode == (int)Keys.RShiftKey ||
                   vkCode == (int)Keys.ControlKey || vkCode == (int)Keys.LControlKey || vkCode == (int)Keys.RControlKey;
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static string GetProcessName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            try { return Process.GetProcessById((int)pid).ProcessName.ToLower(); }
            catch { return ""; }
        }

        // --- Win32 API 導入 ---
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO {
            public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus;
            public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize;
            public IntPtr hwndCaret; public Rectangle rcCaret;
        }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(Keys vKey);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    }
}
