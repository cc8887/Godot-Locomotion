param(
    [string]$RepositoryRoot,
    [string]$FixturePath,
    [string]$StagingRoot,
    [scriptblock]$ProcessInvoker,
    [scriptblock]$CheckpointInvoker,
    [int]$TimeoutSeconds = 28800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-P5aVerifierDefaultProcess
{
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$PhaseName
    )

    $nativeType = 'P5aVerifierPreboundRunnerNative' -as [type]
    if ($null -eq $nativeType)
    {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class P5aVerifierPreboundRunnerNative
{
    private const long MaxStdOutBytes = 8388608;
    private const long MaxStdErrBytes = 8388608;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const int PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x0002000D;
    private const int PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    private const uint JOB_OBJECT_MSG_NEW_PROCESS = 6;
    private const uint MaxActiveProcesses = 128;
    private const int MaxTrackedProcesses = 4096;
    private const int MaxProcessDepth = 64;
    private const int MaxExecutablePathCharacters = 8388608;
    private const int MaxAncestorEdges = 262144;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x00001000;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_TIMEOUT = 258;
    private const uint STILL_ACTIVE = 259;
    private const int ERROR_BROKEN_PIPE = 109;
    private const int ERROR_NO_DATA = 232;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompletionAssociation
    {
        public IntPtr CompletionKey;
        public IntPtr CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessTime;
        public long PerJobTime;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSet;
        public UIntPtr MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    public sealed class DescendantRecord
    {
        public int processId { get; set; }
        public int parentProcessId { get; set; }
        public int[] ancestorProcessIds { get; set; }
        public string imageName { get; set; }
        public string executablePath { get; set; }
    }

    public sealed class RunnerResult
    {
        public int ProcessId { get; set; }
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public bool OutputLimitExceeded { get; set; }
        public string[] StdOutLines { get; set; }
        public string[] StdErrLines { get; set; }
        public long StdOutBytes { get; set; }
        public long StdErrBytes { get; set; }
        public DescendantRecord[] DescendantProcesses { get; set; }
        public int JobTotalProcesses { get; set; }
        public int JobActiveProcesses { get; set; }
        public int[] JobProcessIds { get; set; }
    }

    private sealed class ProcessMetadata : IDisposable
    {
        public int ProcessId;
        public int ParentProcessId;
        public string ImageName;
        public string ExecutablePath;
        public IntPtr Handle;

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) CloseHandle(Handle);
            Handle = IntPtr.Zero;
        }
    }

    private sealed class SharedState
    {
        public readonly Stopwatch Clock;
        public readonly IntPtr Job;
        public readonly ManualResetEventSlim FailureEvent = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim OutputLimitEvent = new ManualResetEventSlim(false);
        public readonly AutoResetEvent ReaderPulse = new AutoResetEvent(false);
        public long DeadlineMilliseconds;
        public int StopReaders;
        public Exception Failure;
        public int OutputLimit;

        public SharedState(Stopwatch clock, IntPtr job, long deadlineMilliseconds)
        {
            Clock = clock;
            Job = job;
            DeadlineMilliseconds = deadlineMilliseconds;
        }

        public void Fail(Exception error)
        {
            if (Interlocked.CompareExchange(ref Failure, error, null) == null)
                FailureEvent.Set();
            TerminateJobObject(Job, 0xE0000001);
        }

        public void ExceedOutputLimit()
        {
            if (Interlocked.Exchange(ref OutputLimit, 1) == 0)
                OutputLimitEvent.Set();
            TerminateJobObject(Job, 0xE0000002);
        }
    }

    private sealed class StreamCapture
    {
        private readonly IntPtr pipe;
        private readonly long maximumBytes;
        private readonly SharedState state;
        private readonly MemoryStream bytes = new MemoryStream();
        public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Completed = new ManualResetEventSlim(false);
        public Thread Thread;
        public long ByteCount { get { return bytes.Length; } }

        public StreamCapture(IntPtr pipe, long maximumBytes, SharedState state)
        {
            this.pipe = pipe;
            this.maximumBytes = maximumBytes;
            this.state = state;
        }

        public void Start(string name)
        {
            Thread = new Thread(ReadPipe);
            Thread.IsBackground = true;
            Thread.Name = name;
            Thread.Start();
        }

        private void ReadPipe()
        {
            Ready.Set();
            byte[] buffer = new byte[8192];
            try
            {
                while (Volatile.Read(ref state.StopReaders) == 0)
                {
                    uint available;
                    if (!PeekNamedPipe(pipe, null, 0, IntPtr.Zero, out available, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ERROR_BROKEN_PIPE || error == ERROR_NO_DATA) break;
                        throw new Win32Exception(error);
                    }
                    if (available == 0)
                    {
                        int pause = RemainingMilliseconds(
                            state.Clock, Volatile.Read(ref state.DeadlineMilliseconds), 2);
                        if (pause > 0) state.ReaderPulse.WaitOne(pause);
                        else Thread.Yield();
                        continue;
                    }
                    int requested = (int)Math.Min((uint)buffer.Length, available);
                    uint read;
                    if (!ReadFile(pipe, buffer, (uint)requested, out read, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ERROR_BROKEN_PIPE || error == ERROR_NO_DATA) break;
                        throw new Win32Exception(error);
                    }
                    if (read == 0) continue;
                    long remaining = maximumBytes - bytes.Length;
                    int retained = (int)Math.Min((long)read, Math.Max(0, remaining));
                    if (retained > 0) bytes.Write(buffer, 0, retained);
                    if ((long)read > remaining)
                    {
                        state.ExceedOutputLimit();
                        break;
                    }
                }
            }
            catch (Exception error)
            {
                state.Fail(new InvalidOperationException("P5A stream capture failed.", error));
            }
            finally
            {
                Completed.Set();
            }
        }

        public string[] Lines()
        {
            string text = new UTF8Encoding(false, false).GetString(bytes.ToArray());
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.Length != 0).ToArray();
        }

        public void DisposeResources()
        {
            Ready.Dispose();
            Completed.Dispose();
            bytes.Dispose();
        }
    }

    private sealed class CollectorState
    {
        private readonly object sync = new object();
        private readonly Dictionary<int, ProcessMetadata> metadata =
            new Dictionary<int, ProcessMetadata>();
        private readonly HashSet<int> jobProcessIds = new HashSet<int>();
        private readonly SharedState shared;
        private readonly IntPtr port;
        private readonly int rootProcessId;
        private int executablePathCharacters;
        public readonly ManualResetEventSlim Ready = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim ActiveZero = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Drained = new ManualResetEventSlim(false);
        public Thread Thread;
        public int Stop;
        public bool RootNotificationSeen;

        public CollectorState(SharedState shared, IntPtr port, int rootProcessId)
        {
            this.shared = shared;
            this.port = port;
            this.rootProcessId = rootProcessId;
            jobProcessIds.Add(rootProcessId);
        }

        public void Start()
        {
            Thread = new Thread(Collect);
            Thread.IsBackground = true;
            Thread.Name = "P5A verifier job metadata collector";
            Thread.Start();
        }

        private void Collect()
        {
            Ready.Set();
            while (Volatile.Read(ref Stop) == 0)
            {
                uint message;
                IntPtr key;
                IntPtr value;
                int wait = RemainingMilliseconds(
                    shared.Clock, Volatile.Read(ref shared.DeadlineMilliseconds), 10);
                bool dequeued = GetQueuedCompletionStatus(
                    port, out message, out key, out value, (uint)Math.Max(0, wait));
                if (!dequeued)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == (int)WAIT_TIMEOUT)
                    {
                        if (ActiveZero.IsSet) Drained.Set();
                        if (wait == 0) Thread.Yield();
                        continue;
                    }
                    shared.Fail(new Win32Exception(error));
                    Drained.Set();
                    return;
                }
                Drained.Reset();
                if (message == 0 && key == new IntPtr(-1)) continue;
                if (message == JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO)
                {
                    ActiveZero.Set();
                    continue;
                }
                if (message != JOB_OBJECT_MSG_NEW_PROCESS) continue;
                int processId = unchecked((int)value.ToInt64());
                try { CaptureMetadata(processId); }
                catch (Exception error)
                {
                    shared.Fail(new InvalidOperationException(
                        "P5A descendant metadata capture failed for process " + processId + ".",
                        error));
                }
            }
        }

        private void CaptureMetadata(int processId)
        {
            if (processId <= 0) throw new InvalidOperationException("metadata process id is invalid.");
            lock (sync)
            {
                if (processId == rootProcessId)
                {
                    if (RootNotificationSeen)
                        throw new InvalidOperationException("duplicate root metadata notification.");
                    RootNotificationSeen = true;
                    return;
                }
                if (jobProcessIds.Count >= MaxTrackedProcesses)
                    throw new InvalidOperationException(
                        "P5A job exceeded its cumulative process metadata limit.");
                if (!jobProcessIds.Add(processId))
                    throw new InvalidOperationException("duplicate descendant metadata notification.");
            }

            IntPtr handle = OpenProcess(
                PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, (uint)processId);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var path = new StringBuilder(32768);
                uint pathLength = (uint)path.Capacity;
                if (!QueryFullProcessImageNameW(handle, 0, path, ref pathLength))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                string executablePath = path.ToString();
                if (executablePath.Length == 0 || !Path.IsPathFullyQualified(executablePath))
                    throw new InvalidOperationException("metadata executable path is missing or relative.");

                var basic = new PROCESS_BASIC_INFORMATION();
                int returned;
                int status = NtQueryInformationProcess(
                    handle, 0, ref basic, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)),
                    out returned);
                if (status != 0)
                    throw new InvalidOperationException(
                        "metadata parent query failed with NTSTATUS 0x" + status.ToString("x8") + ".");
                long parentValue = basic.InheritedFromUniqueProcessId.ToInt64();
                if (parentValue <= 0 || parentValue > Int32.MaxValue)
                    throw new InvalidOperationException("metadata parent process id is invalid.");

                var record = new ProcessMetadata
                {
                    ProcessId = processId,
                    ParentProcessId = (int)parentValue,
                    ImageName = Path.GetFileName(executablePath),
                    ExecutablePath = executablePath,
                    Handle = handle
                };
                if (String.IsNullOrEmpty(record.ImageName))
                    throw new InvalidOperationException("metadata image name is missing.");
                lock (sync)
                {
                    if ((long)executablePathCharacters + executablePath.Length >
                        MaxExecutablePathCharacters)
                        throw new InvalidOperationException(
                            "P5A job exceeded its cumulative executable path limit.");
                    metadata.Add(processId, record);
                    executablePathCharacters += executablePath.Length;
                }
                handle = IntPtr.Zero;
            }
            finally
            {
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }

        public ProcessMetadata[] SnapshotMetadata()
        {
            lock (sync) return metadata.Values.OrderBy(item => item.ProcessId).ToArray();
        }

        public int[] SnapshotJobProcessIds()
        {
            lock (sync) return jobProcessIds.OrderBy(item => item).ToArray();
        }

        public void DisposeMetadata()
        {
            foreach (ProcessMetadata item in SnapshotMetadata()) item.Dispose();
        }

        public void DisposeSignals()
        {
            Ready.Dispose();
            ActiveZero.Dispose();
            Drained.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(
        IntPtr fileHandle, IntPtr existingCompletionPort, IntPtr completionKey,
        uint numberOfConcurrentThreads);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint informationLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass,
        ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information,
        int informationLength, IntPtr returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatus(
        IntPtr completionPort, out uint numberOfBytes,
        out IntPtr completionKey, out IntPtr overlapped, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PostQueuedCompletionStatus(
        IntPtr completionPort, uint numberOfBytes,
        IntPtr completionKey, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr readPipe, out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES pipeAttributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(
        IntPtr pipe, byte[] buffer, uint bufferSize,
        IntPtr bytesRead, out uint totalBytesAvailable, IntPtr bytesLeftThisMessage);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr file, byte[] buffer, uint bytesToRead,
        out uint bytesRead, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList, int attributeCount, int flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute,
        IntPtr value, UIntPtr size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string currentDirectory,
        ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr process, uint flags, StringBuilder executablePath, ref uint size);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process, int informationClass,
        ref PROCESS_BASIC_INFORMATION information, int informationLength,
        out int returnLength);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static int RemainingMilliseconds(
        Stopwatch clock, long deadlineMilliseconds, int maximumMilliseconds)
    {
        long remaining = deadlineMilliseconds - clock.ElapsedMilliseconds;
        if (remaining <= 0) return 0;
        return (int)Math.Min(remaining, maximumMilliseconds);
    }

    private static bool WaitForJobEmpty(
        IntPtr job, Stopwatch clock, long deadlineMilliseconds)
    {
        while (true)
        {
            var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
            if (!QueryInformationJobObject(
                    job, 1, ref accounting,
                    Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)),
                    IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (accounting.ActiveProcesses == 0) return true;
            int remaining = RemainingMilliseconds(clock, deadlineMilliseconds, 10);
            if (remaining <= 0) return false;
            Thread.Sleep(Math.Min(2, remaining));
        }
    }

    private static void Close(ref IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != INVALID_HANDLE_VALUE) CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    private static void ConfigureJob(IntPtr job, IntPtr port)
    {
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
            JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
        limits.Basic.ActiveProcessLimit = MaxActiveProcesses;
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ExtendedLimits)));
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(
                job, 9, buffer, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(buffer); }

        var association = new CompletionAssociation
        {
            CompletionKey = new IntPtr(1),
            CompletionPort = port
        };
        buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CompletionAssociation)));
        try
        {
            Marshal.StructureToPtr(association, buffer, false);
            if (!SetInformationJobObject(
                job, 7, buffer, (uint)Marshal.SizeOf(typeof(CompletionAssociation))))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void MakePipe(
        ref SECURITY_ATTRIBUTES attributes, out IntPtr parentRead, out IntPtr childWrite)
    {
        if (!CreatePipe(out parentRead, out childWrite, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetHandleInformation(parentRead, HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static string QuoteArgument(string value)
    {
        if (value == null) value = String.Empty;
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            return value;
        var result = new StringBuilder();
        result.Append('"');
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1);
                result.Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes);
            slashes = 0;
            result.Append(character);
        }
        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private static StringBuilder BuildCommandLine(string filePath, string[] arguments)
    {
        var command = new StringBuilder(QuoteArgument(filePath));
        foreach (string argument in arguments ?? new string[0])
        {
            command.Append(' ');
            command.Append(QuoteArgument(argument));
        }
        return command;
    }

    private static DescendantRecord[] BuildDescendants(
        int rootProcessId, ProcessMetadata[] metadata)
    {
        var byId = metadata.ToDictionary(item => item.ProcessId);
        var result = new List<DescendantRecord>();
        int ancestorEdges = 0;
        foreach (ProcessMetadata item in metadata.OrderBy(value => value.ProcessId))
        {
            var reverseAncestors = new List<int>();
            var seen = new HashSet<int>();
            int cursor = item.ParentProcessId;
            while (cursor > 0 && seen.Add(cursor))
            {
                if (reverseAncestors.Count >= MaxProcessDepth)
                    throw new InvalidOperationException(
                        "P5A descendant metadata exceeded its maximum process depth.");
                reverseAncestors.Add(cursor);
                ancestorEdges = checked(ancestorEdges + 1);
                if (ancestorEdges > MaxAncestorEdges)
                    throw new InvalidOperationException(
                        "P5A descendant metadata exceeded its cumulative ancestor edge limit.");
                if (cursor == rootProcessId) break;
                ProcessMetadata parent;
                if (!byId.TryGetValue(cursor, out parent))
                    throw new InvalidOperationException(
                        "P5A descendant metadata parent is outside the audited job subtree.");
                cursor = parent.ParentProcessId;
            }
            if (reverseAncestors.Count == 0 ||
                reverseAncestors[reverseAncestors.Count - 1] != rootProcessId)
                throw new InvalidOperationException(
                    "P5A descendant metadata has no audited root ancestor.");
            reverseAncestors.Reverse();
            result.Add(new DescendantRecord
            {
                processId = item.ProcessId,
                parentProcessId = item.ParentProcessId,
                ancestorProcessIds = reverseAncestors.ToArray(),
                imageName = item.ImageName,
                executablePath = item.ExecutablePath
            });
        }
        return result.ToArray();
    }

    public static RunnerResult Run(
        string filePath, string[] arguments, string workingDirectory,
        int timeoutSeconds, string phaseName)
    {
        if (String.IsNullOrWhiteSpace(filePath) || !Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("P5A child executable path must be absolute.");
        if (String.IsNullOrWhiteSpace(workingDirectory) ||
            !Path.IsPathFullyQualified(workingDirectory))
            throw new ArgumentException("P5A child working directory must be absolute.");
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException("timeoutSeconds");

        IntPtr job = IntPtr.Zero;
        IntPtr port = IntPtr.Zero;
        IntPtr stdinRead = IntPtr.Zero;
        IntPtr stdinWrite = IntPtr.Zero;
        IntPtr stdoutRead = IntPtr.Zero;
        IntPtr stdoutWrite = IntPtr.Zero;
        IntPtr stderrRead = IntPtr.Zero;
        IntPtr stderrWrite = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr jobListValue = IntPtr.Zero;
        IntPtr handleListValue = IntPtr.Zero;
        var processInformation = new PROCESS_INFORMATION();
        bool attributeListInitialized = false;
        SharedState shared = null;
        StreamCapture stdoutCapture = null;
        StreamCapture stderrCapture = null;
        CollectorState collector = null;
        bool processCreated = false;
        bool timedOut = false;

        try
        {
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            port = CreateIoCompletionPort(INVALID_HANDLE_VALUE, IntPtr.Zero, IntPtr.Zero, 1);
            if (port == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            ConfigureJob(job, port);

            var pipeAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                bInheritHandle = true
            };
            MakePipe(ref pipeAttributes, out stdoutRead, out stdoutWrite);
            MakePipe(ref pipeAttributes, out stderrRead, out stderrWrite);
            if (!CreatePipe(out stdinRead, out stdinWrite, ref pipeAttributes, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            UIntPtr attributeBytes = UIntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeBytes);
            attributeList = Marshal.AllocHGlobal(checked((int)attributeBytes.ToUInt64()));
            if (!InitializeProcThreadAttributeList(attributeList, 2, 0, ref attributeBytes))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            attributeListInitialized = true;
            jobListValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobListValue, job);
            handleListValue = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handleListValue, 0 * IntPtr.Size, stdinRead);
            Marshal.WriteIntPtr(handleListValue, 1 * IntPtr.Size, stdoutWrite);
            Marshal.WriteIntPtr(handleListValue, 2 * IntPtr.Size, stderrWrite);

            if (!UpdateProcThreadAttribute(
                attributeList, 0, new IntPtr(PROC_THREAD_ATTRIBUTE_JOB_LIST),
                jobListValue, new UIntPtr((uint)IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!UpdateProcThreadAttribute(
                attributeList, 0, new IntPtr(PROC_THREAD_ATTRIBUTE_HANDLE_LIST),
                handleListValue, new UIntPtr((uint)(IntPtr.Size * 3)),
                IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            startup.StartupInfo.dwFlags = (int)STARTF_USESTDHANDLES;
            startup.StartupInfo.hStdInput = stdinRead;
            startup.StartupInfo.hStdOutput = stdoutWrite;
            startup.StartupInfo.hStdError = stderrWrite;
            startup.lpAttributeList = attributeList;
            uint flags = CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW;
            if (!CreateProcessW(
                filePath, BuildCommandLine(filePath, arguments),
                IntPtr.Zero, IntPtr.Zero, true, flags, IntPtr.Zero,
                workingDirectory, ref startup, out processInformation))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            processCreated = true;

            Close(ref stdinRead);
            Close(ref stdinWrite);
            Close(ref stdoutWrite);
            Close(ref stderrWrite);
            DeleteProcThreadAttributeList(attributeList);
            attributeListInitialized = false;
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;
            Marshal.FreeHGlobal(jobListValue);
            jobListValue = IntPtr.Zero;
            Marshal.FreeHGlobal(handleListValue);
            handleListValue = IntPtr.Zero;

            var clock = Stopwatch.StartNew();
            long runDeadline = checked((long)timeoutSeconds * 1000L);
            shared = new SharedState(clock, job, runDeadline);
            int rootProcessId = checked((int)processInformation.dwProcessId);
            stdoutCapture = new StreamCapture(stdoutRead, MaxStdOutBytes, shared);
            stderrCapture = new StreamCapture(stderrRead, MaxStdErrBytes, shared);
            collector = new CollectorState(shared, port, rootProcessId);
            stdoutCapture.Start("P5A verifier stdout reader");
            stderrCapture.Start("P5A verifier stderr reader");
            collector.Start();

            int readyBudget = RemainingMilliseconds(clock, runDeadline, Int32.MaxValue);
            if (readyBudget <= 0 || !stdoutCapture.Ready.Wait(readyBudget))
                throw new TimeoutException("P5A stdout reader was not ready before launch.");
            readyBudget = RemainingMilliseconds(clock, runDeadline, Int32.MaxValue);
            if (readyBudget <= 0 || !stderrCapture.Ready.Wait(readyBudget))
                throw new TimeoutException("P5A stderr reader was not ready before launch.");
            readyBudget = RemainingMilliseconds(clock, runDeadline, Int32.MaxValue);
            if (readyBudget <= 0 || !collector.Ready.Wait(readyBudget))
                throw new TimeoutException("P5A job metadata collector was not ready before launch.");
            if (ResumeThread(processInformation.hThread) == UInt32.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Close(ref processInformation.hThread);

            WaitHandle[] terminalSignals =
            {
                collector.ActiveZero.WaitHandle,
                shared.OutputLimitEvent.WaitHandle,
                shared.FailureEvent.WaitHandle
            };
            bool completedBeforeDeadline = false;
            while (Volatile.Read(ref shared.OutputLimit) == 0 &&
                   shared.Failure == null)
            {
                if (collector.ActiveZero.IsSet)
                {
                    completedBeforeDeadline = clock.ElapsedMilliseconds <= runDeadline;
                    break;
                }
                int remaining = RemainingMilliseconds(clock, runDeadline, 100);
                if (remaining <= 0) break;
                WaitHandle.WaitAny(terminalSignals, remaining);
            }
            if (!completedBeforeDeadline && Volatile.Read(ref shared.OutputLimit) == 0 &&
                shared.Failure == null)
                timedOut = true;
            if (timedOut || Volatile.Read(ref shared.OutputLimit) != 0 ||
                shared.Failure != null)
                TerminateJobObject(job, timedOut ? 0xE0000003u : 0xE0000004u);

            long cleanupDeadline = checked(clock.ElapsedMilliseconds + 3000L);
            Volatile.Write(ref shared.DeadlineMilliseconds, cleanupDeadline);
            shared.ReaderPulse.Set();
            int cleanupRemaining = RemainingMilliseconds(
                clock, cleanupDeadline, Int32.MaxValue);
            bool jobEmpty = shared.Failure == null
                ? cleanupRemaining > 0 && collector.ActiveZero.Wait(cleanupRemaining)
                : WaitForJobEmpty(job, clock, cleanupDeadline);
            if (!jobEmpty)
                throw new TimeoutException("P5A job did not become empty within cleanup budget.");
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !collector.Drained.Wait(cleanupRemaining))
                throw new TimeoutException("P5A job notifications did not drain within cleanup budget.");
            uint processWait = WaitForSingleObject(
                processInformation.hProcess,
                (uint)RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue));
            if (processWait != WAIT_OBJECT_0)
                throw new TimeoutException("P5A direct child did not exit within cleanup budget.");
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !stdoutCapture.Completed.Wait(cleanupRemaining))
                throw new TimeoutException("P5A stdout did not drain within cleanup budget.");
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !stderrCapture.Completed.Wait(cleanupRemaining))
                throw new TimeoutException("P5A stderr did not drain within cleanup budget.");
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !stdoutCapture.Thread.Join(cleanupRemaining))
                throw new TimeoutException("P5A stdout reader did not stop within cleanup budget.");
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !stderrCapture.Thread.Join(cleanupRemaining))
                throw new TimeoutException("P5A stderr reader did not stop within cleanup budget.");

            Volatile.Write(ref collector.Stop, 1);
            PostQueuedCompletionStatus(port, 0, new IntPtr(-1), IntPtr.Zero);
            cleanupRemaining = RemainingMilliseconds(clock, cleanupDeadline, Int32.MaxValue);
            if (cleanupRemaining <= 0 || !collector.Thread.Join(cleanupRemaining))
                throw new TimeoutException("P5A job metadata collector did not stop within cleanup budget.");

            if (shared.Failure != null)
                throw new InvalidOperationException(
                    "P5A native runner failed while collecting output or metadata.", shared.Failure);
            if (!collector.RootNotificationSeen)
                throw new InvalidOperationException("P5A root process metadata notification is missing.");

            var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
            if (!QueryInformationJobObject(
                job, 1, ref accounting, Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)),
                IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            ProcessMetadata[] metadata = collector.SnapshotMetadata();
            int[] jobProcessIds = collector.SnapshotJobProcessIds();
            if (accounting.ActiveProcesses != 0)
                throw new InvalidOperationException("P5A job still has active processes.");
            if (accounting.TotalProcesses != (uint)(1 + metadata.Length) ||
                accounting.TotalProcesses != (uint)jobProcessIds.Length)
                throw new InvalidOperationException(
                    "P5A job TotalProcesses != complete metadata process count.");

            uint nativeExitCode;
            if (!GetExitCodeProcess(processInformation.hProcess, out nativeExitCode) ||
                nativeExitCode == STILL_ACTIVE)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            bool outputLimitExceeded = Volatile.Read(ref shared.OutputLimit) != 0;
            return new RunnerResult
            {
                ProcessId = rootProcessId,
                ExitCode = timedOut ? -1 : (outputLimitExceeded ? -2 : unchecked((int)nativeExitCode)),
                TimedOut = timedOut,
                OutputLimitExceeded = outputLimitExceeded,
                StdOutLines = stdoutCapture.Lines(),
                StdErrLines = stderrCapture.Lines(),
                StdOutBytes = stdoutCapture.ByteCount,
                StdErrBytes = stderrCapture.ByteCount,
                DescendantProcesses = BuildDescendants(rootProcessId, metadata),
                JobTotalProcesses = checked((int)accounting.TotalProcesses),
                JobActiveProcesses = checked((int)accounting.ActiveProcesses),
                JobProcessIds = jobProcessIds
            };
        }
        finally
        {
            long finalDeadline = 0;
            if (shared != null)
            {
                finalDeadline = checked(shared.Clock.ElapsedMilliseconds + 3000L);
                Volatile.Write(ref shared.DeadlineMilliseconds, finalDeadline);
            }
            bool finalJobEmpty = false;
            if (processCreated && job != IntPtr.Zero)
            {
                try
                {
                    var finalAccounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
                    finalJobEmpty = QueryInformationJobObject(
                        job, 1, ref finalAccounting,
                        Marshal.SizeOf(typeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION)),
                        IntPtr.Zero) && finalAccounting.ActiveProcesses == 0;
                }
                catch { finalJobEmpty = false; }
            }
            if (processCreated && !finalJobEmpty)
                TerminateJobObject(job, 0xE0000005);
            if (shared != null)
            {
                Volatile.Write(ref shared.StopReaders, 1);
                shared.ReaderPulse.Set();
            }
            if (collector != null)
            {
                Volatile.Write(ref collector.Stop, 1);
                if (port != IntPtr.Zero)
                    PostQueuedCompletionStatus(port, 0, new IntPtr(-1), IntPtr.Zero);
                if (collector.Thread != null && collector.Thread.IsAlive)
                    collector.Thread.Join(RemainingMilliseconds(
                        shared.Clock, finalDeadline, Int32.MaxValue));
            }
            if (stdoutCapture != null && stdoutCapture.Thread != null &&
                stdoutCapture.Thread.IsAlive)
                stdoutCapture.Thread.Join(RemainingMilliseconds(
                    shared.Clock, finalDeadline, Int32.MaxValue));
            if (stderrCapture != null && stderrCapture.Thread != null &&
                stderrCapture.Thread.IsAlive)
                stderrCapture.Thread.Join(RemainingMilliseconds(
                    shared.Clock, finalDeadline, Int32.MaxValue));

            bool collectorStopped = collector == null || collector.Thread == null ||
                !collector.Thread.IsAlive;
            bool stdoutStopped = stdoutCapture == null || stdoutCapture.Thread == null ||
                !stdoutCapture.Thread.IsAlive;
            bool stderrStopped = stderrCapture == null || stderrCapture.Thread == null ||
                !stderrCapture.Thread.IsAlive;
            if (collectorStopped && collector != null) collector.DisposeMetadata();
            if (attributeList != IntPtr.Zero)
            {
                if (attributeListInitialized)
                    DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (jobListValue != IntPtr.Zero) Marshal.FreeHGlobal(jobListValue);
            if (handleListValue != IntPtr.Zero) Marshal.FreeHGlobal(handleListValue);
            Close(ref processInformation.hThread);
            Close(ref processInformation.hProcess);
            Close(ref stdinRead);
            Close(ref stdinWrite);
            if (stdoutStopped) Close(ref stdoutRead);
            else stdoutRead = IntPtr.Zero;
            Close(ref stdoutWrite);
            if (stderrStopped) Close(ref stderrRead);
            else stderrRead = IntPtr.Zero;
            Close(ref stderrWrite);
            if (collectorStopped) Close(ref port);
            else port = IntPtr.Zero;
            if (collectorStopped && stdoutStopped && stderrStopped) Close(ref job);
            else job = IntPtr.Zero;
            if (shared != null && collectorStopped && stdoutStopped && stderrStopped)
            {
                shared.ReaderPulse.Dispose();
                shared.FailureEvent.Dispose();
                shared.OutputLimitEvent.Dispose();
            }
            if (collectorStopped && collector != null) collector.DisposeSignals();
            if (stdoutStopped && stdoutCapture != null) stdoutCapture.DisposeResources();
            if (stderrStopped && stderrCapture != null) stderrCapture.DisposeResources();
        }
    }
}
'@
        $nativeType = 'P5aVerifierPreboundRunnerNative' -as [type]
    }

    $nativeResult = $nativeType::Run(
        $FilePath, [string[]]$Arguments, $WorkingDirectory, $TimeoutSeconds, $PhaseName)
    $exitCode = [int]$nativeResult.ExitCode
    if ([bool]$nativeResult.TimedOut) { $exitCode = -1 }
    elseif ([bool]$nativeResult.OutputLimitExceeded) { $exitCode = -2 }
    return [pscustomobject][ordered]@{
        ProcessId = [int]$nativeResult.ProcessId
        ExitCode = $exitCode
        TimedOut = [bool]$nativeResult.TimedOut
        OutputLimitExceeded = [bool]$nativeResult.OutputLimitExceeded
        StdOutLines = @($nativeResult.StdOutLines)
        StdErrLines = @($nativeResult.StdErrLines)
        StdOutBytes = [int64]$nativeResult.StdOutBytes
        StdErrBytes = [int64]$nativeResult.StdErrBytes
        DescendantProcesses = @($nativeResult.DescendantProcesses)
        JobTotalProcesses = [int]$nativeResult.JobTotalProcesses
        JobActiveProcesses = [int]$nativeResult.JobActiveProcesses
        JobProcessIds = @($nativeResult.JobProcessIds)
    }
}


