using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Helper
{
    public static class LeagueAuthHelper
    {
        private static int _curPid = 0;
        private static DateTime _lastAuthCallTime = DateTime.MinValue;
        private static readonly object _lockObject = new object();

        // Windows API常量
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private const int ProcessCommandLineInformation = 60;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        // Windows API导入
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
            IntPtr processInformation, int processInformationLength, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer,
            int dwSize, out IntPtr lpNumberOfBytesRead);

        /// <summary>
        /// 获取认证信息
        /// </summary>
        public static (string? port, string? token) GetAuth()
        {
            lock (_lockObject)
            {
                // 频率限制检查
                //if ((DateTime.Now - _lastAuthCallTime).TotalSeconds < 3)
                //{
                //    throw new InvalidOperationException("调用过于频繁，请稍后再试");
                //}

                _lastAuthCallTime = DateTime.Now;

                var pids = GetProcessPidByName("LeagueClient");
                if (pids.Count == 0)
                {
                    throw new InvalidOperationException("未找到英雄联盟客户端进程");
                }

                string cmdLine = null;
                bool foundValidProcess = false;

                foreach (var pid in pids)
                {
                    if (pid == _curPid)
                        continue;

                    try
                    {
                        var tempCmdLine = GetProcessCommandLine((uint)pid);
                        if (!string.IsNullOrEmpty(tempCmdLine))
                        {
                            cmdLine = tempCmdLine;
                            _curPid = pid;
                            foundValidProcess = true;
                            break;
                        }
                    }
                    catch
                    {
                        // 忽略单个进程的查询错误，继续尝试其他进程
                    }
                }

                if (!foundValidProcess)
                {
                    if (_curPid > 0)
                    {
                        cmdLine = GetProcessCommandLine((uint)_curPid);
                        if (string.IsNullOrEmpty(cmdLine))
                        {
                            throw new InvalidOperationException("无法获取命令行");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("未找到有效的英雄联盟客户端进程");
                    }
                }

                return AuthResolver(cmdLine);
            }
        }

        /// <summary>
        /// 通过进程名获取PID列表
        /// </summary>
        private static List<int> GetProcessPidByName(string name)
        {
            var pids = new List<int>();
            var nameLower = name.ToLowerInvariant();

            IntPtr hSnapshot = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS
            if (hSnapshot == IntPtr.Zero || hSnapshot == new IntPtr(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建进程快照");
            }

            try
            {
                PROCESSENTRY32 procEntry = new PROCESSENTRY32();
                procEntry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (!Process32First(hSnapshot, ref procEntry))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取第一个进程");
                }

                do
                {
                    if (procEntry.szExeFile.ToLowerInvariant().Contains(nameLower))
                    {
                        pids.Add((int)procEntry.th32ProcessID);
                    }
                }
                while (Process32Next(hSnapshot, ref procEntry));
            }
            finally
            {
                CloseHandle(hSnapshot);
            }

            return pids;
        }

        /// <summary>
        /// 获取进程命令行
        /// </summary>
        private static string GetProcessCommandLine(uint pid)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "wmic";
                    process.StartInfo.Arguments = $"process where ProcessId={pid} get CommandLine /value";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    // 解析输出获取命令行
                    var match = Regex.Match(output, @"CommandLine=([^\r\n]+)");
                    return match.Success ? match.Groups[1].Value : null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析命令行参数
        /// </summary>
        private static (string remotingAuthToken, string appPort) AuthResolver(string commandLine)
        {
            var regex = new Regex(@"--([^\s=]+)(?:=(?:""([^""]+)""|([^\s""]+)))?");
            var matches = regex.Matches(commandLine);
            var parameters = new Dictionary<string, string>();

            foreach (Match match in matches)
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Success ? match.Groups[2].Value :
                              match.Groups[3].Success ? match.Groups[3].Value : "";

                parameters[key] = value;
            }

            if (!parameters.TryGetValue("remoting-auth-token", out string remotingAuthToken) ||
                !parameters.TryGetValue("app-port", out string appPort) ||
                string.IsNullOrEmpty(remotingAuthToken) ||
                string.IsNullOrEmpty(appPort))
            {
                throw new InvalidOperationException("命令行中未找到必要的认证参数");
            }

            return (remotingAuthToken, appPort);
        }
    }
}