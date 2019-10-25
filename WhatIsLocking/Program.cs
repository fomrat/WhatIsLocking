using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

static public class FileUtilities
{
    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    const int RmRebootReasonNone = 0;
    const int CCH_RM_MAX_APP_NAME = 255;
    const int CCH_RM_MAX_SVC_NAME = 63;

    enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint pSessionHandle,
                                          UInt32 nFiles,
                                          string[] rgsFilenames,
                                          UInt32 nApplications,
                                          [In] RM_UNIQUE_PROCESS[] rgApplications,
                                          UInt32 nServices,
                                          string[] rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
    static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint dwSessionHandle,
                                out uint pnProcInfoNeeded,
                                ref uint pnProcInfo,
                                [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
                                ref uint lpdwRebootReasons);
    public static void Main(string[] args)
    {
        string toLock;
        if (args.Length == 0)
        {
            Console.WriteLine("Determines if file is locked for write access." + System.Environment.NewLine + "Usage: WhatIsLocking <fully-qualified filename>" + System.Environment.NewLine +  "(Control-c to End)");
            Console.WriteLine("Enter a fully-qualified filename:");
            toLock = Console.ReadLine();
        }
        else
        {
            toLock = args[0];
        }
        
        // toLock = "C:\\Users\\ll56150\\AppData\\Roaming\\Corp\\ClientCashManagement\\CCM.config";

        // FileStream streamLocked = new FileStream(toLock, FileMode.Open, FileAccess.Read, FileShare.None);
        // streamLocked.Close();

        List<Process> isLocked;
        bool nothingWasLocked = false;
        string wasLocking = string.Empty;

        do
        {
            isLocked = WhatIsLocking(toLock);

            if (isLocked.Count == 0)
            {
                if (nothingWasLocked == false)
                {
                    LogIt("Nothing is locking the file.");
                    nothingWasLocked = true;
                    wasLocking = string.Empty;
                }
            }
            else
            {
                nothingWasLocked = false;
                foreach (var p in isLocked)
                {
                    string isLocking = p.Modules[0].FileName;
                    if (isLocking != wasLocking)
                    {
                        LogIt("Locked by: " + isLocking);
                        wasLocking = isLocking;
                    }
                }
            } 
        } while (true);

        //LogIt("Nothing is locking " + toLock + ".");
        //LogIt("Press a Enter to end.");
        //_ = Console.ReadLine();

    }

    private static void LogIt(string msg)
    {
        Console.WriteLine(DateTime.Now.ToString() + ": " + msg);
    }

    /// <summary>
    /// Find out what process(es) have a lock on the specified file.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>Processes locking the file</returns>
    /// <remarks>See also:
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa373661(v=vs.85).aspx
    /// http://wyupdate.googlecode.com/svn-history/r401/trunk/frmFilesInUse.cs (no copyright in code at time of viewing)
    /// 
    /// </remarks>
    static public List<Process> WhatIsLocking(string path)
    {
        uint handle;
        string key = Guid.NewGuid().ToString();
        List<Process> processes = new List<Process>();

        int res = RmStartSession(out handle, 0, key);
        if (res != 0) throw new Exception("Could not begin restart session.  Unable to determine file locker.");

        try
        {
            const int ERROR_MORE_DATA = 234;
            uint pnProcInfoNeeded = 0,
                 pnProcInfo = 0,
                 lpdwRebootReasons = RmRebootReasonNone;

            string[] resources = new string[] { path }; // Just checking on one resource.

            res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);

            if (res != 0) throw new Exception("Could not register resource.");

            //Note: there's a race condition here -- the first call to RmGetList() returns
            //      the total number of process. However, when we call RmGetList() again to get
            //      the actual processes this number may have increased.
            res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

            if (res == ERROR_MORE_DATA)
            {
                // Create an array to store the process results
                RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                // Get the list
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                if (res == 0)
                {
                    processes = new List<Process>((int)pnProcInfo);

                    // Enumerate all of the results and add them to the 
                    // list to be returned
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        try
                        {
                            processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                        }
                        // catch the error -- in case the process is no longer running
                        catch (ArgumentException) { }
                    }
                }
                else throw new Exception("Could not list processes locking resource.");
            }
            else if (res != 0) throw new Exception("Could not list processes locking resource. Failed to get size of result.");
        }
        finally
        {
            RmEndSession(handle);
        }

        return processes;
    }
}