function Invoke-P5aVerifierChild
{
    param(
        [Parameter(Mandatory)][scriptblock]$ProcessInvoker,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$PhaseName
    )

    return & $ProcessInvoker $FilePath $Arguments $WorkingDirectory $TimeoutSeconds $PhaseName
}

function Get-P5aVerifierFullPath
{
    param([Parameter(Mandatory)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\.\', [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'P5A verifier device paths are forbidden.'
    }
    if ($full.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase))
    {
        $full = '\\' + $full.Substring(8)
    }
    elseif ($full.StartsWith('\\?\', [StringComparison]::OrdinalIgnoreCase))
    {
        $extendedPath = $full.Substring(4)
        if ($extendedPath -cnotmatch '^[A-Za-z]:[\\/]')
        {
            throw 'P5A verifier extended path namespace is forbidden.'
        }
        $full = $extendedPath
    }
    $full = [IO.Path]::GetFullPath($full)
    $volumeRoot = [IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($volumeRoot))
    {
        throw 'P5A verifier path has no volume root.'
    }
    if ($full -ieq $volumeRoot) { return $full }
    return $full.TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Open-P5aVerifierFixedFileLease
{
    param([Parameter(Mandatory)][string]$Path)

    $nativeType = 'P5aVerifierFixedFileLeaseNative' -as [type]
    if ($null -eq $nativeType)
    {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class P5aVerifierFixedFileLeaseNative
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const long MAX_EVIDENCE_READ_BYTES = 64L * 1024L * 1024L;

    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path, uint access, uint share, IntPtr securityAttributes,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle, out BY_HANDLE_FILE_INFORMATION information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle, StringBuilder path, uint pathLength, uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, SafeFileHandle sourceHandle, IntPtr targetProcess,
        out SafeFileHandle targetHandle, uint desiredAccess, bool inheritHandle,
        uint options);

    public sealed class Lease : IDisposable
    {
        private readonly object sync = new object();
        private readonly uint volumeSerialNumber;
        private readonly ulong fileId;

        public string Name { get; private set; }
        public SafeFileHandle SafeFileHandle { get; private set; }
        public DateTime LastWriteTimeUtc { get; private set; }

        internal Lease(
            string name, SafeFileHandle handle, BY_HANDLE_FILE_INFORMATION information)
        {
            Name = name;
            SafeFileHandle = handle;
            volumeSerialNumber = information.VolumeSerialNumber;
            fileId = ToFileId(information);
            long timestamp = ((long)(uint)information.LastWriteTime.dwHighDateTime << 32) |
                (uint)information.LastWriteTime.dwLowDateTime;
            LastWriteTimeUtc = DateTime.FromFileTimeUtc(timestamp);
        }

        public void AssertPathIdentity()
        {
            lock (sync)
            {
                EnsureOpen();
                using (SafeFileHandle candidate = OpenHandle(Name))
                {
                    BY_HANDLE_FILE_INFORMATION information = ValidateFile(Name, candidate);
                    if (information.VolumeSerialNumber != volumeSerialNumber ||
                        ToFileId(information) != fileId)
                    {
                        throw new IOException(
                            "P5A verifier fixed-file lease path identity changed: " + Name);
                    }
                }
            }
        }

        public bool MatchesPath()
        {
            try
            {
                AssertPathIdentity();
                return true;
            }
            catch { return false; }
        }

        public string ComputeSha256()
        {
            lock (sync)
            {
                EnsureOpen();
                using (SafeFileHandle duplicate = DuplicateForRead())
                using (var stream = new FileStream(
                    duplicate, FileAccess.Read, 65536, false))
                using (SHA256 algorithm = SHA256.Create())
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    byte[] digest = algorithm.ComputeHash(stream);
                    var text = new StringBuilder(digest.Length * 2);
                    foreach (byte value in digest) text.Append(value.ToString("x2"));
                    return text.ToString();
                }
            }
        }

        public byte[] ReadAllBytes()
        {
            lock (sync)
            {
                EnsureOpen();
                using (SafeFileHandle duplicate = DuplicateForRead())
                using (var stream = new FileStream(
                    duplicate, FileAccess.Read, 65536, false))
                {
                    if (stream.Length > MAX_EVIDENCE_READ_BYTES)
                        throw new IOException(
                            "P5A verifier evidence file exceeds the fixed read limit: " + Name);
                    stream.Seek(0, SeekOrigin.Begin);
                    using (var output = new MemoryStream((int)stream.Length))
                    {
                        stream.CopyTo(output);
                        return output.ToArray();
                    }
                }
            }
        }

        public string ReadAllText()
        {
            lock (sync)
            {
                EnsureOpen();
                using (SafeFileHandle duplicate = DuplicateForRead())
                using (var stream = new FileStream(
                    duplicate, FileAccess.Read, 65536, false))
                {
                    if (stream.Length > MAX_EVIDENCE_READ_BYTES)
                        throw new IOException(
                            "P5A verifier evidence file exceeds the fixed read limit: " + Name);
                    stream.Seek(0, SeekOrigin.Begin);
                    using (var reader = new StreamReader(
                        stream, Encoding.UTF8, true, 65536, false))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                SafeFileHandle.Dispose();
            }
        }

        private SafeFileHandle DuplicateForRead()
        {
            SafeFileHandle duplicate;
            IntPtr process = GetCurrentProcess();
            if (!DuplicateHandle(
                    process, SafeFileHandle, process, out duplicate,
                    0, false, DUPLICATE_SAME_ACCESS))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Cannot duplicate a P5A verifier fixed-file lease handle");
            }
            return duplicate;
        }

        private void EnsureOpen()
        {
            if (SafeFileHandle == null || SafeFileHandle.IsInvalid || SafeFileHandle.IsClosed)
                throw new ObjectDisposedException("P5A verifier fixed-file lease");
        }
    }

    public static Lease Open(string path)
    {
        string normalized = NormalizePath(path);
        SafeFileHandle handle = OpenHandle(normalized);
        try
        {
            BY_HANDLE_FILE_INFORMATION information = ValidateFile(normalized, handle);
            var lease = new Lease(normalized, handle, information);
            handle = null;
            return lease;
        }
        finally
        {
            if (handle != null) handle.Dispose();
        }
    }

    private static SafeFileHandle OpenHandle(string path)
    {
        SafeFileHandle handle = CreateFileW(
            path, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle == null || handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (handle != null) handle.Dispose();
            throw new Win32Exception(
                error, "Cannot open a fixed P5A verifier evidence file: " + path);
        }
        return handle;
    }

    private static BY_HANDLE_FILE_INFORMATION ValidateFile(
        string expectedPath, SafeFileHandle handle)
    {
        BY_HANDLE_FILE_INFORMATION information;
        if (!GetFileInformationByHandle(handle, out information))
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Cannot identify a fixed P5A verifier evidence file: " + expectedPath);
        if ((information.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
            throw new IOException(
                "P5A verifier fixed-file lease rejected a directory: " + expectedPath);
        if ((information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            throw new IOException(
                "P5A verifier fixed-file lease rejected a reparse point: " + expectedPath);

        string finalPath = ReadFinalPath(handle);
        if (!String.Equals(
                NormalizePath(finalPath), NormalizePath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "P5A verifier fixed-file lease resolved outside its requested path: " +
                expectedPath);
        }
        return information;
    }

    private static ulong ToFileId(BY_HANDLE_FILE_INFORMATION information)
    {
        return ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
    }

    private static string ReadFinalPath(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
        if (length == 0)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Cannot resolve a fixed P5A verifier evidence path");
        if (length >= path.Capacity)
            throw new IOException("A fixed P5A verifier evidence path is too long.");
        string value = path.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + value.Substring(8);
        if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return value.Substring(4);
        return value;
    }

    private static string NormalizePath(string path)
    {
        string value = Path.GetFullPath(path);
        string root = Path.GetPathRoot(value);
        if (!String.Equals(value, root, StringComparison.OrdinalIgnoreCase))
            value = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return value;
    }
}
'@
        $nativeType = 'P5aVerifierFixedFileLeaseNative' -as [type]
    }

    $fullPath = Get-P5aVerifierFullPath $Path
    Assert-P5aVerifierNoReparsePoint $fullPath
    return $nativeType::Open($fullPath)
}

function Get-P5aVerifierFixedFileLeaseForPath
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$FixedLeasesByPath
    )

    $fullPath = Get-P5aVerifierFullPath $Path
    if (-not $FixedLeasesByPath.ContainsKey($fullPath))
    {
        throw "P5A verifier evidence path was not fixed by the active lease: $fullPath"
    }
    $lease = $FixedLeasesByPath[$fullPath]
    $lease.AssertPathIdentity()
    return $lease
}

function Get-P5aVerifierFileHash
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [object]$FixedLeasesByPath
    )

    if ($null -ne $FixedLeasesByPath)
    {
        $lease = Get-P5aVerifierFixedFileLeaseForPath `
            -Path $Path -FixedLeasesByPath $FixedLeasesByPath
        return ([string]$lease.ComputeSha256()).ToLowerInvariant()
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        throw "Required P5A verifier evidence file is missing: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-P5aVerifierFileBytes
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [object]$FixedLeasesByPath
    )

    if ($null -eq $FixedLeasesByPath) { return ,([IO.File]::ReadAllBytes($Path)) }
    $lease = Get-P5aVerifierFixedFileLeaseForPath `
        -Path $Path -FixedLeasesByPath $FixedLeasesByPath
    return ,([byte[]]$lease.ReadAllBytes())
}

function Read-P5aVerifierFileText
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [object]$FixedLeasesByPath
    )

    if ($null -eq $FixedLeasesByPath) { return [IO.File]::ReadAllText($Path) }
    $lease = Get-P5aVerifierFixedFileLeaseForPath `
        -Path $Path -FixedLeasesByPath $FixedLeasesByPath
    return [string]$lease.ReadAllText()
}

function Get-P5aVerifierFileLastWriteTimeUtc
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [object]$FixedLeasesByPath
    )

    if ($null -eq $FixedLeasesByPath) { return [IO.File]::GetLastWriteTimeUtc($Path) }
    $lease = Get-P5aVerifierFixedFileLeaseForPath `
        -Path $Path -FixedLeasesByPath $FixedLeasesByPath
    return [DateTime]$lease.LastWriteTimeUtc
}

