﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Reflection;

namespace UEVR {
    class Injector {

        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, [In] ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport ( "ntdll.dll" )]
        public static extern uint RtlAdjustPrivilege ( uint Privilege, bool bEnablePrivilege, bool IsThreadPrivilege, out bool PreviousValue );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        // Inject the DLL into the target process
        // dllPath is local filename, relative to EXE.
        public static bool InjectDll(int processId, string dllPath, out IntPtr dllBase) {
            //use old behavior if we just pass a filename otherwise if passing a path we just try to inject the remote path
            if ( !dllPath.Contains ( "\\" ))
                {
                string originalPath = dllPath;
                try
                    {
                    var exeDirectory = AppContext.BaseDirectory;

                    if ( exeDirectory != null )
                        {
                        var newPath = Path.Combine ( exeDirectory, dllPath );

                        if ( System.IO.File.Exists ( newPath ) )
                            {
                            dllPath = Path.Combine ( exeDirectory, dllPath );
                            }
                        }
                    }
                catch ( Exception )
                    {
                    }

                if ( !System.IO.File.Exists ( dllPath ) )
                    {
                    MessageBox.Show ( $"{originalPath} does not appear to exist! Check if any anti-virus software has deleted the file. Reinstall UEVR if necessary.\n\nBaseDirectory: {AppContext.BaseDirectory}" );
                    }
                }

            RtlAdjustPrivilege ( 20, true, IsThreadPrivilege: false, out var _ );

            dllBase = IntPtr.Zero;

            string fullPath = Path.GetFullPath(dllPath);

            // Open the target process with the necessary access
            IntPtr processHandle = OpenProcess(0x1F0FFF, false, processId);

            if (processHandle == IntPtr.Zero) {
                MessageBox.Show("Could not open a handle to the target process.\nYou may need to start this program as an administrator, or the process may be protected.");
                return false;
            }

            // Get the address of the LoadLibrary function
            //IntPtr loadLibraryAddress = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            IntPtr loadLibraryAddress = GetProcAddress ( LoadLibrary ( "kernel32.dll" ), "LoadLibraryW" );
            if (loadLibraryAddress == IntPtr.Zero) {
                MessageBox.Show("Could not obtain LoadLibraryW address in the target process.");
                return false;
            }

            // Allocate memory in the target process for the DLL path
            IntPtr dllPathAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)fullPath.Length, 0x1000, 0x40);

            if (dllPathAddress == IntPtr.Zero) {
                MessageBox.Show("Failed to allocate memory in the target process.");
                return false;
            }

            // Write the DLL path in UTF-16
            int bytesWritten = 0;
            var bytes = Encoding.Unicode.GetBytes(fullPath);
            WriteProcessMemory(processHandle, dllPathAddress, bytes, (uint)(fullPath.Length * 2), out bytesWritten);

            // Create a remote thread in the target process that calls LoadLibrary with the DLL path
            IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddress, dllPathAddress, 0,IntPtr.Zero);

            if (threadHandle == IntPtr.Zero) {
                MessageBox.Show("Failed to create remote thread in the target processs.");
                return false;
            }

            WaitForSingleObject(threadHandle, 1000);

            Process p = Process.GetProcessById(processId);

            // Get base of DLL that was just injected
            if (p != null) try {
                foreach (ProcessModule module in p.Modules) {
                    if (module.FileName != null && module.FileName == fullPath) {
                        dllBase = module.BaseAddress;
                        break;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
                MessageBox.Show($"Exception while injecting: {ex}");
            }

            return true;
        }

        public static bool InjectDll(int processId, string dllPath) {
            IntPtr dummy;
            return InjectDll(processId, dllPath, out dummy);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        // FreeLibrary
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool FreeLibrary(IntPtr hModule);

        public static bool CallFunctionNoArgs(int processId, string dllPath, IntPtr dllBase, string functionName, bool wait = false) {
            IntPtr processHandle = OpenProcess(0x1F0FFF, false, processId);

            if (processHandle == IntPtr.Zero) {
                MessageBox.Show("Could not open a handle to the target process.\nYou may need to start this program as an administrator, or the process may be protected.");
                return false;
            }

            // We need to load the DLL into our own process temporarily as a workaround for GetProcAddress not working with remote DLLs
            IntPtr localDllHandle = LoadLibrary(dllPath);

            if (localDllHandle == IntPtr.Zero) {
                MessageBox.Show("Could not load the target DLL into our own process.");
                return false;
            }

            IntPtr localVa = GetProcAddress(localDllHandle, functionName);

            if (localVa == IntPtr.Zero) {
                MessageBox.Show("Could not obtain " + functionName + " address in our own process.");
                return false;
            }

            IntPtr rva = (IntPtr)(localVa.ToInt64() - localDllHandle.ToInt64());
            IntPtr functionAddress = (IntPtr)(dllBase.ToInt64() + rva.ToInt64());

            // Create a remote thread in the target process that calls the function
            IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, functionAddress, IntPtr.Zero, 0, IntPtr.Zero);

            if (threadHandle == IntPtr.Zero) {
                MessageBox.Show("Failed to create remote thread in the target processs.");
                return false;
            }

            if (wait) {
                WaitForSingleObject(threadHandle, 2000);
            }

            return true;
        }


        [Flags]
        private enum SnapshotFlags : uint
            {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            Inherit = 0x80000000,
            All = 0x0000001F,
            NoHeaps = 0x40000000
            }

        [DllImport ( "toolhelp.dll" )]
        private static extern IntPtr CreateToolhelp32Snapshot ( SnapshotFlags dwFlags, int th32ProcessID );

        [StructLayout ( LayoutKind.Sequential )]
        public struct PROCESSENTRY32
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
            [MarshalAs ( UnmanagedType.ByValTStr, SizeConst = 260 )] public string szExeFile;
            };



        [DllImport ( "psapi.dll", SetLastError = true )]
        public static extern bool EnumProcessModulesEx ( IntPtr hProcess, [Out] IntPtr [ ] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag );


        [DllImport ( "kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode )]
        public static extern bool CreateProcessWithToken ( string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, [In] ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation );


        }
}