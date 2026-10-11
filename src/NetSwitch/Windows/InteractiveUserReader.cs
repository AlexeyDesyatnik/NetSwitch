using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NetSwitch.Model;

namespace NetSwitch.Windows
{
    /// <summary>
    /// Определяет интерактивного пользователя. Когда права администратора получены вводом чужих
    /// учётных данных, процесс принадлежит администратору, но остаётся в сеансе того, кто вошёл в систему.
    /// </summary>
    internal static class InteractiveUserReader
    {
        private const int WtsUserName = 5;
        private const int WtsDomainName = 7;
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int TokenQuery = 0x8;

        public static InteractiveUser Read()
        {
            int session = CurrentSession();
            string user = SessionText(session, WtsUserName);
            string domain = SessionText(session, WtsDomainName);
            if (user.Length == 0)
                throw new InvalidOperationException("В сеансе " + session + " нет вошедшего пользователя.");

            string name = domain.Length > 0 ? domain + "\\" + user : user;
            var sid = (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
            return new InteractiveUser(name, sid.Value, sid.Value == ProcessOwnerSid());
        }

        /// <summary>
        /// Второй способ — по владельцу оболочки (explorer.exe) в том же сеансе.
        /// Нужен только для сверки в отчёте.
        /// </summary>
        public static InteractiveUser? ReadFromShell()
        {
            int session = CurrentSession();
            try
            {
                // Чужой процесс администратор может открыть только с привилегией отладки
                Process.EnterDebugMode();
            }
            catch (Win32Exception)
            {
            }

            foreach (Process shell in Process.GetProcessesByName("explorer"))
            {
                using (shell)
                {
                    if (shell.SessionId != session) continue;

                    IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, shell.Id);
                    if (process == IntPtr.Zero) throw new Win32Exception();
                    try
                    {
                        IntPtr token;
                        if (!OpenProcessToken(process, TokenQuery, out token)) throw new Win32Exception();
                        try
                        {
                            using (var identity = new WindowsIdentity(token))
                            {
                                string sid = identity.User != null ? identity.User.Value : "";
                                return new InteractiveUser(identity.Name, sid, sid == ProcessOwnerSid());
                            }
                        }
                        finally
                        {
                            CloseHandle(token);
                        }
                    }
                    finally
                    {
                        CloseHandle(process);
                    }
                }
            }
            return null;
        }

        public static int CurrentSession()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                return current.SessionId;
            }
        }

        public static string ProcessOwnerName()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity.Name;
            }
        }

        private static string ProcessOwnerSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity.User != null ? identity.User.Value : "";
            }
        }

        private static string SessionText(int session, int infoClass)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out buffer, out bytes))
                throw new Win32Exception();
            try
            {
                return Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