function Assert-P5aVerifierNoReparsePoint
{
    param([Parameter(Mandatory)][string]$Path)

    $current = Get-P5aVerifierFullPath $Path
    while (-not [string]::IsNullOrEmpty($current))
    {
        if (Test-Path -LiteralPath $current)
        {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "P5A verifier evidence contains a reparse point: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($parent) -or $parent -ceq $current) { break }
        $current = $parent
    }
}

function Test-P5aVerifierPathWithin
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    if ($Path -ieq $Root) { return $true }
    $prefix = $Root
    if (-not $prefix.EndsWith(
            [string][IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal) -and
        -not $prefix.EndsWith(
            [string][IO.Path]::AltDirectorySeparatorChar,
            [StringComparison]::Ordinal))
    {
        $prefix += [IO.Path]::DirectorySeparatorChar
    }
    return $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-P5aVerifierInvocationContract
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$FixturePath,
        [Parameter(Mandatory)][string]$StagingRoot,
        [string]$OracleAppHost,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    if ($TimeoutSeconds -le 0) { throw 'P5A timeout must be positive.' }
    foreach ($entry in @(
        @{ Value = $RepositoryRoot; Name = 'Repository root' },
        @{ Value = $FixturePath; Name = 'Fixture path' },
        @{ Value = $StagingRoot; Name = 'Staging root' }))
    {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value) -or
            -not [IO.Path]::IsPathFullyQualified([string]$entry.Value))
        {
            throw "$($entry.Name) must be absolute."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($OracleAppHost) -and
        -not [IO.Path]::IsPathFullyQualified($OracleAppHost))
    {
        throw 'P5A Oracle apphost path must be absolute.'
    }

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $fixture = Get-P5aVerifierFullPath $FixturePath
    $staging = Get-P5aVerifierFullPath $StagingRoot
    if (-not (Test-Path -LiteralPath $root -PathType Container))
    {
        throw 'Repository root external prerequisite must be an existing directory.'
    }
    if (-not (Test-Path -LiteralPath $fixture -PathType Leaf))
    {
        throw 'P5A fixture external prerequisite must be an existing file.'
    }
    if (-not [string]::IsNullOrWhiteSpace($OracleAppHost) -and
        -not (Test-Path -LiteralPath $OracleAppHost -PathType Leaf))
    {
        throw 'P5A Oracle apphost external prerequisite must be an existing file.'
    }
    $volumeRoot = Get-P5aVerifierFullPath ([IO.Path]::GetPathRoot($staging))
    if ($staging -ieq $volumeRoot) { throw 'P5A verifier staging cannot be a volume root.' }
    Assert-P5aVerifierNoReparsePoint $staging
    if ((Test-P5aVerifierPathWithin -Path $staging -Root $root) -or
        (Test-P5aVerifierPathWithin -Path $root -Root $staging))
    {
        throw 'P5A verifier staging and repository must not contain one another.'
    }
    if (Test-Path -LiteralPath $staging)
    {
        if (-not (Test-Path -LiteralPath $staging -PathType Container))
        {
            throw 'P5A verifier staging path already exists and is not a directory.'
        }
        throw 'P5A verifier staging directory must not preexist, whether empty or non-empty.'
    }
}

