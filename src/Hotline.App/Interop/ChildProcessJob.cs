using System.Diagnostics;
using System.Runtime.InteropServices;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>Job object with kill-on-close: child processes (agy) die with Hotline, even on a crash.</summary>
internal sealed class ChildProcessJob
{
    private readonly nint _job;
    private readonly FileLog _log;

    public ChildProcessJob(FileLog log)
    {
        _log = log;
        _job = Native.CreateJobObject(0, null);
        var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (_job == 0 || !Native.SetInformationJobObject(_job, Native.JobObjectExtendedLimitInformation, ref info,
                Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            log.Error($"child process job setup failed (error {Marshal.GetLastWin32Error()}); agy may outlive a crash");
    }

    /// <summary>Whether a process is in the job: one Hotline started (a CLI) or something that CLI started.</summary>
    public bool Owns(uint processId)
    {
        if (_job == 0 || processId == 0) return false;
        var process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return false;
        try { return Native.IsProcessInJob(process, _job, out var inJob) && inJob; }
        finally { Native.CloseHandle(process); }
    }

    public void Add(Process process)
    {
        if (_job != 0 && !Native.AssignProcessToJobObject(_job, process.Handle))
            _log.Error($"could not add process {process.Id} to job");
    }
}