function Get-P5aVerifierOracleSourceClosure
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [object]$FixedLeasesByPath
    )

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $relativePaths = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in @(
        '.editorconfig', 'global.json', 'Directory.Build.props',
        'tools/Als.P5aOracle/Als.P5aOracle.csproj', 'tools/Als.P5aOracle/Program.cs',
        'src/Als.Import/Als.Import.csproj', 'src/Als.Core/Als.Core.csproj'))
    {
        $path = Join-Path $root $relativePath
        Assert-P5aVerifierNoReparsePoint $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Oracle source closure is missing '$relativePath'."
        }
        $relativePaths.Add($relativePath)
    }
    foreach ($family in @('src/Als.Import', 'src/Als.Core'))
    {
        $familyRoot = Join-Path $root $family
        Assert-P5aVerifierNoReparsePoint $familyRoot
        $files = @(Get-ChildItem -LiteralPath $familyRoot -Filter '*.cs' -File -Recurse |
            Where-Object {
                $relative = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
                -not @($relative.Split('/') | Where-Object {
                    $_.Equals('bin', [StringComparison]::OrdinalIgnoreCase) -or
                    $_.Equals('obj', [StringComparison]::OrdinalIgnoreCase)
                }).Count
            })
        if ($files.Count -eq 0) { throw "Oracle source closure family '$family' is empty." }
        foreach ($file in $files)
        {
            Assert-P5aVerifierNoReparsePoint $file.FullName
            $relativePaths.Add(
                [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/'))
        }
    }
    $orderedPaths = [string[]]@($relativePaths)
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    if (@($orderedPaths | Select-Object -Unique).Count -ne $orderedPaths.Count)
    {
        throw 'Oracle source closure contains a duplicate entry.'
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try
    {
        foreach ($relativePath in $orderedPaths)
        {
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($relativePath))
            $hash.AppendData([byte[]]@(0))
            $hash.AppendData([Text.Encoding]::ASCII.GetBytes(
                (Get-P5aVerifierFileHash `
                    -Path (Join-Path $root $relativePath) `
                    -FixedLeasesByPath $FixedLeasesByPath)))
            $hash.AppendData([byte[]]@(10))
        }
        $treeHash = [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    }
    finally { $hash.Dispose() }
    return [pscustomobject]@{
        Paths = @($orderedPaths | ForEach-Object { Join-Path $root $_ })
        TreeSha256 = $treeHash
    }
}

function Assert-P5aVerifierOracleProjectContract
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [object]$FixedLeasesByPath
    )

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $projectPath = Join-Path $root 'tools\Als.P5aOracle\Als.P5aOracle.csproj'
    try
    {
        [xml]$project = Read-P5aVerifierFileText `
            -Path $projectPath -FixedLeasesByPath $FixedLeasesByPath
    }
    catch { throw "Oracle project XML is invalid: $($_.Exception.Message)" }
    $references = @($project.Project.ItemGroup.ProjectReference)
    $referencePaths = @($references | ForEach-Object { [string]$_.Include })
    if ($referencePaths.Count -ne 2 -or
        @($referencePaths | Where-Object { $_ -ceq '..\..\src\Als.Import\Als.Import.csproj' }).Count -ne 1 -or
        @($referencePaths | Where-Object { $_ -ceq '..\..\src\Als.Core\Als.Core.csproj' }).Count -ne 1)
    {
        throw 'Oracle project must reference exactly Als.Import and Als.Core.'
    }
    foreach ($forbiddenName in @(
        'PackageReference', 'Reference', 'COMReference', 'NativeReference',
        'CopyToOutputDirectory'))
    {
        if (@($project.SelectNodes("//*[local-name()='$forbiddenName']")).Count -ne 0)
        {
            throw "Oracle project contains forbidden $forbiddenName."
        }
    }
    $targets = @($project.Project.Target)
    $privateMode = '--write-' + 'build-manifest'
    $expectedCommand = '"$(TargetDir)Als.P5aOracle.exe" ' + $privateMode +
        ' --repository-root "$(P5aRepositoryRoot)" --output "$(TargetDir)p5a-oracle-build.manifest" --sdk-version "$(NETCoreSdkVersion)"'
    if ($targets.Count -ne 1 -or
        [string]$targets[0].Name -cne 'WriteP5aOracleBuildManifest' -or
        [string]$targets[0].AfterTargets -cne 'Build' -or
        [string]$targets[0].Condition -cne "'`$(Configuration)' == 'Release'" -or
        @($targets[0].Exec).Count -ne 1 -or
        [string]$targets[0].Exec.Command -cne $expectedCommand)
    {
        throw 'Oracle project requires one exact Release AfterBuild manifest target.'
    }

    $depsPath = Join-Path $root 'tools\Als.P5aOracle\bin\Release\net8.0\Als.P5aOracle.deps.json'
    try
    {
        $deps = Read-P5aVerifierFileText `
            -Path $depsPath -FixedLeasesByPath $FixedLeasesByPath |
            ConvertFrom-Json -Depth 100
    }
    catch { throw 'Oracle deps document is missing or malformed.' }
    $libraries = @($deps.libraries.PSObject.Properties)
    if ($libraries.Count -ne 3)
    {
        throw 'Oracle deps contains a forbidden local library.'
    }
    foreach ($name in @('Als.P5aOracle', 'Als.Import', 'Als.Core'))
    {
        $matches = @($libraries | Where-Object {
            $_.Name -match ('^' + [regex]::Escape($name) + '/[^/]+$')
        })
        if ($matches.Count -ne 1 -or [string]$matches[0].Value.type -cne 'project')
        {
            throw "Oracle deps must contain project library '$name'."
        }
    }
    $frameworks = @($deps.targets.PSObject.Properties)
    if ($frameworks.Count -ne 1 -or [string]$frameworks[0].Name -cne '.NETCoreApp,Version=v8.0')
    {
        throw 'Oracle deps target framework must be net8.0.'
    }
    $projects = @($frameworks[0].Value.PSObject.Properties)
    $runtimeDlls = @($projects | ForEach-Object {
        @($_.Value.runtime.PSObject.Properties | ForEach-Object { $_.Name })
    })
    if ($projects.Count -ne 3 -or
        @(Compare-Object @('Als.Core.dll', 'Als.Import.dll', 'Als.P5aOracle.dll') `
            @($runtimeDlls | Sort-Object) -CaseSensitive).Count -ne 0)
    {
        throw 'Oracle deps runtime target closure is not exact.'
    }
}

function Read-P5aVerifierOracleEvidence
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [object]$FixedLeasesByPath
    )

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    Assert-P5aVerifierOracleProjectContract `
        -RepositoryRoot $root -FixedLeasesByPath $FixedLeasesByPath
    $closure = Get-P5aVerifierOracleSourceClosure `
        -RepositoryRoot $root -FixedLeasesByPath $FixedLeasesByPath
    $outputRoot = Join-Path $root 'tools\Als.P5aOracle\bin\Release\net8.0'
    $manifestPath = Join-Path $outputRoot 'p5a-oracle-build.manifest'
    $artifactNames = @(
        'Als.P5aOracle.exe', 'Als.P5aOracle.dll', 'Als.Import.dll', 'Als.Core.dll',
        'Als.P5aOracle.deps.json', 'Als.P5aOracle.runtimeconfig.json')
    $artifactPaths = @($artifactNames | ForEach-Object { Join-Path $outputRoot $_ })
    foreach ($path in @($manifestPath) + $artifactPaths)
    {
        Assert-P5aVerifierNoReparsePoint $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Fixed Release Oracle apphost evidence is missing: $path"
        }
    }
    $bytes = Read-P5aVerifierFileBytes `
        -Path $manifestPath -FixedLeasesByPath $FixedLeasesByPath
    if ($bytes.Length -eq 0 -or
        ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and
         $bytes[2] -eq 0xbf))
    {
        throw 'Oracle build manifest must be UTF-8 without BOM.'
    }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    if ($text.Contains("`r", [StringComparison]::Ordinal) -or
        -not $text.EndsWith("`n", [StringComparison]::Ordinal) -or
        $text.EndsWith("`n`n", [StringComparison]::Ordinal))
    {
        throw 'Oracle build manifest newline contract is invalid.'
    }
    $lines = $text.Substring(0, $text.Length - 1).Split("`n")
    $names = @(
        'configuration', 'targetFramework', 'sdkVersion', 'sourceTreeSha256',
        'executableSha256', 'oracleAssemblySha256', 'importAssemblySha256',
        'coreAssemblySha256', 'depsSha256', 'runtimeConfigSha256')
    if ($lines.Count -ne 11 -or $lines[0] -cne 'p5a_oracle_build_v1')
    {
        throw 'Oracle build manifest line closure is invalid.'
    }
    $values = [ordered]@{}
    for ($index = 0; $index -lt $names.Count; $index++)
    {
        $prefix = $names[$index] + '='
        if (-not $lines[$index + 1].StartsWith($prefix, [StringComparison]::Ordinal))
        {
            throw "Oracle build manifest field '$($names[$index])' is missing or reordered."
        }
        $values[$names[$index]] = $lines[$index + 1].Substring($prefix.Length)
    }
    if ($values.configuration -cne 'Release' -or $values.targetFramework -cne 'net8.0' -or
        [string]::IsNullOrEmpty([string]$values.sdkVersion))
    {
        throw 'Oracle build manifest configuration, framework, or SDK is invalid.'
    }
    $actualHashes = [ordered]@{
        sourceTreeSha256 = $closure.TreeSha256
        executableSha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[0] -FixedLeasesByPath $FixedLeasesByPath
        oracleAssemblySha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[1] -FixedLeasesByPath $FixedLeasesByPath
        importAssemblySha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[2] -FixedLeasesByPath $FixedLeasesByPath
        coreAssemblySha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[3] -FixedLeasesByPath $FixedLeasesByPath
        depsSha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[4] -FixedLeasesByPath $FixedLeasesByPath
        runtimeConfigSha256 = Get-P5aVerifierFileHash `
            -Path $artifactPaths[5] -FixedLeasesByPath $FixedLeasesByPath
    }
    foreach ($entry in $actualHashes.GetEnumerator())
    {
        if ([string]$values[$entry.Key] -cne [string]$entry.Value)
        {
            throw "Oracle evidence hash mismatch for '$($entry.Key)'."
        }
    }
    $newestSource = @($closure.Paths | ForEach-Object {
        Get-P5aVerifierFileLastWriteTimeUtc `
            -Path $_ -FixedLeasesByPath $FixedLeasesByPath
    } | Sort-Object -Descending)[0]
    foreach ($path in $artifactPaths[0..3])
    {
        if ((Get-P5aVerifierFileLastWriteTimeUtc `
                -Path $path -FixedLeasesByPath $FixedLeasesByPath) -lt $newestSource)
        {
            throw "Oracle runtime artifact is stale relative to source: $path"
        }
    }
    return [pscustomobject]@{
        Evidence = [pscustomobject][ordered]@{
            sdkVersion = [string]$values.sdkVersion
            sourceTreeSha256 = [string]$actualHashes.sourceTreeSha256
            buildManifestSha256 = Get-P5aVerifierFileHash `
                -Path $manifestPath -FixedLeasesByPath $FixedLeasesByPath
            executableSha256 = [string]$actualHashes.executableSha256
            oracleAssemblySha256 = [string]$actualHashes.oracleAssemblySha256
            importAssemblySha256 = [string]$actualHashes.importAssemblySha256
            coreAssemblySha256 = [string]$actualHashes.coreAssemblySha256
            depsSha256 = [string]$actualHashes.depsSha256
            runtimeConfigSha256 = [string]$actualHashes.runtimeConfigSha256
        }
        AppHostPath = $artifactPaths[0]
        SourcePaths = @($closure.Paths)
        ManifestPath = $manifestPath
        ArtifactPaths = @($artifactPaths)
    }
}

function Get-P5aVerifierRuntimeInputPaths
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$FixturePath
    )

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $testRoot = Get-P5aVerifierFullPath (Join-Path $root 'tests\Als.Core.Tests')
    $testProject = Get-P5aVerifierFullPath (Join-Path `
        $testRoot 'Als.Core.Tests.csproj')
    $focusedTest = Get-P5aVerifierFullPath (Join-Path `
        $testRoot 'AlsP5aGoldenTests.cs')
    Assert-P5aVerifierNoReparsePoint $testRoot
    Assert-P5aVerifierNoReparsePoint $testProject
    if (-not (Test-Path -LiteralPath $testProject -PathType Leaf))
    {
        throw "P5A focused test project is missing: $testProject"
    }
    $testSources = @(Get-ChildItem -LiteralPath $testRoot -Filter '*.cs' -File -Recurse |
        Where-Object {
            $relative = [IO.Path]::GetRelativePath($testRoot, $_.FullName)
            -not @($relative.Split([IO.Path]::DirectorySeparatorChar) | Where-Object {
                $_.Equals('bin', [StringComparison]::OrdinalIgnoreCase) -or
                $_.Equals('obj', [StringComparison]::OrdinalIgnoreCase) -or
                $_.Equals('TestResults', [StringComparison]::OrdinalIgnoreCase)
            }).Count
        } | ForEach-Object {
            $path = Get-P5aVerifierFullPath $_.FullName
            Assert-P5aVerifierNoReparsePoint $path
            $path
        })
    [Array]::Sort($testSources, [StringComparer]::Ordinal)
    if ($testSources.Count -eq 0 -or $focusedTest -cnotin $testSources)
    {
        throw "P5A focused test source is missing: $focusedTest"
    }

    $paths = [string[]]@(
        (Get-P5aVerifierFullPath $FixturePath)
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'tools\schemas\als_p5a_trace.schema.json'))
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'tools\schemas\als_p5a_trace_plan.schema.json'))
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'assets\generated\als_v4\als_manifest.json'))
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'assets\config\p3_locomotion_profile.json'))
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'assets\config\p4_pose_profile.json'))
        (Get-P5aVerifierFullPath (Join-Path `
            $root 'assets\config\p5a_animation_runtime.json'))
        $testProject
        @($testSources)
    )
    $unique = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $paths)
    {
        if (-not $unique.Add($path))
        {
            throw "P5A verifier runtime input closure contains a duplicate path: $path"
        }
    }
    return $paths
}

function Read-P5aVerifierRuntimeInputEvidence
{
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [object]$FixedLeasesByPath
    )

    return @($Paths | ForEach-Object {
        $path = Get-P5aVerifierFullPath $_
        Assert-P5aVerifierNoReparsePoint $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "P5A verifier runtime input is missing: $path"
        }
        [pscustomobject][ordered]@{
            path = $path
            sha256 = Get-P5aVerifierFileHash `
                -Path $path -FixedLeasesByPath $FixedLeasesByPath
        }
    })
}

function Open-P5aVerifierOracleEvidenceLease
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$FixturePath
    )

    $snapshot = Read-P5aVerifierOracleEvidence $RepositoryRoot
    $inputPaths = Get-P5aVerifierRuntimeInputPaths `
        -RepositoryRoot $RepositoryRoot -FixturePath $FixturePath
    $inputEvidence = @(Read-P5aVerifierRuntimeInputEvidence $inputPaths)
    $handles = [Collections.Generic.List[object]]::new()
    $fixedLeasesByPath = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    try
    {
        foreach ($path in @($snapshot.SourcePaths) + @($snapshot.ManifestPath) +
                         @($snapshot.ArtifactPaths) + @($inputPaths))
        {
            $fullPath = Get-P5aVerifierFullPath $path
            if ($fixedLeasesByPath.ContainsKey($fullPath))
            {
                throw "P5A verifier fixed-file lease contains a duplicate path: $fullPath"
            }
            $handle = Open-P5aVerifierFixedFileLease $fullPath
            $fixedLeasesByPath.Add($fullPath, $handle)
            $handles.Add($handle)
        }

        foreach ($handle in $handles) { $handle.AssertPathIdentity() }
        $confirmedInputPaths = Get-P5aVerifierRuntimeInputPaths `
            -RepositoryRoot $RepositoryRoot -FixturePath $FixturePath
        if (($inputPaths | ConvertTo-Json -Compress) -cne
            ($confirmedInputPaths | ConvertTo-Json -Compress))
        {
            throw 'P5A runtime input closure changed while fixed-file leases were being acquired.'
        }
        $fixedSnapshot = Read-P5aVerifierOracleEvidence `
            -RepositoryRoot $RepositoryRoot -FixedLeasesByPath $fixedLeasesByPath
        $fixedInputEvidence = @(Read-P5aVerifierRuntimeInputEvidence `
            -Paths $confirmedInputPaths -FixedLeasesByPath $fixedLeasesByPath)

        $beforeOracle = [pscustomobject][ordered]@{
            evidence = $snapshot.Evidence
            appHostPath = [string]$snapshot.AppHostPath
            sourcePaths = [string[]]@($snapshot.SourcePaths)
            manifestPath = [string]$snapshot.ManifestPath
            artifactPaths = [string[]]@($snapshot.ArtifactPaths)
        }
        $afterOracle = [pscustomobject][ordered]@{
            evidence = $fixedSnapshot.Evidence
            appHostPath = [string]$fixedSnapshot.AppHostPath
            sourcePaths = [string[]]@($fixedSnapshot.SourcePaths)
            manifestPath = [string]$fixedSnapshot.ManifestPath
            artifactPaths = [string[]]@($fixedSnapshot.ArtifactPaths)
        }
        if (($beforeOracle | ConvertTo-Json -Compress -Depth 8) -cne
            ($afterOracle | ConvertTo-Json -Compress -Depth 8))
        {
            throw 'Oracle evidence changed while fixed-file leases were being acquired.'
        }
        if (($inputEvidence | ConvertTo-Json -Compress -Depth 8) -cne
            ($fixedInputEvidence | ConvertTo-Json -Compress -Depth 8))
        {
            throw 'P5A runtime input changed while fixed-file leases were being acquired.'
        }

        $lease = [pscustomobject]@{
            Evidence = $fixedSnapshot.Evidence
            InputEvidence = @($fixedInputEvidence)
            AppHostPath = $fixedSnapshot.AppHostPath
            Handles = @($handles)
            FixedLeasesByPath = $fixedLeasesByPath
        }
        Add-Member -InputObject $lease -MemberType ScriptMethod -Name Dispose -Value {
            foreach ($handle in @($this.Handles)) { $handle.Dispose() }
        }
        return $lease
    }
    catch
    {
        foreach ($handle in $handles) { $handle.Dispose() }
        throw
    }
}

function Assert-P5aVerifierRuntimeInputEvidenceUnchanged
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$FixturePath,
        [Parameter(Mandatory)][object[]]$ExpectedEvidence,
        [object]$FixedLeasesByPath
    )

    if ($null -ne $FixedLeasesByPath)
    {
        foreach ($fixedLease in $FixedLeasesByPath.Values)
        {
            $fixedLease.AssertPathIdentity()
        }
    }
    $paths = [string[]]@($ExpectedEvidence | ForEach-Object { [string]$_.path })
    $currentPaths = Get-P5aVerifierRuntimeInputPaths `
        -RepositoryRoot $RepositoryRoot -FixturePath $FixturePath
    if (($currentPaths | ConvertTo-Json -Compress) -cne
        ($paths | ConvertTo-Json -Compress))
    {
        throw 'P5A verifier runtime input closure changed while verification was running.'
    }
    $actual = @(Read-P5aVerifierRuntimeInputEvidence `
        -Paths $paths -FixedLeasesByPath $FixedLeasesByPath)
    if (($actual | ConvertTo-Json -Compress) -cne
        ($ExpectedEvidence | ConvertTo-Json -Compress))
    {
        throw 'P5A verifier runtime input changed while verification was running.'
    }
}

function Assert-P5aVerifierOracleEvidenceUnchanged
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][object]$ExpectedEvidence,
        [object]$FixedLeasesByPath
    )

    $actual = Read-P5aVerifierOracleEvidence `
        -RepositoryRoot $RepositoryRoot -FixedLeasesByPath $FixedLeasesByPath
    if (($actual.Evidence | ConvertTo-Json -Compress) -cne
        ($ExpectedEvidence | ConvertTo-Json -Compress))
    {
        throw 'Oracle evidence changed while the P5A verifier was running.'
    }
}

function Invoke-P5aVerifierCheckpoint
{
    param(
        [scriptblock]$CheckpointInvoker,
        [Parameter(Mandatory)][string]$Checkpoint,
        [object]$OracleLease
    )

    if ($null -ne $CheckpointInvoker)
    {
        & $CheckpointInvoker $Checkpoint $OracleLease $null | Out-Null
    }
}

function Enter-P5aVerifierEnvironmentGate
{
    param([Parameter(Mandatory)][int]$TimeoutSeconds)

    $name = 'Local\GodotALS.P5A.Verifier.Environment.' + [Environment]::ProcessId
    $mutex = [Threading.Mutex]::new($false, $name)
    try
    {
        $acquired = $false
        try { $acquired = $mutex.WaitOne($TimeoutSeconds * 1000) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired)
        {
            throw 'P5A verifier timed out waiting for its process environment gate.'
        }
        return $mutex
    }
    catch
    {
        $mutex.Dispose()
        throw
    }
}

function Exit-P5aVerifierEnvironmentGate
{
    param([Parameter(Mandatory)][Threading.Mutex]$Mutex)

    try { $Mutex.ReleaseMutex() }
    finally { $Mutex.Dispose() }
}

function Assert-P5aVerifierOracleChild
{
    param([Parameter(Mandatory)][object]$Result)

    if ($Result.TimedOut) { throw 'P5A Oracle verification timed out.' }
    if ($null -ne $Result.PSObject.Properties['OutputLimitExceeded'] -and
        [bool]$Result.OutputLimitExceeded)
    {
        throw 'P5A Oracle verification exceeded its output limit.'
    }
    if ([int]$Result.ExitCode -ne 0) { throw 'P5A Oracle verification exited non-zero.' }
    foreach ($streamProperty in @('StdOutLines', 'StdErrLines'))
    {
        if ($null -eq $Result.PSObject.Properties[$streamProperty] -or
            $null -eq $Result.$streamProperty)
        {
            throw "P5A Oracle verification $streamProperty evidence is missing."
        }
    }
    $markerPattern = '^P5A_ORACLE_DIGESTS layout=f2336240d749284b bindings=40f33e59692dfd38 graph=44403c2869d8f615 plan=[0-9a-f]{64}$'
    $stdoutLines = @($Result.StdOutLines)
    $stderrLines = @($Result.StdErrLines)
    $markers = @($stdoutLines | Where-Object { [string]$_ -cmatch $markerPattern })
    foreach ($lineObject in $stdoutLines)
    {
        $line = [string]$lineObject
        if ($line.Contains('P5A_', [StringComparison]::Ordinal) -and
            $line -cnotmatch $markerPattern)
        {
            throw 'P5A Oracle verification emitted an invalid marker.'
        }
        if ($line -match '(?i)(^|:\s*)(warning|error|fatal)(\s|:|$)')
        {
            throw 'P5A Oracle verification emitted a warning, error, or fatal line.'
        }
    }
    foreach ($lineObject in $stderrLines)
    {
        $line = [string]$lineObject
        if ($line.Contains('P5A_', [StringComparison]::Ordinal))
        {
            throw 'P5A Oracle verification emitted a marker on stderr.'
        }
        if ($line -match '(?i)(^|:\s*)(warning|error|fatal)(\s|:|$)')
        {
            throw 'P5A Oracle verification emitted a warning, error, or fatal line.'
        }
    }
    if ($markers.Count -ne 1) { throw 'P5A Oracle verification marker is missing or duplicated.' }
    if ($null -eq $Result.PSObject.Properties['DescendantProcesses'] -or
        $null -eq $Result.DescendantProcesses)
    {
        throw 'P5A verifier Oracle child descendant metadata is missing.'
    }
    $descendants = @($Result.DescendantProcesses)
    if ($descendants.Count -eq 0) { return }
    $directId = [int]$Result.ProcessId
    $systemConhost = Get-P5aVerifierFullPath (Join-Path `
        ([Environment]::SystemDirectory) 'conhost.exe')
    if ($descendants.Count -ne 1)
    {
        throw 'P5A verifier Oracle child has a forbidden descendant process count.'
    }
    $record = $descendants[0]
    foreach ($property in @(
        'processId', 'parentProcessId', 'ancestorProcessIds', 'imageName', 'executablePath'))
    {
        if ($null -eq $record.PSObject.Properties[$property])
        {
            throw "P5A verifier Oracle child descendant metadata is missing '$property'."
        }
    }
    $ancestors = @($record.ancestorProcessIds | ForEach-Object { [int]$_ })
    if ([string]$record.imageName -cne 'conhost.exe' -or
        (Get-P5aVerifierFullPath ([string]$record.executablePath)) -ine $systemConhost -or
        [int]$record.parentProcessId -ne $directId -or
        $ancestors.Count -ne 1 -or $ancestors[0] -ne $directId)
    {
        throw 'P5A verifier Oracle child descendant is forbidden because it is not its direct system console host.'
    }
}

function Get-P5aVerifierDotnetClosure
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = Get-P5aVerifierFullPath $RepositoryRoot
    try { $global = [IO.File]::ReadAllText((Join-Path $root 'global.json')) | ConvertFrom-Json }
    catch { throw 'P5A selected dotnet SDK global.json is missing or malformed.' }
    $version = [string]$global.sdk.version
    if ($version -cnotmatch '^8\.0\.[0-9]+$') { throw 'P5A selected dotnet SDK version is invalid.' }
    $commands = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)
    if ($commands.Count -eq 0) { throw 'P5A selected dotnet application is missing.' }
    $dotnetPath = Get-P5aVerifierFullPath ([string]$commands[0].Source)
    Assert-P5aVerifierNoReparsePoint $dotnetPath
    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf) -or
        [IO.Path]::GetFileName($dotnetPath) -cne 'dotnet.exe')
    {
        throw 'P5A selected dotnet executable is invalid.'
    }
    $sdkRoot = Get-P5aVerifierFullPath (Join-Path (Split-Path -Parent $dotnetPath) "sdk\$version")
    Assert-P5aVerifierNoReparsePoint $sdkRoot
    if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container))
    {
        throw "P5A selected dotnet SDK root is missing: $sdkRoot"
    }
    $pathsByImage = @{}
    $rolesByPath = [Collections.Generic.Dictionary[string,string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $pathsByImage['dotnet.exe'] = [string[]]@($dotnetPath)
    $rolesByPath[$dotnetPath] = 'Dotnet'
    $fixedCandidates = [ordered]@{
        'MSBuild.exe' = [pscustomobject]@{
            Path = Join-Path $sdkRoot 'MSBuild.exe'; Role = 'SdkMsBuild'
        }
        'vstest.console.exe' = [pscustomobject]@{
            Path = Join-Path $sdkRoot 'vstest.console.exe'; Role = 'SdkVstest'
        }
        'testhost.exe' = [pscustomobject]@{
            Path = Join-Path $sdkRoot 'TestHostNetFramework\testhost.exe'
            Role = 'SdkTestHost'
        }
    }
    foreach ($candidate in $fixedCandidates.GetEnumerator())
    {
        $candidatePath = Get-P5aVerifierFullPath ([string]$candidate.Value.Path)
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf)
        {
            Assert-P5aVerifierNoReparsePoint $candidatePath
            $pathsByImage[[string]$candidate.Key] = [string[]]@($candidatePath)
            $rolesByPath[$candidatePath] = [string]$candidate.Value.Role
        }
    }
    $projectTestHostPath = Get-P5aVerifierFullPath (Join-Path `
        $root 'tests\Als.Core.Tests\bin\Debug\net8.0\testhost.exe')
    Assert-P5aVerifierNoReparsePoint $projectTestHostPath
    $pathsByImage['testhost.exe'] = [string[]]@(
        @($pathsByImage['testhost.exe']) + $projectTestHostPath)
    $rolesByPath[$projectTestHostPath] = 'ProjectTestHost'

    $systemConhostPath = Get-P5aVerifierFullPath (Join-Path `
        ([Environment]::SystemDirectory) 'conhost.exe')
    Assert-P5aVerifierNoReparsePoint $systemConhostPath
    if (-not (Test-Path -LiteralPath $systemConhostPath -PathType Leaf))
    {
        throw 'P5A selected dotnet system conhost executable is missing.'
    }
    $pathsByImage['conhost.exe'] = [string[]]@($systemConhostPath)
    $rolesByPath[$systemConhostPath] = 'SystemConhost'

    $oracleApphostPath = Get-P5aVerifierFullPath (Join-Path `
        $root 'tools\Als.P5aOracle\bin\Release\net8.0\Als.P5aOracle.exe')
    Assert-P5aVerifierNoReparsePoint $oracleApphostPath
    if (-not (Test-Path -LiteralPath $oracleApphostPath -PathType Leaf))
    {
        throw 'P5A selected dotnet prebuilt Oracle apphost is missing.'
    }
    $pathsByImage['Als.P5aOracle.exe'] = [string[]]@($oracleApphostPath)
    $rolesByPath[$oracleApphostPath] = 'OracleAppHost'
    return [pscustomobject]@{
        DotnetPath = $dotnetPath
        SdkRoot = $sdkRoot
        SdkVersion = $version
        PathsByImage = $pathsByImage
        RolesByPath = $rolesByPath
    }
}

function Assert-P5aVerifierDescendantClosure
{
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][object]$Closure
    )

    $directId = [int]$Result.ProcessId
    if ($directId -le 0) { throw 'P5A verifier descendant direct process id is invalid.' }
    if ($null -eq $Result.PSObject.Properties['DescendantProcesses'] -or
        $null -eq $Result.DescendantProcesses)
    {
        throw 'P5A verifier descendant metadata collection is missing.'
    }
    $records = @($Result.DescendantProcesses)
    $byId = @{}
    $rolesById = @{}
    foreach ($record in $records)
    {
        if ($null -eq $record)
        {
            throw 'P5A verifier descendant metadata record is null.'
        }
        foreach ($propertyName in @(
            'processId', 'parentProcessId', 'ancestorProcessIds',
            'imageName', 'executablePath'))
        {
            if ($null -eq $record.PSObject.Properties[$propertyName])
            {
                throw "P5A verifier descendant metadata '$propertyName' is missing."
            }
        }
        $recordId = [int]$record.processId
        if ($recordId -le 0 -or $byId.ContainsKey($recordId))
        {
            throw 'P5A verifier descendant has a duplicate process id.'
        }
        $byId[$recordId] = $record
    }
    foreach ($record in $records)
    {
        $recordId = [int]$record.processId
        $parentId = [int]$record.parentProcessId
        $image = [string]$record.imageName
        $path = [string]$record.executablePath
        if ([string]::IsNullOrWhiteSpace($image) -or
            [string]::IsNullOrWhiteSpace($path))
        {
            throw 'P5A verifier descendant metadata image or path is empty.'
        }
        if ($parentId -eq $recordId) { throw 'P5A verifier descendant has a self parent cycle.' }
        if (-not [IO.Path]::IsPathFullyQualified($path))
        {
            throw 'P5A verifier descendant path must be absolute.'
        }
        if ([IO.Path]::GetFileName($path) -cne $image)
        {
            throw 'P5A verifier descendant image basename does not match its path.'
        }
        $normalizedPath = Get-P5aVerifierFullPath $path
        Assert-P5aVerifierNoReparsePoint $normalizedPath
        if ($image -ceq 'dotnet.exe')
        {
            if ($null -eq $Closure.PSObject.Properties['DotnetPath'] -or
                $normalizedPath -ine (Get-P5aVerifierFullPath ([string]$Closure.DotnetPath)))
            {
                throw 'P5A verifier descendant is outside the selected dotnet executable closure.'
            }
        }
        elseif (@($Closure.PathsByImage.Keys | Where-Object {
                    [string]$_ -ceq $image
                }).Count -ne 1)
        {
            throw 'P5A verifier descendant process is forbidden.'
        }
        elseif (@($Closure.PathsByImage[$image] | Where-Object {
                    (Get-P5aVerifierFullPath ([string]$_)) -ieq $normalizedPath
                }).Count -ne 1)
        {
            throw 'P5A verifier descendant is outside the selected dotnet executable closure.'
        }
        if ($null -ne $Closure.PSObject.Properties['RolesByPath'])
        {
            if (-not $Closure.RolesByPath.ContainsKey($normalizedPath))
            {
                throw 'P5A verifier descendant role is outside the selected dotnet closure.'
            }
            $rolesById[$recordId] = [string]$Closure.RolesByPath[$normalizedPath]
        }
        else
        {
            $rolesById[$recordId] = $image
        }
        if (-not (Test-Path -LiteralPath $normalizedPath -PathType Leaf))
        {
            throw 'P5A verifier descendant executable in the selected closure is missing.'
        }
        if ($parentId -ne $directId -and -not $byId.ContainsKey($parentId))
        {
            throw 'P5A verifier descendant parent is outside the direct subtree.'
        }
        $ancestors = @($record.ancestorProcessIds | ForEach-Object { [int]$_ })
        if ($ancestors.Count -eq 0 -or $ancestors[0] -ne $directId)
        {
            throw 'P5A verifier descendant ancestor root is invalid.'
        }
        if (@($ancestors | Sort-Object -Unique).Count -ne $ancestors.Count)
        {
            throw 'P5A verifier descendant has a duplicate ancestor.'
        }
        if ($ancestors -contains $recordId) { throw 'P5A verifier descendant ancestor cycle detected.' }
        if ($ancestors[-1] -ne $parentId)
        {
            throw 'P5A verifier descendant parent is not the final ancestor.'
        }
        $expected = [Collections.Generic.List[int]]::new()
        $cursor = $parentId
        $seen = [Collections.Generic.HashSet[int]]::new()
        while ($cursor -ne $directId)
        {
            if (-not $seen.Add($cursor)) { throw 'P5A verifier descendant parent cycle detected.' }
            if (-not $byId.ContainsKey($cursor))
            {
                throw 'P5A verifier descendant parent is outside the subtree.'
            }
            $expected.Insert(0, $cursor)
            $cursor = [int]$byId[$cursor].parentProcessId
        }
        $expected.Insert(0, $directId)
        if (@(Compare-Object @($expected) $ancestors -SyncWindow 0).Count -ne 0)
        {
            throw 'P5A verifier descendant ancestor order is invalid.'
        }
    }
    foreach ($record in $records)
    {
        $recordId = [int]$record.processId
        $role = [string]$rolesById[$recordId]
        $parentId = [int]$record.parentProcessId
        if ($role -ceq 'ProjectTestHost' -and $parentId -ne $directId -and
            (-not $rolesById.ContainsKey($parentId) -or
             [string]$rolesById[$parentId] -cne 'Dotnet'))
        {
            throw 'P5A verifier project testhost parent must be the selected dotnet host.'
        }
        if ($role -ceq 'OracleAppHost' -and
            ($parentId -eq $directId -or -not $rolesById.ContainsKey($parentId) -or
             [string]$rolesById[$parentId] -cne 'ProjectTestHost'))
        {
            throw 'P5A verifier Oracle apphost parent must be the allowed project testhost.'
        }
        if ($role -cne 'SystemConhost') { continue }
        $parentIsAllowedRuntimeOwner = $rolesById.ContainsKey($parentId) -and
            [string]$rolesById[$parentId] -cin @(
                'ProjectTestHost', 'SdkTestHost', 'OracleAppHost')
        if ($parentId -ne $directId -and -not $parentIsAllowedRuntimeOwner)
        {
            $ancestors = @($record.ancestorProcessIds | ForEach-Object { [int]$_ })
            $parentRole = if ($rolesById.ContainsKey($parentId)) {
                [string]$rolesById[$parentId]
            } else {
                'Unknown'
            }
            throw "P5A verifier system conhost parent must be an allowed runtime owner: pid=$recordId,parent=$parentId,parentRole=$parentRole,ancestors=$($ancestors -join ',')."
        }
        if (@($records | Where-Object {
                    [int]$_.parentProcessId -eq $recordId
                }).Count -ne 0)
        {
            throw 'P5A verifier system conhost must be a terminal descendant.'
        }
    }
}

function Assert-P5aTrx
{
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$TrxPath
    )

    $staging = Get-P5aVerifierFullPath $StagingRoot
    $trx = Get-P5aVerifierFullPath $TrxPath
    Assert-P5aVerifierNoReparsePoint $staging
    Assert-P5aVerifierNoReparsePoint $trx
    $entries = @(Get-ChildItem -LiteralPath $staging -Force)
    if ($entries.Count -ne 1 -or $entries[0].PSIsContainer -or
        (Get-P5aVerifierFullPath $entries[0].FullName) -cne $trx)
    {
        throw 'P5A verifier requires exactly one expected TRX file.'
    }
    try { [xml]$document = [IO.File]::ReadAllText($trx) }
    catch { throw 'P5A verifier TRX is malformed.' }
    $counters = @($document.SelectNodes("//*[local-name()='Counters']"))
    if ($counters.Count -ne 1) { throw 'P5A verifier TRX counters are missing or duplicated.' }
    $counter = $counters[0]
    foreach ($name in @('total', 'passed', 'failed', 'error', 'notExecuted'))
    {
        if ($null -eq $counter.Attributes[$name] -or
            [string]$counter.Attributes[$name].Value -cnotmatch '^[0-9]+$')
        {
            throw "P5A verifier TRX counter '$name' is invalid."
        }
    }
    $skipped = 0
    if ($null -ne $counter.Attributes['skipped'])
    {
        if ([string]$counter.Attributes['skipped'].Value -cnotmatch '^[0-9]+$')
        {
            throw "P5A verifier TRX counter 'skipped' is invalid."
        }
        $skipped = [int]$counter.skipped
    }
    $total = [int]$counter.total
    if ($total -le 0 -or [int]$counter.passed -ne $total -or
        [int]$counter.failed -ne 0 -or [int]$counter.error -ne 0 -or
        [int]$counter.notExecuted -ne 0 -or $skipped -ne 0)
    {
        throw 'P5A verifier TRX reports zero, failed, skipped, or unexecuted tests.'
    }
}

function Assert-P5aVerifierDotnetChild
{
    param([Parameter(Mandatory)][object]$Result)

    if ($Result.TimedOut) { throw 'P5A focused dotnet test timed out.' }
    if ($null -ne $Result.PSObject.Properties['OutputLimitExceeded'] -and
        [bool]$Result.OutputLimitExceeded)
    {
        throw 'P5A focused dotnet test exceeded its output limit.'
    }
    if ([int]$Result.ExitCode -ne 0) { throw 'P5A focused dotnet test exited non-zero.' }
    foreach ($streamProperty in @('StdOutLines', 'StdErrLines'))
    {
        if ($null -eq $Result.PSObject.Properties[$streamProperty] -or
            $null -eq $Result.$streamProperty)
        {
            throw "P5A focused dotnet test $streamProperty evidence is missing."
        }
    }
    foreach ($lineObject in @($Result.StdOutLines) + @($Result.StdErrLines))
    {
        $line = [string]$lineObject
        if ($line.Contains('P5A_', [StringComparison]::Ordinal))
        {
            throw 'P5A focused dotnet test emitted a forbidden marker.'
        }
        if ($line -match '(?i)(^|:\s*)(warning|error|fatal)(\s|:|$)')
        {
            throw 'P5A focused dotnet test emitted a warning, error, or fatal line.'
        }
    }
}

function Open-P5aVerifierOwnedStaging
{
    param([Parameter(Mandatory)][string]$Path)

    $nativeType = 'P5aVerifierStagingNative' -as [type]
    if ($null -eq $nativeType)
    {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class P5aVerifierStagingNative
{
    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x00000080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const int FILE_DISPOSITION_INFO_CLASS = 4;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool DeleteFile;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string path, uint access, uint share, IntPtr securityAttributes,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        IntPtr handle, out BY_HANDLE_FILE_INFORMATION information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        IntPtr handle, StringBuilder path, uint pathLength, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(
        IntPtr handle, int informationClass,
        ref FILE_DISPOSITION_INFO information, uint informationSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public sealed class Lease : IDisposable
    {
        private IntPtr handle;
        private readonly uint volume;
        private readonly ulong fileId;
        public string Path { get; private set; }
        public string FinalPath { get; private set; }

        internal Lease(
            string path, string finalPath, IntPtr handle, uint volume, ulong fileId)
        {
            Path = path;
            FinalPath = finalPath;
            this.handle = handle;
            this.volume = volume;
            this.fileId = fileId;
        }

        public bool MatchesPath()
        {
            if (handle == IntPtr.Zero) return false;
            IntPtr candidate = OpenProbe(Path);
            if (candidate == INVALID_HANDLE_VALUE) return false;
            try
            {
                BY_HANDLE_FILE_INFORMATION information;
                if (!GetFileInformationByHandle(candidate, out information)) return false;
                ulong candidateId = ((ulong)information.FileIndexHigh << 32) |
                    information.FileIndexLow;
                string candidateFinal = System.IO.Path.GetFullPath(ReadFinalPath(candidate));
                return information.VolumeSerialNumber == volume && candidateId == fileId &&
                    (information.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
                    (information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0 &&
                    FinalPath.Equals(candidateFinal, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(candidate); }
        }

        public void DeleteIfEmpty()
        {
            if (handle == IntPtr.Zero)
                throw new ObjectDisposedException("P5A verifier staging lease");
            var disposition = new FILE_DISPOSITION_INFO { DeleteFile = true };
            if (!SetFileInformationByHandle(
                    handle, FILE_DISPOSITION_INFO_CLASS, ref disposition,
                    (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO))))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot atomically delete the P5A verifier staging directory");
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }

    public static Lease CreateOwned(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!CreateDirectoryW(fullPath, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "P5A verifier staging directory must be newly created by this run");
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = CreateFileW(
                fullPath, FILE_READ_ATTRIBUTES | DELETE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE)
            {
                int error = Marshal.GetLastWin32Error();
                handle = IntPtr.Zero;
                throw new Win32Exception(error,
                    "Cannot retain the P5A verifier staging directory handle");
            }
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(handle, out information))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot identify the P5A verifier staging directory");
            if ((information.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new InvalidOperationException(
                    "P5A verifier staging path is not a directory.");
            if ((information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                throw new InvalidOperationException(
                    "P5A verifier staging path is a reparse point.");
            string finalPath = System.IO.Path.GetFullPath(ReadFinalPath(handle));
            if (!fullPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "P5A verifier staging canonical path does not match its requested path.");
            ulong fileId = ((ulong)information.FileIndexHigh << 32) |
                information.FileIndexLow;
            var lease = new Lease(
                fullPath, finalPath, handle, information.VolumeSerialNumber, fileId);
            handle = IntPtr.Zero;
            return lease;
        }
        catch
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var disposition = new FILE_DISPOSITION_INFO { DeleteFile = true };
                    SetFileInformationByHandle(
                        handle, FILE_DISPOSITION_INFO_CLASS, ref disposition,
                        (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO)));
                }
                catch { }
            }
            else
            {
                try { System.IO.Directory.Delete(fullPath, false); }
                catch { }
            }
            throw;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    private static IntPtr OpenProbe(string path)
    {
        return CreateFileW(
            path, FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
    }

    public static string FinalDirectoryPath(string path)
    {
        IntPtr handle = OpenProbe(path);
        if (handle == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot open a directory for canonical identity resolution");
        try { return ReadFinalPath(handle); }
        finally { CloseHandle(handle); }
    }

    private static string ReadFinalPath(IntPtr handle)
    {
        var path = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot resolve a canonical directory path");
        string value = path.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + value.Substring(8);
        if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return value.Substring(4);
        return value;
    }
}
'@
        $nativeType = 'P5aVerifierStagingNative' -as [type]
    }
    return $nativeType::CreateOwned((Get-P5aVerifierFullPath $Path))
}

function Assert-P5aVerifierOwnedStaging
{
    param([Parameter(Mandatory)][object]$Lease)

    if (-not $Lease.MatchesPath())
    {
        throw 'P5A verifier staging directory identity changed.'
    }
    Assert-P5aVerifierNoReparsePoint ([string]$Lease.Path)
}

function Remove-P5aVerifierPathStrict
{
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return }
    if ($item.PSIsContainer)
    {
        throw "P5A verifier staging contains an unexpected child directory: $($item.FullName)"
    }
    [IO.File]::Delete($item.FullName)
    if (Test-Path -LiteralPath $item.FullName)
    {
        throw "P5A verifier staging file remained after strict cleanup: $($item.FullName)"
    }
}

function Close-P5aVerifierOwnedStaging
{
    param([Parameter(Mandatory)][object]$Lease)

    $path = Get-P5aVerifierFullPath ([string]$Lease.Path)
    try
    {
        Assert-P5aVerifierOwnedStaging $Lease
        foreach ($child in @(Get-ChildItem -LiteralPath $path -Force))
        {
            Remove-P5aVerifierPathStrict $child.FullName
        }
        Assert-P5aVerifierOwnedStaging $Lease
        $Lease.DeleteIfEmpty()
    }
    finally { $Lease.Dispose() }
    if (Test-Path -LiteralPath $path)
    {
        throw 'P5A verifier staging directory remained after strict cleanup.'
    }
}

function Invoke-P5aVerifierProcessProtocol
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$OracleAppHost,
        [Parameter(Mandatory)][string]$FixturePath,
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][scriptblock]$ProcessInvoker,
        [scriptblock]$CheckpointInvoker,
        [object]$OracleLease,
        [int]$TimeoutSeconds = 28800
    )

    Assert-P5aVerifierInvocationContract `
        -RepositoryRoot $RepositoryRoot -FixturePath $FixturePath `
        -StagingRoot $StagingRoot -OracleAppHost $OracleAppHost `
        -TimeoutSeconds $TimeoutSeconds
    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $fixture = Get-P5aVerifierFullPath $FixturePath
    $staging = Get-P5aVerifierFullPath $StagingRoot
    if ((Test-P5aVerifierPathWithin -Path $staging -Root $root) -or
        (Test-P5aVerifierPathWithin -Path $root -Root $staging))
    {
        throw 'P5A verifier staging and repository must not contain one another.'
    }
    if (Test-Path -LiteralPath $staging)
    {
        throw 'P5A verifier staging directory must not preexist.'
    }
    $environmentGate = Enter-P5aVerifierEnvironmentGate $TimeoutSeconds
    $stagingLease = $null
    $protocolError = $null
    $cleanupError = $null
    $protocolSucceeded = $false
    $environmentName = 'GODOTALS_P5A_STAGING_ROOT'
    $ambient = [Environment]::GetEnvironmentVariable(
        $environmentName, [EnvironmentVariableTarget]::Process)
    $prebuiltEnvironmentName = 'GODOTALS_P5A_PREBUILT_ORACLE_APPHOST'
    $prebuiltAmbient = [Environment]::GetEnvironmentVariable(
        $prebuiltEnvironmentName, [EnvironmentVariableTarget]::Process)
    try
    {
        $stagingLease = Open-P5aVerifierOwnedStaging $staging
        Assert-P5aVerifierOwnedStaging $stagingLease
        $stagingNative = 'P5aVerifierStagingNative' -as [type]
        $physicalRoot = Get-P5aVerifierFullPath (
            $stagingNative::FinalDirectoryPath($root))
        $physicalStaging = Get-P5aVerifierFullPath ([string]$stagingLease.FinalPath)
        if ((Test-P5aVerifierPathWithin -Path $physicalStaging -Root $physicalRoot) -or
            (Test-P5aVerifierPathWithin -Path $physicalRoot -Root $physicalStaging))
        {
            throw 'P5A verifier staging physical identity overlaps the repository.'
        }
        Invoke-P5aVerifierCheckpoint $CheckpointInvoker 'BeforeOracleChild' $OracleLease
        Assert-P5aVerifierOwnedStaging $stagingLease
        if ($null -ne $OracleLease)
        {
            Assert-P5aVerifierOracleEvidenceUnchanged `
                -RepositoryRoot $root -ExpectedEvidence $OracleLease.Evidence `
                -FixedLeasesByPath $OracleLease.FixedLeasesByPath
            Assert-P5aVerifierRuntimeInputEvidenceUnchanged `
                -RepositoryRoot $root -FixturePath $fixture `
                -ExpectedEvidence $OracleLease.InputEvidence `
                -FixedLeasesByPath $OracleLease.FixedLeasesByPath
        }
        [Environment]::SetEnvironmentVariable(
            $environmentName, $staging, [EnvironmentVariableTarget]::Process)
        try
        {
            $oracleResult = Invoke-P5aVerifierChild -ProcessInvoker $ProcessInvoker `
                -FilePath $OracleAppHost `
                -Arguments @('--verify-fixture', '--repository-root', $root, '--fixture', $fixture) `
                -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds `
                -PhaseName 'P5A Oracle fixture verification'
            $observed = [Environment]::GetEnvironmentVariable(
                $environmentName, [EnvironmentVariableTarget]::Process)
            if ([string]::IsNullOrEmpty($observed))
            {
                throw 'P5A verifier staging environment is missing after Oracle child.'
            }
            if (-not [IO.Path]::IsPathFullyQualified($observed))
            {
                throw 'P5A verifier staging environment must remain absolute.'
            }
            if ((Get-P5aVerifierFullPath $observed) -ine $staging)
            {
                throw 'P5A verifier staging environment changed outside its owned directory.'
            }
            Assert-P5aVerifierOracleChild $oracleResult
            Assert-P5aVerifierOwnedStaging $stagingLease
        }
        finally
        {
            [Environment]::SetEnvironmentVariable(
                $environmentName, $ambient, [EnvironmentVariableTarget]::Process)
        }
        Invoke-P5aVerifierCheckpoint $CheckpointInvoker 'OracleChildCompleted' $OracleLease
        Assert-P5aVerifierOwnedStaging $stagingLease
        Invoke-P5aVerifierCheckpoint $CheckpointInvoker 'BeforeDotnetChild' $OracleLease
        Assert-P5aVerifierOwnedStaging $stagingLease
        if ($null -ne $OracleLease)
        {
            Assert-P5aVerifierOracleEvidenceUnchanged `
                -RepositoryRoot $root -ExpectedEvidence $OracleLease.Evidence `
                -FixedLeasesByPath $OracleLease.FixedLeasesByPath
            Assert-P5aVerifierRuntimeInputEvidenceUnchanged `
                -RepositoryRoot $root -FixturePath $fixture `
                -ExpectedEvidence $OracleLease.InputEvidence `
                -FixedLeasesByPath $OracleLease.FixedLeasesByPath
        }
        $closure = Get-P5aVerifierDotnetClosure $root
        $trxPath = Join-Path $staging 'p5a-golden-tests.trx'
        [Environment]::SetEnvironmentVariable(
            $prebuiltEnvironmentName, (Get-P5aVerifierFullPath $OracleAppHost),
            [EnvironmentVariableTarget]::Process)
        try
        {
            $dotnetResult = Invoke-P5aVerifierChild -ProcessInvoker $ProcessInvoker `
                -FilePath $closure.DotnetPath `
                -Arguments @(
                    'test', 'tests/Als.Core.Tests/Als.Core.Tests.csproj', '-c', 'Debug',
                    '--filter', 'FullyQualifiedName~AlsP5aGoldenTests', '--logger',
                    "trx;LogFileName=$trxPath") `
                -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds `
                -PhaseName 'P5A focused dotnet fixture tests'
        }
        finally
        {
            [Environment]::SetEnvironmentVariable(
                $prebuiltEnvironmentName, $prebuiltAmbient,
                [EnvironmentVariableTarget]::Process)
        }
        Assert-P5aVerifierDotnetChild $dotnetResult
        Assert-P5aVerifierDescendantClosure -Result $dotnetResult -Closure $closure
        Assert-P5aVerifierOwnedStaging $stagingLease
        Assert-P5aTrx -StagingRoot $staging -TrxPath $trxPath
        Invoke-P5aVerifierCheckpoint $CheckpointInvoker 'ChildrenCompleted' $OracleLease
        Assert-P5aVerifierOwnedStaging $stagingLease
        $protocolSucceeded = $true
    }
    catch { $protocolError = $_ }
    finally
    {
        [Environment]::SetEnvironmentVariable(
            $environmentName, $ambient, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $prebuiltEnvironmentName, $prebuiltAmbient,
            [EnvironmentVariableTarget]::Process)
        if ($null -ne $stagingLease)
        {
            try { Close-P5aVerifierOwnedStaging $stagingLease }
            catch { $cleanupError = $_ }
        }
        try { Exit-P5aVerifierEnvironmentGate $environmentGate }
        catch { if ($null -eq $cleanupError) { $cleanupError = $_ } }
    }
    if ($null -ne $protocolError) { throw $protocolError }
    if ($null -ne $cleanupError) { throw $cleanupError }
    if (-not $protocolSucceeded) { throw 'P5A verifier protocol did not complete.' }
    "P5A_GOLDEN_FIXTURE_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17"
}

function Invoke-P5aVerifierWorkflow
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$FixturePath,
        [Parameter(Mandatory)][string]$StagingRoot,
        [scriptblock]$ProcessInvoker = ${function:Invoke-P5aVerifierDefaultProcess},
        [scriptblock]$CheckpointInvoker,
        [int]$TimeoutSeconds = 28800
    )
    Assert-P5aVerifierInvocationContract `
        -RepositoryRoot $RepositoryRoot -FixturePath $FixturePath `
        -StagingRoot $StagingRoot -TimeoutSeconds $TimeoutSeconds
    $root = Get-P5aVerifierFullPath $RepositoryRoot
    $lease = $null
    try
    {
        $lease = Open-P5aVerifierOracleEvidenceLease `
            -RepositoryRoot $root -FixturePath $FixturePath
        Invoke-P5aVerifierCheckpoint $CheckpointInvoker 'OracleEvidenceOpened' $lease
        $output = @(Invoke-P5aVerifierProcessProtocol `
            -RepositoryRoot $root -OracleAppHost $lease.AppHostPath `
            -FixturePath $FixturePath -StagingRoot $StagingRoot `
            -ProcessInvoker $ProcessInvoker -CheckpointInvoker $CheckpointInvoker `
            -OracleLease $lease -TimeoutSeconds $TimeoutSeconds)
        Assert-P5aVerifierOracleEvidenceUnchanged `
            -RepositoryRoot $root -ExpectedEvidence $lease.Evidence `
            -FixedLeasesByPath $lease.FixedLeasesByPath
        Assert-P5aVerifierRuntimeInputEvidenceUnchanged `
            -RepositoryRoot $root -FixturePath $FixturePath `
            -ExpectedEvidence $lease.InputEvidence `
            -FixedLeasesByPath $lease.FixedLeasesByPath
        return @($output)
    }
    finally
    {
        if ($null -ne $lease) { $lease.Dispose() }
    }
}

if ($MyInvocation.InvocationName -ne '.')
{
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
    {
        $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    }
    if ([string]::IsNullOrWhiteSpace($FixturePath))
    {
        $FixturePath = Join-Path $RepositoryRoot `
            'tests\Als.Core.Tests\Fixtures\P5A\trace_p5a_runtime.json'
    }
    if ([string]::IsNullOrWhiteSpace($StagingRoot))
    {
        $StagingRoot = Join-Path ([IO.Path]::GetTempPath()) `
            ('godotals-p5a-verifier-' + [Guid]::NewGuid().ToString('N'))
    }
    $workflowParameters = @{
        RepositoryRoot = $RepositoryRoot
        FixturePath = $FixturePath
        StagingRoot = $StagingRoot
        TimeoutSeconds = $TimeoutSeconds
    }
    if ($null -ne $ProcessInvoker) { $workflowParameters.ProcessInvoker = $ProcessInvoker }
    if ($null -ne $CheckpointInvoker) { $workflowParameters.CheckpointInvoker = $CheckpointInvoker }
    Invoke-P5aVerifierWorkflow @workflowParameters
}
