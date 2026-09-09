param(
    [string]$RepositoryRoot,
    [string]$UnrealEditorCmd,
    [Alias('UProject')][string]$UnrealProject,
    [string]$ReferenceRoot,
    [string]$StagingRoot,
    [string]$DestinationPath,
    [scriptblock]$ProcessInvoker,
    [scriptblock]$FileSystemInvoker,
    [scriptblock]$CheckpointInvoker,
    [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-P5aGeneratorDefaultProcess
{
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$PhaseName
    )

    $nativeType = 'P5aGeneratorPreboundNativeProcess' -as [type]
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
using Microsoft.Win32.SafeHandles;

public static class P5aGeneratorPreboundNativeProcess
{
    private const int MaxStdOutBytes = 8388608;
    private const int MaxStdErrBytes = 8388608;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x0002000D;
    private const uint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    private const uint JOB_OBJECT_MSG_NEW_PROCESS = 6;
    private const uint JOB_OBJECT_MSG_EXIT_PROCESS = 7;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint STILL_ACTIVE = 259;
    private const int ERROR_TIMEOUT = 258;
    private const uint MaxActiveProcesses = 128;
    private const int MaxTrackedProcesses = 4096;
    private const int MaxProcessDepth = 64;
    private const int MaxExecutablePathCharacters = 8388608;
    private const int MaxAncestorEdges = 262144;

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
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
        public ulong ReadOperations;
        public ulong WriteOperations;
        public ulong OtherOperations;
        public ulong ReadBytes;
        public ulong WriteBytes;
        public ulong OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory;
        public UIntPtr JobMemory;
        public UIntPtr PeakProcessMemory;
        public UIntPtr PeakJobMemory;
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

    public sealed class RunResult
    {
        public int ProcessId { get; set; }
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public bool OutputLimitExceeded { get; set; }
        public string[] StdOutLines { get; set; }
        public string[] StdErrLines { get; set; }
        public long StdOutBytes { get; set; }
        public long StdErrBytes { get; set; }
        public string[] OutputLines { get; set; }
        public DescendantRecord[] DescendantProcesses { get; set; }
        public int JobTotalProcesses { get; set; }
        public int JobActiveProcesses { get; set; }
        public int[] JobProcessIds { get; set; }
    }

    private sealed class NativeRecord
    {
        public int ProcessId;
        public int ParentProcessId;
        public string ImageName;
        public string ExecutablePath;
        public IntPtr Handle;
    }

    private sealed class CaptureState
    {
        public readonly object Gate = new object();
        public readonly int Limit;
        public readonly MemoryStream Bytes = new MemoryStream();
        public long ByteCount;
        public bool Exceeded;
        public string Error;
        public bool Complete;

        public CaptureState(int limit) { Limit = limit; }
    }

    private sealed class CollectorState
    {
        public readonly object Gate = new object();
        public readonly Dictionary<int, NativeRecord> Records = new Dictionary<int, NativeRecord>();
        public readonly HashSet<int> ProcessIds = new HashSet<int>();
        public volatile bool Stop;
        public string MetadataFailure;
        public int RootProcessId;
        public Stopwatch Clock;
        public long DeadlineMilliseconds;
        public bool ActiveProcessZeroSeen;
        public long LastMessageMilliseconds = -1;
        public bool ResourcesReleased;
        public int ExecutablePathCharacters;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
        IntPtr environment, string currentDirectory, ref STARTUPINFOEX startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr readPipe, out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES attributes, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(
        IntPtr handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(
        IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(
        IntPtr fileHandle, IntPtr existingCompletionPort,
        IntPtr completionKey, uint concurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information,
        uint informationLength, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(
        IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatus(
        IntPtr completionPort, out uint completionCode,
        out IntPtr completionKey, out IntPtr overlapped, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PostQueuedCompletionStatus(
        IntPtr completionPort, uint completionCode,
        IntPtr completionKey, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value,
        IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(
        IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(
        IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr process, uint flags, StringBuilder executablePath, ref uint size);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process, int informationClass, out PROCESS_BASIC_INFORMATION information,
        int informationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static RunResult Run(
        string filePath, string[] arguments, string workingDirectory, int timeoutSeconds)
    {
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        string executable = Path.GetFullPath(filePath);
        string currentDirectory = Path.GetFullPath(workingDirectory);
        IntPtr job = IntPtr.Zero;
        IntPtr completionPort = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr jobListValue = IntPtr.Zero;
        IntPtr handleListValue = IntPtr.Zero;
        IntPtr standardInputRead = IntPtr.Zero;
        IntPtr standardInputWrite = IntPtr.Zero;
        IntPtr standardOutputRead = IntPtr.Zero;
        IntPtr standardOutputWrite = IntPtr.Zero;
        IntPtr standardErrorRead = IntPtr.Zero;
        IntPtr standardErrorWrite = IntPtr.Zero;
        bool attributeListInitialized = false;
        PROCESS_INFORMATION processInformation = new PROCESS_INFORMATION();
        CollectorState collectorState = null;
        Thread collectorThread = null;
        Thread standardOutputThread = null;
        Thread standardErrorThread = null;
        bool collectorStarted = false;
        CaptureState standardOutput = null;
        CaptureState standardError = null;
        var collectorReady = new ManualResetEventSlim(false);
        var clock = Stopwatch.StartNew();
        long runDeadlineMilliseconds = checked((long)timeoutSeconds * 1000L);
        long cleanupDeadlineMilliseconds = runDeadlineMilliseconds + 2000L;
        bool timedOut = false;
        bool outputLimitExceeded = false;

        try
        {
            job = CreateConfiguredJob(out completionPort);
            var attributes = new SECURITY_ATTRIBUTES
            {
                Length = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                InheritHandle = true
            };
            CreateInheritedPipe(out standardInputRead, out standardInputWrite, ref attributes, true);
            CreateInheritedPipe(out standardOutputRead, out standardOutputWrite, ref attributes, false);
            CreateInheritedPipe(out standardErrorRead, out standardErrorWrite, ref attributes, false);

            IntPtr attributeBytes = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeBytes);
            attributeList = Marshal.AllocHGlobal(attributeBytes);
            if (!InitializeProcThreadAttributeList(attributeList, 2, 0, ref attributeBytes))
                ThrowLastError("InitializeProcThreadAttributeList");
            attributeListInitialized = true;

            jobListValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(jobListValue, job);
            if (!UpdateProcThreadAttribute(
                    attributeList, 0, new IntPtr(PROC_THREAD_ATTRIBUTE_JOB_LIST),
                    jobListValue, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                ThrowLastError("UpdateProcThreadAttribute PROC_THREAD_ATTRIBUTE_JOB_LIST");

            handleListValue = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handleListValue, 0, standardInputRead);
            Marshal.WriteIntPtr(handleListValue, IntPtr.Size, standardOutputWrite);
            Marshal.WriteIntPtr(handleListValue, IntPtr.Size * 2, standardErrorWrite);
            if (!UpdateProcThreadAttribute(
                    attributeList, 0, new IntPtr(PROC_THREAD_ATTRIBUTE_HANDLE_LIST),
                    handleListValue, new IntPtr(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
                ThrowLastError("UpdateProcThreadAttribute PROC_THREAD_ATTRIBUTE_HANDLE_LIST");

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.StartupInfo.Flags = (int)STARTF_USESTDHANDLES;
            startup.StartupInfo.StandardInput = standardInputRead;
            startup.StartupInfo.StandardOutput = standardOutputWrite;
            startup.StartupInfo.StandardError = standardErrorWrite;
            startup.AttributeList = attributeList;
            var commandLine = new StringBuilder(BuildCommandLine(executable, arguments));
            if (!CreateProcessW(
                    executable, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                    CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero, currentDirectory, ref startup, out processInformation))
                ThrowLastError("CreateProcessW");
            CloseOwnedHandle(ref standardInputRead);
            CloseOwnedHandle(ref standardInputWrite);
            CloseOwnedHandle(ref standardOutputWrite);
            CloseOwnedHandle(ref standardErrorWrite);

            standardOutput = new CaptureState(MaxStdOutBytes);
            standardError = new CaptureState(MaxStdErrBytes);
            standardOutputThread = NewCaptureThread(standardOutputRead, standardOutput);
            standardErrorThread = NewCaptureThread(standardErrorRead, standardError);

            collectorState = new CollectorState
            {
                RootProcessId = unchecked((int)processInformation.ProcessId),
                Clock = clock,
                DeadlineMilliseconds = cleanupDeadlineMilliseconds
            };
            collectorState.ProcessIds.Add(collectorState.RootProcessId);
            collectorThread = new Thread(() =>
                CollectJobProcesses(completionPort, collectorState, collectorReady));
            collectorThread.IsBackground = true;
            collectorThread.Name = "P5A generator process metadata collector";
            collectorThread.Start();
            collectorStarted = true;
            standardOutputThread.Start();
            standardOutputRead = IntPtr.Zero;
            standardErrorThread.Start();
            standardErrorRead = IntPtr.Zero;

            if (!collectorReady.Wait(RemainingMilliseconds(clock, runDeadlineMilliseconds)))
                throw new InvalidOperationException("P5A process metadata collector was not ready.");
            if (ResumeThread(processInformation.Thread) == UInt32.MaxValue)
                ThrowLastError("ResumeThread");
            CloseOwnedHandle(ref processInformation.Thread);

            bool terminated = false;
            bool completedBeforeDeadline = false;
            while (RemainingMilliseconds(clock, runDeadlineMilliseconds) > 0)
            {
                if (CaptureExceeded(standardOutput) || CaptureExceeded(standardError))
                {
                    outputLimitExceeded = true;
                    TerminateJobObject(job, 2);
                    terminated = true;
                    break;
                }

                JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting = QueryAccounting(job);
                bool rootExited = WaitForSingleObject(processInformation.Process, 0) == WAIT_OBJECT_0;
                if (rootExited && accounting.ActiveProcesses == 0)
                {
                    completedBeforeDeadline = true;
                    break;
                }
                Thread.Sleep(Math.Min(2, RemainingMilliseconds(clock, runDeadlineMilliseconds)));
            }

            if (!completedBeforeDeadline && !outputLimitExceeded)
            {
                timedOut = true;
                TerminateJobObject(job, 1);
                terminated = true;
            }
            if (CaptureExceeded(standardOutput) || CaptureExceeded(standardError))
            {
                outputLimitExceeded = true;
                timedOut = false;
                if (!terminated) TerminateJobObject(job, 2);
            }

            cleanupDeadlineMilliseconds = checked(clock.ElapsedMilliseconds + 2000L);
            collectorState.DeadlineMilliseconds = cleanupDeadlineMilliseconds;

            while (RemainingMilliseconds(clock, cleanupDeadlineMilliseconds) > 0)
            {
                JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting = QueryAccounting(job);
                bool readersComplete = CaptureComplete(standardOutput) && CaptureComplete(standardError);
                if (accounting.ActiveProcesses == 0 && readersComplete &&
                    CollectorQueueClosed(collectorState, accounting.TotalProcesses)) break;
                Thread.Sleep(Math.Min(2, RemainingMilliseconds(clock, cleanupDeadlineMilliseconds)));
            }

            SignalCollectorStop(completionPort, collectorState);
            int remaining = RemainingMilliseconds(clock, cleanupDeadlineMilliseconds);
            if (collectorThread.IsAlive && !collectorThread.Join(remaining))
                throw new InvalidOperationException("P5A process metadata collector exceeded its deadline.");
            remaining = RemainingMilliseconds(clock, cleanupDeadlineMilliseconds);
            if (standardOutputThread.IsAlive && !standardOutputThread.Join(remaining))
                throw new InvalidOperationException("P5A stdout collector exceeded its deadline.");
            remaining = RemainingMilliseconds(clock, cleanupDeadlineMilliseconds);
            if (standardErrorThread.IsAlive && !standardErrorThread.Join(remaining))
                throw new InvalidOperationException("P5A stderr collector exceeded its deadline.");

            if (CaptureExceeded(standardOutput) || CaptureExceeded(standardError))
            {
                outputLimitExceeded = true;
                timedOut = false;
                if (!terminated) TerminateJobObject(job, 2);
            }

            JOBOBJECT_BASIC_ACCOUNTING_INFORMATION finalAccounting = QueryAccounting(job);
            if (finalAccounting.ActiveProcesses != 0)
                throw new InvalidOperationException("P5A Job active process accounting did not reach zero.");
            if (!CollectorQueueClosed(collectorState, finalAccounting.TotalProcesses))
                throw new InvalidOperationException(
                    "P5A Job completion queue did not close after ACTIVE_PROCESS_ZERO.");
            if (standardOutput.Error != null || standardError.Error != null)
                throw new InvalidOperationException(
                    "P5A stream collector failed: " + (standardOutput.Error ?? standardError.Error));

            NativeRecord[] nativeRecords;
            int[] jobProcessIds;
            string metadataFailure;
            lock (collectorState.Gate)
            {
                nativeRecords = collectorState.Records.Values.OrderBy(value => value.ProcessId).ToArray();
                jobProcessIds = collectorState.ProcessIds.OrderBy(value => value).ToArray();
                metadataFailure = collectorState.MetadataFailure;
            }
            if (metadataFailure != null)
                throw new InvalidOperationException("P5A descendant metadata failure: " + metadataFailure);
            if (finalAccounting.TotalProcesses != jobProcessIds.Length)
                throw new InvalidOperationException(
                    "P5A Job TotalProcesses != captured JobProcessIds closure.");

            DescendantRecord[] descendants = BuildDescendants(
                collectorState.RootProcessId, nativeRecords);
            if (finalAccounting.TotalProcesses != 1 + descendants.Length)
                throw new InvalidOperationException(
                    "P5A Job TotalProcesses != root plus complete descendants.");

            uint nativeExitCode;
            if (!GetExitCodeProcess(processInformation.Process, out nativeExitCode) ||
                nativeExitCode == STILL_ACTIVE)
                ThrowLastError("GetExitCodeProcess");
            int exitCode = unchecked((int)nativeExitCode);
            if ((timedOut || outputLimitExceeded) && exitCode == 0) exitCode = -1;
            string[] stdoutLines = CaptureLines(standardOutput);
            string[] stderrLines = CaptureLines(standardError);
            return new RunResult
            {
                ProcessId = collectorState.RootProcessId,
                ExitCode = exitCode,
                TimedOut = timedOut,
                OutputLimitExceeded = outputLimitExceeded,
                StdOutLines = stdoutLines,
                StdErrLines = stderrLines,
                StdOutBytes = standardOutput.ByteCount,
                StdErrBytes = standardError.ByteCount,
                OutputLines = stdoutLines.Concat(stderrLines).ToArray(),
                DescendantProcesses = descendants,
                JobTotalProcesses = unchecked((int)finalAccounting.TotalProcesses),
                JobActiveProcesses = unchecked((int)finalAccounting.ActiveProcesses),
                JobProcessIds = jobProcessIds
            };
        }
        finally
        {
            if (collectorState != null) SignalCollectorStop(completionPort, collectorState);
            if (job != IntPtr.Zero) TerminateJobObject(job, 3);
            if (collectorThread != null && collectorThread.IsAlive)
                collectorThread.Join(
                    Math.Max(0, RemainingMilliseconds(clock, cleanupDeadlineMilliseconds)));
            if (standardOutputThread != null && standardOutputThread.IsAlive)
                standardOutputThread.Join(Math.Max(0, RemainingMilliseconds(clock, cleanupDeadlineMilliseconds)));
            if (standardErrorThread != null && standardErrorThread.IsAlive)
                standardErrorThread.Join(Math.Max(0, RemainingMilliseconds(clock, cleanupDeadlineMilliseconds)));
            CloseOwnedHandle(ref processInformation.Thread);
            CloseOwnedHandle(ref processInformation.Process);
            CloseOwnedHandle(ref standardInputRead);
            CloseOwnedHandle(ref standardInputWrite);
            CloseOwnedHandle(ref standardOutputRead);
            CloseOwnedHandle(ref standardOutputWrite);
            CloseOwnedHandle(ref standardErrorRead);
            CloseOwnedHandle(ref standardErrorWrite);
            if (attributeList != IntPtr.Zero)
            {
                if (attributeListInitialized) DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (jobListValue != IntPtr.Zero) Marshal.FreeHGlobal(jobListValue);
            if (handleListValue != IntPtr.Zero) Marshal.FreeHGlobal(handleListValue);
            if (!collectorStarted)
            {
                CloseRecordHandles(collectorState);
                CloseOwnedHandle(ref completionPort);
                collectorReady.Dispose();
            }
            CloseOwnedHandle(ref job);
        }
    }

    public static int RemainingMilliseconds(Stopwatch clock, long deadlineMilliseconds)
    {
        long remaining = deadlineMilliseconds - clock.ElapsedMilliseconds;
        if (remaining <= 0) return 0;
        return remaining > Int32.MaxValue ? Int32.MaxValue : unchecked((int)remaining);
    }

    private static IntPtr CreateConfiguredJob(out IntPtr completionPort)
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) ThrowLastError("CreateJobObject");
        completionPort = IntPtr.Zero;
        try
        {
            completionPort = CreateIoCompletionPort(
                new IntPtr(-1), IntPtr.Zero, IntPtr.Zero, 1);
            if (completionPort == IntPtr.Zero) ThrowLastError("CreateIoCompletionPort");

            var association = new CompletionAssociation { CompletionPort = completionPort };
            SetJobInformation(job, 7, association);
            var limits = new ExtendedLimits();
            limits.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            limits.Basic.ActiveProcessLimit = MaxActiveProcesses;
            SetJobInformation(job, 9, limits);
            return job;
        }
        catch
        {
            CloseOwnedHandle(ref completionPort);
            CloseHandle(job);
            throw;
        }
    }

    private static void SetJobInformation<T>(IntPtr job, int informationClass, T value)
        where T : struct
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            if (!SetInformationJobObject(
                    job, informationClass, buffer, unchecked((uint)Marshal.SizeOf<T>())))
                ThrowLastError("SetInformationJobObject");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void CreateInheritedPipe(
        out IntPtr readPipe, out IntPtr writePipe,
        ref SECURITY_ATTRIBUTES attributes, bool inheritRead)
    {
        if (!CreatePipe(out readPipe, out writePipe, ref attributes, 0))
            ThrowLastError("CreatePipe");
        IntPtr privateEnd = inheritRead ? writePipe : readPipe;
        if (!SetHandleInformation(privateEnd, HANDLE_FLAG_INHERIT, 0))
        {
            CloseHandle(readPipe);
            CloseHandle(writePipe);
            readPipe = IntPtr.Zero;
            writePipe = IntPtr.Zero;
            ThrowLastError("SetHandleInformation");
        }
    }

    private static Thread NewCaptureThread(IntPtr pipe, CaptureState state)
    {
        return new Thread(() =>
        {
            try
            {
                using (var stream = new FileStream(
                    new SafeFileHandle(pipe, true), FileAccess.Read, 65536, false))
                {
                    var buffer = new byte[65536];
                    while (true)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        lock (state.Gate)
                        {
                            long next = state.ByteCount + read;
                            if (next > state.Limit)
                            {
                                int retained = Math.Max(
                                    0, state.Limit - unchecked((int)state.Bytes.Length));
                                if (retained > 0) state.Bytes.Write(buffer, 0, Math.Min(retained, read));
                                state.ByteCount = state.Limit + 1L;
                                state.Exceeded = true;
                            }
                            else
                            {
                                state.ByteCount = next;
                                state.Bytes.Write(buffer, 0, read);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                lock (state.Gate) state.Error = exception.Message;
            }
            finally
            {
                lock (state.Gate) state.Complete = true;
            }
        })
        {
            IsBackground = true,
            Name = "P5A bounded stream collector"
        };
    }

    private static void CollectJobProcesses(
        IntPtr completionPort, CollectorState state, ManualResetEventSlim ready)
    {
        try
        {
            ready.Set();
            while (!state.Stop)
            {
                uint message;
                IntPtr key;
                IntPtr overlapped;
                bool received = GetQueuedCompletionStatus(
                    completionPort, out message, out key, out overlapped, 0);
                if (!received)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_TIMEOUT)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                    lock (state.Gate)
                    {
                        if (state.MetadataFailure == null)
                            state.MetadataFailure = "completion collector error " + error;
                    }
                    return;
                }

                if (key == new IntPtr(-1)) return;

                lock (state.Gate)
                {
                    state.LastMessageMilliseconds = state.Clock.ElapsedMilliseconds;
                    if (message == JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO)
                        state.ActiveProcessZeroSeen = true;
                }
                if (message == JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO) continue;

                int processId = unchecked((int)overlapped.ToInt64());
                if (processId <= 0) continue;
                bool trackProcess = true;
                lock (state.Gate)
                {
                    if (!state.ProcessIds.Contains(processId))
                    {
                        if (state.ProcessIds.Count >= MaxTrackedProcesses)
                        {
                            if (state.MetadataFailure == null)
                                state.MetadataFailure =
                                    "cumulative process metadata limit exceeded";
                            trackProcess = false;
                        }
                        else state.ProcessIds.Add(processId);
                    }
                }
                if (!trackProcess) continue;
                if (message == JOB_OBJECT_MSG_NEW_PROCESS && processId != state.RootProcessId)
                    CaptureMetadata(processId, state);
                else if (message == JOB_OBJECT_MSG_EXIT_PROCESS && processId != state.RootProcessId)
                {
                    lock (state.Gate)
                    {
                        if (!state.Records.ContainsKey(processId) && state.MetadataFailure == null)
                            state.MetadataFailure =
                                "exit arrived without retained creation metadata for PID " + processId;
                    }
                }
            }
        }
        finally
        {
            CloseRecordHandles(state);
            lock (state.Gate)
            {
                CloseHandle(completionPort);
                state.ResourcesReleased = true;
            }
            ready.Dispose();
        }
    }

    private static void SignalCollectorStop(IntPtr completionPort, CollectorState state)
    {
        lock (state.Gate)
        {
            state.Stop = true;
            if (!state.ResourcesReleased)
                PostQueuedCompletionStatus(completionPort, 0, new IntPtr(-1), IntPtr.Zero);
        }
    }

    private static bool CollectorQueueClosed(CollectorState state, uint totalProcesses)
    {
        lock (state.Gate)
        {
            return state.ActiveProcessZeroSeen && state.LastMessageMilliseconds >= 0 &&
                state.Clock.ElapsedMilliseconds - state.LastMessageMilliseconds >= 10 &&
                state.ProcessIds.Count == totalProcesses &&
                state.Records.Count + 1 == totalProcesses;
        }
    }

    private static void CaptureMetadata(int processId, CollectorState state)
    {
        lock (state.Gate)
        {
            if (state.Records.ContainsKey(processId)) return;
        }

        IntPtr handle = OpenProcess(
            PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, unchecked((uint)processId));
        if (handle == IntPtr.Zero)
        {
            SetMetadataFailure(state, "cannot retain process handle for PID " + processId);
            return;
        }

        try
        {
            var path = new StringBuilder(32768);
            uint size = unchecked((uint)path.Capacity);
            if (!QueryFullProcessImageNameW(handle, 0, path, ref size))
            {
                SetMetadataFailure(state, "cannot query executable path for PID " + processId);
                return;
            }

            PROCESS_BASIC_INFORMATION basic;
            int returned;
            int status = NtQueryInformationProcess(
                handle, 0, out basic, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out returned);
            if (status != 0)
            {
                SetMetadataFailure(state, "cannot query parent process for PID " + processId);
                return;
            }

            var record = new NativeRecord
            {
                ProcessId = processId,
                ParentProcessId = unchecked((int)basic.InheritedFromUniqueProcessId.ToInt64()),
                ExecutablePath = path.ToString(),
                ImageName = Path.GetFileName(path.ToString()),
                Handle = handle
            };
            lock (state.Gate)
            {
                if (state.Records.ContainsKey(processId))
                {
                    CloseHandle(handle);
                    handle = IntPtr.Zero;
                }
                else
                {
                    if ((long)state.ExecutablePathCharacters +
                        record.ExecutablePath.Length > MaxExecutablePathCharacters)
                    {
                        if (state.MetadataFailure == null)
                            state.MetadataFailure =
                                "cumulative executable path limit exceeded";
                    }
                    else
                    {
                        state.Records.Add(processId, record);
                        state.ExecutablePathCharacters += record.ExecutablePath.Length;
                        handle = IntPtr.Zero;
                    }
                }
            }
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    private static DescendantRecord[] BuildDescendants(
        int rootProcessId, NativeRecord[] nativeRecords)
    {
        var byId = nativeRecords.ToDictionary(value => value.ProcessId);
        var descendants = new List<DescendantRecord>();
        int ancestorEdges = 0;
        foreach (NativeRecord record in nativeRecords.OrderBy(value => value.ProcessId))
        {
            var ancestors = new List<int>();
            var seen = new HashSet<int>();
            int cursor = record.ParentProcessId;
            while (cursor > 0 && seen.Add(cursor))
            {
                if (ancestors.Count >= MaxProcessDepth)
                    throw new InvalidOperationException(
                        "P5A descendant metadata exceeded its maximum process depth.");
                ancestors.Add(cursor);
                ancestorEdges = checked(ancestorEdges + 1);
                if (ancestorEdges > MaxAncestorEdges)
                    throw new InvalidOperationException(
                        "P5A descendant metadata exceeded its cumulative ancestor edge limit.");
                if (cursor == rootProcessId) break;
                NativeRecord parent;
                if (!byId.TryGetValue(cursor, out parent))
                    throw new InvalidOperationException(
                        "P5A descendant metadata parent is outside the captured Job closure.");
                cursor = parent.ParentProcessId;
            }
            if (ancestors.Count == 0 || ancestors[ancestors.Count - 1] != rootProcessId)
                throw new InvalidOperationException(
                    "P5A descendant metadata ancestry does not reach the direct process.");
            ancestors.Reverse();
            descendants.Add(new DescendantRecord
            {
                processId = record.ProcessId,
                parentProcessId = record.ParentProcessId,
                ancestorProcessIds = ancestors.ToArray(),
                imageName = record.ImageName,
                executablePath = record.ExecutablePath
            });
        }
        return descendants.ToArray();
    }

    private static JOBOBJECT_BASIC_ACCOUNTING_INFORMATION QueryAccounting(IntPtr job)
    {
        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information;
        if (!QueryInformationJobObject(
                job, 1, out information,
                unchecked((uint)Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>()),
                IntPtr.Zero))
            ThrowLastError("QueryInformationJobObject");
        return information;
    }

    private static bool CaptureExceeded(CaptureState state)
    {
        lock (state.Gate) return state.Exceeded;
    }

    private static bool CaptureComplete(CaptureState state)
    {
        lock (state.Gate) return state.Complete;
    }

    private static string[] CaptureLines(CaptureState state)
    {
        lock (state.Gate)
        {
            if (state.Exceeded) return Array.Empty<string>();
            return Encoding.UTF8.GetString(state.Bytes.ToArray()).Split(
                new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static string BuildCommandLine(string executable, string[] arguments)
    {
        var values = new List<string> { executable };
        if (arguments != null) values.AddRange(arguments);
        return String.Join(" ", values.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        if (value == null) value = String.Empty;
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return value;
        var quoted = new StringBuilder();
        quoted.Append('"');
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
                quoted.Append('\\', slashes * 2 + 1);
                quoted.Append('"');
                slashes = 0;
                continue;
            }
            quoted.Append('\\', slashes);
            slashes = 0;
            quoted.Append(character);
        }
        quoted.Append('\\', slashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private static void SetMetadataFailure(CollectorState state, string message)
    {
        lock (state.Gate)
        {
            if (state.MetadataFailure == null) state.MetadataFailure = message;
        }
    }

    private static void CloseRecordHandles(CollectorState state)
    {
        if (state == null) return;
        lock (state.Gate)
        {
            foreach (NativeRecord record in state.Records.Values)
            {
                if (record.Handle != IntPtr.Zero) CloseHandle(record.Handle);
                record.Handle = IntPtr.Zero;
            }
        }
    }

    private static void CloseOwnedHandle(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
        CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    private static void ThrowLastError(string operation)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
}
'@
        $nativeType = 'P5aGeneratorPreboundNativeProcess' -as [type]
    }

    $nativeResult = $nativeType::Run(
        $FilePath, $Arguments, $WorkingDirectory, $TimeoutSeconds)
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
        OutputLines = @($nativeResult.OutputLines)
        DescendantProcesses = @($nativeResult.DescendantProcesses)
        JobTotalProcesses = [int]$nativeResult.JobTotalProcesses
        JobActiveProcesses = [int]$nativeResult.JobActiveProcesses
        JobProcessIds = @($nativeResult.JobProcessIds)
    }
}

function Invoke-P5aGeneratorChild
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

function Get-P5aGeneratorFullPath
{
    param([Parameter(Mandatory)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\.\', [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'P5A generator device paths are forbidden.'
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
            throw 'P5A generator extended path namespace is forbidden.'
        }
        $full = $extendedPath
    }
    $full = [IO.Path]::GetFullPath($full)
    $volumeRoot = [IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($volumeRoot))
    {
        throw 'P5A generator path has no volume root.'
    }
    if ($full -ieq $volumeRoot) { return $full }
    return $full.TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Get-P5aGeneratorFileHash
{
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf))
    {
        throw "Required P5A evidence file is missing: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-P5aGeneratorNoReparsePoint
{
    param([Parameter(Mandatory)][string]$Path)

    $current = Get-P5aGeneratorFullPath $Path
    while (-not [string]::IsNullOrEmpty($current))
    {
        if (Test-Path -LiteralPath $current)
        {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "P5A evidence contains a reparse point: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($parent) -or $parent -ceq $current) { break }
        $current = $parent
    }
}

function Open-P5aGeneratorHelperLease
{
    param([Parameter(Mandatory)][string]$UnrealEditorCmd)

    $handles = [Collections.Generic.List[object]]::new()
    $entries = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    try
    {
        $engineBinaryRoot = Split-Path -Parent (Get-P5aGeneratorFullPath $UnrealEditorCmd)
        $candidates = [Collections.Generic.List[object]]::new()
        $candidates.Add([pscustomobject]@{
            Path = Join-Path ([Environment]::SystemDirectory) 'conhost.exe'
            Role = 'SystemConhost'; BundledPath = $null
        })
        foreach ($image in @('UnrealTraceServer.exe', 'zen.exe', 'zenserver.exe', 'crashpad_handler.exe'))
        {
            $bundled = Join-Path $engineBinaryRoot $image
            if (-not (Test-Path -LiteralPath $bundled -PathType Leaf)) { continue }
            $candidates.Add([pscustomobject]@{
                Path = $bundled; Role = $image; BundledPath = $null
            })
            if ($image -ceq 'UnrealTraceServer.exe')
            {
                $traceRoot = Join-Path $env:LOCALAPPDATA 'UnrealEngine\Common\UnrealTrace\Bin'
                if (Test-Path -LiteralPath $traceRoot -PathType Container)
                {
                    Assert-P5aGeneratorNoReparsePoint $traceRoot
                    foreach ($installed in @(Get-ChildItem -LiteralPath $traceRoot `
                        -Filter $image -File -Recurse))
                    {
                        $candidates.Add([pscustomobject]@{
                            Path = $installed.FullName; Role = $image; BundledPath = $bundled
                        })
                    }
                }
            }
            else
            {
                $installed = Join-Path $env:LOCALAPPDATA "UnrealEngine\Common\Zen\Install\$image"
                if (Test-Path -LiteralPath $installed -PathType Leaf)
                {
                    $candidates.Add([pscustomobject]@{
                        Path = $installed; Role = $image; BundledPath = $bundled
                    })
                }
            }
        }
        foreach ($candidate in $candidates)
        {
            $path = Get-P5aGeneratorFullPath $candidate.Path
            Assert-P5aGeneratorNoReparsePoint $path
            $handle = Open-P5aGeneratorPinnedFile $path
            $handles.Add($handle)
            $hash = $handle.HashSha256()
            if ($null -ne $candidate.BundledPath -and
                $hash -cne [string]$entries[[string]$candidate.BundledPath].Sha256)
            {
                $handle.Dispose()
                $null = $handles.Remove($handle)
                continue
            }
            $entries.Add($path, [pscustomobject]@{
                Role = [string]$candidate.Role; Handle = $handle; Sha256 = $hash
                Installed = $null -ne $candidate.BundledPath
            })
        }
        $lease = [pscustomobject]@{ Handles = @($handles); EntriesByPath = $entries }
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

function Assert-P5aGeneratorHelperLease
{
    param([Parameter(Mandatory)][object]$Lease)

    foreach ($entry in $Lease.EntriesByPath.Values)
    {
        if (-not $entry.Handle.MatchesPath() -or
            $entry.Handle.HashSha256() -cne [string]$entry.Sha256)
        {
            throw 'P5A generator helper executable identity or bytes changed.'
        }
        Assert-P5aGeneratorNoReparsePoint ([string]$entry.Handle.Name)
    }
}

function Test-P5aGeneratorPathWithin
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

function Assert-P5aGeneratorInvocationContract
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$UnrealEditorCmd,
        [Parameter(Mandatory)][string]$UnrealProject,
        [Parameter(Mandatory)][string]$ReferenceRoot,
        [Parameter(Mandatory)][string]$StagingRoot,
        [string]$DestinationPath,
        [string]$OracleAppHost,
        [switch]$StagingAlreadyOwned,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    if ($TimeoutSeconds -le 0) { throw 'P5A timeout must be positive.' }
    foreach ($entry in @(
        @{ Value = $RepositoryRoot; Name = 'Repository root' },
        @{ Value = $UnrealEditorCmd; Name = 'Unreal Editor command' },
        @{ Value = $UnrealProject; Name = 'Unreal project' },
        @{ Value = $ReferenceRoot; Name = 'Reference root' },
        @{ Value = $StagingRoot; Name = 'Staging root' }))
    {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value) -or
            -not [IO.Path]::IsPathFullyQualified([string]$entry.Value))
        {
            throw "$($entry.Name) must be absolute."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($DestinationPath) -and
        -not [IO.Path]::IsPathFullyQualified($DestinationPath))
    {
        throw 'P5A destination path must be absolute.'
    }
    if (-not [string]::IsNullOrWhiteSpace($OracleAppHost) -and
        -not [IO.Path]::IsPathFullyQualified($OracleAppHost))
    {
        throw 'P5A Oracle apphost path must be absolute.'
    }

    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $editor = Get-P5aGeneratorFullPath $UnrealEditorCmd
    $project = Get-P5aGeneratorFullPath $UnrealProject
    $reference = Get-P5aGeneratorFullPath $ReferenceRoot
    $staging = Get-P5aGeneratorFullPath $StagingRoot
    if (-not (Test-Path -LiteralPath $root -PathType Container))
    {
        throw 'Repository root external prerequisite must be an existing directory.'
    }
    if (-not (Test-Path -LiteralPath $editor -PathType Leaf))
    {
        throw 'Unreal Editor external prerequisite must be an existing apphost file.'
    }
    if (-not (Test-Path -LiteralPath $project -PathType Leaf))
    {
        throw 'Unreal project external prerequisite must be an existing file.'
    }
    if (-not (Test-Path -LiteralPath $reference -PathType Container))
    {
        throw 'Reference root external prerequisite must be an existing directory.'
    }
    if (-not [string]::IsNullOrWhiteSpace($OracleAppHost) -and
        -not (Test-Path -LiteralPath $OracleAppHost -PathType Leaf))
    {
        throw 'P5A Oracle apphost external prerequisite must be an existing file.'
    }

    $volumeRoot = Get-P5aGeneratorFullPath ([IO.Path]::GetPathRoot($staging))
    if ($staging -ieq $volumeRoot) { throw 'P5A staging directory cannot be a volume root.' }
    Assert-P5aGeneratorNoReparsePoint $staging
    if ((Test-P5aGeneratorPathWithin -Path $staging -Root $root) -or
        (Test-P5aGeneratorPathWithin -Path $root -Root $staging))
    {
        throw 'P5A staging directory and repository must not contain one another.'
    }
    if (Test-Path -LiteralPath $staging)
    {
        if (-not $StagingAlreadyOwned)
        {
            throw 'P5A staging directory must not already exist.'
        }
        if (-not (Test-Path -LiteralPath $staging -PathType Container) -or
            @(Get-ChildItem -LiteralPath $staging -Force).Count -ne 0)
        {
            throw 'Owned P5A staging directory must remain empty before child execution.'
        }
    }
    elseif ($StagingAlreadyOwned)
    {
        throw 'Owned P5A staging directory identity is missing.'
    }
    if (-not [string]::IsNullOrWhiteSpace($DestinationPath))
    {
        $destination = Get-P5aGeneratorFullPath $DestinationPath
        if (Test-P5aGeneratorPathWithin -Path $destination -Root $staging)
        {
            throw 'P5A destination path must not be inside staging.'
        }
        if ((Test-Path -LiteralPath $destination) -and
            -not (Test-Path -LiteralPath $destination -PathType Leaf))
        {
            throw 'P5A destination path must identify a file.'
        }
    }
}

function Initialize-P5aGeneratorFilesystemNative
{
    $nativeType = 'P5aGeneratorStagingNative' -as [type]
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

public static class P5aGeneratorStagingNative
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_SHARE_DELETE = 4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    private const int FILE_DISPOSITION_INFO_CLASS = 4;
    private const int FILE_RENAME_INFO_CLASS = 3;
    private const int FILE_RENAME_INFO_EX_CLASS = 22;
    private const int FILE_RENAME_FLAG_REPLACE_IF_EXISTS = 0x1;
    private const int FILE_RENAME_FLAG_POSIX_SEMANTICS = 0x2;
    private const uint DUPLICATE_SAME_ACCESS = 0x2;
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
        IntPtr handle, System.Text.StringBuilder path, uint pathLength, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(
        IntPtr handle, int informationClass,
        ref FILE_DISPOSITION_INFO information, uint informationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle, int informationClass,
        IntPtr information, uint informationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, SafeFileHandle sourceHandle,
        IntPtr targetProcess, out SafeFileHandle targetHandle,
        uint desiredAccess, bool inheritHandle, uint options);

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
                throw new ObjectDisposedException("P5A generator staging lease");
            var disposition = new FILE_DISPOSITION_INFO { DeleteFile = true };
            if (!SetFileInformationByHandle(
                    handle, FILE_DISPOSITION_INFO_CLASS, ref disposition,
                    (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO))))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot atomically delete the P5A generator staging directory");
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }

    public sealed class PinnedFile : IDisposable
    {
        private readonly object sync = new object();
        private readonly bool allowRename;
        private readonly uint volume;
        private readonly ulong fileId;
        private SafeFileHandle safeHandle;

        public string Name { get; private set; }
        public string FinalPath { get; private set; }
        public SafeFileHandle SafeFileHandle { get { return safeHandle; } }

        internal PinnedFile(
            string path, string finalPath, IntPtr handle, bool allowRename,
            uint volume, ulong fileId)
        {
            Name = path;
            FinalPath = finalPath;
            safeHandle = new SafeFileHandle(handle, true);
            this.allowRename = allowRename;
            this.volume = volume;
            this.fileId = fileId;
        }

        public bool MatchesPath()
        {
            lock (sync)
            {
                if (safeHandle == null || safeHandle.IsClosed || safeHandle.IsInvalid)
                    return false;
                IntPtr candidate = OpenFileProbe(Name);
                if (candidate == INVALID_HANDLE_VALUE) return false;
                try
                {
                    BY_HANDLE_FILE_INFORMATION information;
                    if (!GetFileInformationByHandle(candidate, out information)) return false;
                    ulong candidateId = ((ulong)information.FileIndexHigh << 32) |
                        information.FileIndexLow;
                    return information.VolumeSerialNumber == volume && candidateId == fileId &&
                        (information.FileAttributes &
                            (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) == 0;
                }
                finally { CloseHandle(candidate); }
            }
        }

        public string HashSha256()
        {
            lock (sync)
            {
                EnsureOpen();
                using (var duplicate = DuplicateForRead())
                using (var stream = new FileStream(duplicate, FileAccess.Read, 65536, false))
                using (var hash = SHA256.Create())
                {
                    stream.Position = 0;
                    return BitConverter.ToString(hash.ComputeHash(stream))
                        .Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public void CopyTo(string destinationPath)
        {
            lock (sync)
            {
                EnsureOpen();
                using (var duplicate = DuplicateForRead())
                using (var source = new FileStream(duplicate, FileAccess.Read, 65536, false))
                using (var destination = new FileStream(
                    destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65536, FileOptions.WriteThrough))
                {
                    source.Position = 0;
                    source.CopyTo(destination);
                    destination.Flush(true);
                }
            }
        }

        public void CommitTo(string destinationPath, bool replaceIfExists)
        {
            lock (sync)
            {
                EnsureOpen();
                if (!allowRename)
                    throw new InvalidOperationException(
                        "P5A pinned file was not opened for atomic publication.");
                string destination = Path.GetFullPath(destinationPath);
                byte[] nameBytes = Encoding.Unicode.GetBytes(destination);
                int rootDirectoryOffset = IntPtr.Size == 8 ? 8 : 4;
                int fileNameLengthOffset = rootDirectoryOffset + IntPtr.Size;
                int fileNameOffset = fileNameLengthOffset + 4;
                int bufferSize = fileNameOffset + nameBytes.Length + 2;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    for (int index = 0; index < bufferSize; index++)
                        Marshal.WriteByte(buffer, index, 0);
                    int informationClass = FILE_RENAME_INFO_CLASS;
                    if (replaceIfExists)
                    {
                        informationClass = FILE_RENAME_INFO_EX_CLASS;
                        Marshal.WriteInt32(
                            buffer, 0,
                            FILE_RENAME_FLAG_REPLACE_IF_EXISTS |
                            FILE_RENAME_FLAG_POSIX_SEMANTICS);
                    }
                    Marshal.WriteIntPtr(buffer, rootDirectoryOffset, IntPtr.Zero);
                    Marshal.WriteInt32(buffer, fileNameLengthOffset, nameBytes.Length);
                    Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, fileNameOffset), nameBytes.Length);
                    if (!SetFileInformationByHandle(
                            safeHandle, informationClass, buffer,
                            (uint)bufferSize))
                        throw new Win32Exception(Marshal.GetLastWin32Error(),
                            "Cannot atomically publish the pinned P5A fixture");
                    Name = destination;
                    FinalPath = destination;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (safeHandle == null) return;
                safeHandle.Dispose();
            }
        }

        private void EnsureOpen()
        {
            if (safeHandle == null || safeHandle.IsClosed || safeHandle.IsInvalid)
                throw new ObjectDisposedException("P5A pinned file lease");
        }

        private SafeFileHandle DuplicateForRead()
        {
            EnsureOpen();
            SafeFileHandle duplicate;
            IntPtr process = GetCurrentProcess();
            if (!DuplicateHandle(
                    process, safeHandle, process, out duplicate,
                    0, false, DUPLICATE_SAME_ACCESS))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot duplicate the P5A pinned file handle");
            return duplicate;
        }
    }

    public static Lease CreateOwned(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!CreateDirectoryW(fullPath, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "P5A staging directory must be newly created by this run");
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
                    "Cannot retain the P5A staging directory handle");
            }
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(handle, out information))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot identify the P5A staging directory");
            if ((information.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new InvalidOperationException(
                    "P5A staging path is not a directory.");
            if ((information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                throw new InvalidOperationException(
                    "P5A staging path is a reparse point.");
            string finalPath = Path.GetFullPath(ReadFinalPath(handle));
            if (!fullPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "P5A staging canonical path does not match its requested path.");
            ulong fileId = ((ulong)information.FileIndexHigh << 32) |
                information.FileIndexLow;
            var lease = new Lease(
                fullPath, finalPath, handle,
                information.VolumeSerialNumber, fileId);
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

    public static PinnedFile OpenPinnedFile(string path, bool allowRename)
    {
        uint access = GENERIC_READ | FILE_READ_ATTRIBUTES;
        if (allowRename) access |= DELETE;
        IntPtr handle = CreateFileW(
            path, access, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot retain the P5A pinned file handle");
        try
        {
            BY_HANDLE_FILE_INFORMATION information;
            if (!GetFileInformationByHandle(handle, out information))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Cannot identify the P5A pinned file");
            if ((information.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                throw new InvalidOperationException("P5A pinned path is a directory.");
            if ((information.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                throw new InvalidOperationException("P5A pinned path is a reparse point.");
            string fullPath = Path.GetFullPath(path);
            string finalPath = Path.GetFullPath(ReadFinalPath(handle));
            if (!fullPath.Equals(finalPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "P5A pinned file canonical path does not match its requested path.");
            ulong fileId = ((ulong)information.FileIndexHigh << 32) |
                information.FileIndexLow;
            var lease = new PinnedFile(
                fullPath, finalPath, handle, allowRename,
                information.VolumeSerialNumber, fileId);
            handle = IntPtr.Zero;
            return lease;
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

    private static IntPtr OpenFileProbe(string path)
    {
        return CreateFileW(
            path, FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
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
        var path = new System.Text.StringBuilder(32768);
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
        $nativeType = 'P5aGeneratorStagingNative' -as [type]
    }
    return $nativeType
}

function Open-P5aGeneratorOwnedStaging
{
    param([Parameter(Mandatory)][string]$Path)

    $nativeType = Initialize-P5aGeneratorFilesystemNative
    return $nativeType::CreateOwned((Get-P5aGeneratorFullPath $Path))
}

function Open-P5aGeneratorPinnedFile
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowAtomicRename
    )

    $nativeType = Initialize-P5aGeneratorFilesystemNative
    return $nativeType::OpenPinnedFile(
        (Get-P5aGeneratorFullPath $Path), [bool]$AllowAtomicRename)
}

function Assert-P5aGeneratorOwnedStaging
{
    param([Parameter(Mandatory)][object]$Lease)

    if (-not $Lease.MatchesPath())
    {
        throw 'P5A generator staging directory identity changed.'
    }
    Assert-P5aGeneratorNoReparsePoint ([string]$Lease.Path)
}

function Remove-P5aGeneratorPathStrict
{
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return }
    if ($item.PSIsContainer)
    {
        throw "P5A generator staging contains an unexpected child directory: $($item.FullName)"
    }
    [IO.File]::Delete($item.FullName)
    if (Test-Path -LiteralPath $item.FullName)
    {
        throw "P5A generator staging file remained after strict cleanup: $($item.FullName)"
    }
}

function Close-P5aGeneratorOwnedStaging
{
    param([Parameter(Mandatory)][object]$Lease)

    $path = Get-P5aGeneratorFullPath ([string]$Lease.Path)
    try
    {
        Assert-P5aGeneratorOwnedStaging $Lease
        foreach ($child in @(Get-ChildItem -LiteralPath $path -Force))
        {
            Remove-P5aGeneratorPathStrict $child.FullName
        }
        Assert-P5aGeneratorOwnedStaging $Lease
        $Lease.DeleteIfEmpty()
    }
    finally { $Lease.Dispose() }
    if (Test-Path -LiteralPath $path)
    {
        throw 'P5A generator staging directory remained after strict cleanup.'
    }
}

function Assert-P5aGeneratorPhysicalStagingBoundary
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][object]$StagingLease
    )

    Assert-P5aGeneratorOwnedStaging $StagingLease
    $nativeType = 'P5aGeneratorStagingNative' -as [type]
    $physicalRoot = Get-P5aGeneratorFullPath (
        $nativeType::FinalDirectoryPath((Get-P5aGeneratorFullPath $RepositoryRoot)))
    $physicalStaging = Get-P5aGeneratorFullPath ([string]$StagingLease.FinalPath)
    if ((Test-P5aGeneratorPathWithin -Path $physicalStaging -Root $physicalRoot) -or
        (Test-P5aGeneratorPathWithin -Path $physicalRoot -Root $physicalStaging))
    {
        throw 'P5A generator staging physical identity overlaps the repository.'
    }
}

function Get-P5aGeneratorTreeHash
{
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$RelativePaths
    )

    $hash = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try
    {
        foreach ($relativePath in $RelativePaths)
        {
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($relativePath))
            $hash.AppendData([byte[]]@(0))
            $fileHash = Get-P5aGeneratorFileHash (Join-Path $Root $relativePath)
            $hash.AppendData([Text.Encoding]::ASCII.GetBytes($fileHash))
            $hash.AppendData([byte[]]@(10))
        }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    }
    finally
    {
        $hash.Dispose()
    }
}

function Get-P5aOracleSourceClosure
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $fixed = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in @(
        '.editorconfig', 'global.json', 'Directory.Build.props',
        'tools/Als.P5aOracle/Als.P5aOracle.csproj', 'tools/Als.P5aOracle/Program.cs',
        'src/Als.Import/Als.Import.csproj', 'src/Als.Core/Als.Core.csproj'))
    {
        $path = Join-Path $root $relativePath
        Assert-P5aGeneratorNoReparsePoint $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Oracle source closure is missing '$relativePath'."
        }
        $fixed.Add($relativePath)
    }

    foreach ($family in @('src/Als.Import', 'src/Als.Core'))
    {
        $familyRoot = Join-Path $root $family
        Assert-P5aGeneratorNoReparsePoint $familyRoot
        $files = @(Get-ChildItem -LiteralPath $familyRoot -Filter '*.cs' -File -Recurse |
            Where-Object {
                $relative = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
                -not @($relative.Split('/') | Where-Object {
                    $_.Equals('bin', [StringComparison]::OrdinalIgnoreCase) -or
                    $_.Equals('obj', [StringComparison]::OrdinalIgnoreCase)
                }).Count
            })
        if ($files.Count -eq 0)
        {
            throw "Oracle source closure family '$family' is empty."
        }
        foreach ($file in $files)
        {
            Assert-P5aGeneratorNoReparsePoint $file.FullName
            $fixed.Add([IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/'))
        }
    }

    $relativePaths = [string[]]@($fixed)
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    if (@($relativePaths | Select-Object -Unique).Count -ne $relativePaths.Count)
    {
        throw 'Oracle source closure contains a duplicate entry.'
    }
    return [pscustomobject]@{
        RelativePaths = $relativePaths
        Paths = @($relativePaths | ForEach-Object { Join-Path $root $_ })
        TreeSha256 = Get-P5aGeneratorTreeHash -Root $root -RelativePaths $relativePaths
    }
}

function Assert-P5aOracleProjectContract
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $projectPath = Join-Path $root 'tools\Als.P5aOracle\Als.P5aOracle.csproj'
    try { [xml]$project = [IO.File]::ReadAllText($projectPath) }
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
        'PackageReference', 'Reference', 'COMReference', 'NativeReference'))
    {
        if (@($project.SelectNodes("//*[local-name()='$forbiddenName']")).Count -ne 0)
        {
            throw "Oracle project contains forbidden $forbiddenName."
        }
    }
    if (@($project.SelectNodes("//*[local-name()='CopyToOutputDirectory']")).Count -ne 0)
    {
        throw 'Oracle project contains forbidden content or runtime copying.'
    }

    $targets = @($project.Project.Target)
    if ($targets.Count -ne 1 -or
        [string]$targets[0].Name -cne 'WriteP5aOracleBuildManifest' -or
        [string]$targets[0].AfterTargets -cne 'Build' -or
        [string]$targets[0].Condition -cne "'`$(Configuration)' == 'Release'")
    {
        throw 'Oracle project requires one exact Release AfterBuild manifest target.'
    }
    $execs = @($targets[0].Exec)
    $privateMode = '--write-' + 'build-manifest'
    $expectedCommand = '"$(TargetDir)Als.P5aOracle.exe" ' + $privateMode +
        ' --repository-root "$(P5aRepositoryRoot)" --output "$(TargetDir)p5a-oracle-build.manifest" --sdk-version "$(NETCoreSdkVersion)"'
    if ($execs.Count -ne 1 -or [string]$execs[0].Command -cne $expectedCommand)
    {
        throw 'Oracle project private build-manifest invocation is not exact.'
    }

    $depsPath = Join-Path $root 'tools\Als.P5aOracle\bin\Release\net8.0\Als.P5aOracle.deps.json'
    if (Test-Path -LiteralPath $depsPath -PathType Leaf)
    {
        try { $deps = [IO.File]::ReadAllText($depsPath) | ConvertFrom-Json -Depth 100 }
        catch { throw "Oracle deps document is invalid: $($_.Exception.Message)" }
        $libraries = @($deps.libraries.PSObject.Properties)
        if ($libraries.Count -ne 3)
        {
            throw 'Oracle deps contains a forbidden local library.'
        }
        foreach ($name in @('Als.P5aOracle', 'Als.Import', 'Als.Core'))
        {
            $matches = @($libraries | Where-Object { $_.Name -match ('^' + [regex]::Escape($name) + '/[^/]+$') })
            if ($matches.Count -ne 1 -or [string]$matches[0].Value.type -cne 'project')
            {
                throw "Oracle deps must contain project library '$name'."
            }
        }
        $targetsByFramework = @($deps.targets.PSObject.Properties)
        if ($targetsByFramework.Count -ne 1 -or
            [string]$targetsByFramework[0].Name -cne '.NETCoreApp,Version=v8.0')
        {
            throw 'Oracle deps target framework must be net8.0.'
        }
        $targetProjects = @($targetsByFramework[0].Value.PSObject.Properties)
        if ($targetProjects.Count -ne 3)
        {
            throw 'Oracle deps runtime target closure is not exact.'
        }
        $runtimeDlls = @($targetProjects | ForEach-Object {
            @($_.Value.runtime.PSObject.Properties | ForEach-Object { $_.Name })
        })
        $expectedRuntimeDlls = @('Als.Core.dll', 'Als.Import.dll', 'Als.P5aOracle.dll')
        if (@(Compare-Object $expectedRuntimeDlls @($runtimeDlls | Sort-Object) `
                -CaseSensitive).Count -ne 0)
        {
            throw 'Oracle deps runtime DLL closure is not exact.'
        }
    }
}

function Read-P5aOracleEvidence
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    Assert-P5aOracleProjectContract -RepositoryRoot $root
    $closure = Get-P5aOracleSourceClosure -RepositoryRoot $root
    $outputRoot = Join-Path $root 'tools\Als.P5aOracle\bin\Release\net8.0'
    $manifestPath = Join-Path $outputRoot 'p5a-oracle-build.manifest'
    $artifactNames = @(
        'Als.P5aOracle.exe', 'Als.P5aOracle.dll', 'Als.Import.dll', 'Als.Core.dll',
        'Als.P5aOracle.deps.json', 'Als.P5aOracle.runtimeconfig.json')
    $artifactPaths = @($artifactNames | ForEach-Object { Join-Path $outputRoot $_ })
    foreach ($path in @($manifestPath) + $artifactPaths)
    {
        Assert-P5aGeneratorNoReparsePoint $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf))
        {
            throw "Fixed Release Oracle apphost evidence is missing: $path"
        }
    }

    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    if ($manifestBytes.Length -eq 0 -or
        ($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xef -and
         $manifestBytes[1] -eq 0xbb -and $manifestBytes[2] -eq 0xbf))
    {
        throw 'Oracle build manifest must be UTF-8 without BOM.'
    }
    $manifestText = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
    if ($manifestText.Contains("`r", [StringComparison]::Ordinal) -or
        -not $manifestText.EndsWith("`n", [StringComparison]::Ordinal) -or
        $manifestText.EndsWith("`n`n", [StringComparison]::Ordinal))
    {
        throw 'Oracle build manifest must use LF with exactly one final LF.'
    }
    $lines = $manifestText.Substring(0, $manifestText.Length - 1).Split("`n")
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
        [string]::IsNullOrEmpty($values.sdkVersion))
    {
        throw 'Oracle build manifest configuration, framework, or SDK is invalid.'
    }
    foreach ($name in @($names | Select-Object -Skip 3))
    {
        if ([string]$values[$name] -cnotmatch '^[0-9a-f]{64}$')
        {
            throw "Oracle build manifest hash '$name' is malformed."
        }
    }

    $actualHashes = [ordered]@{
        sourceTreeSha256 = $closure.TreeSha256
        executableSha256 = Get-P5aGeneratorFileHash $artifactPaths[0]
        oracleAssemblySha256 = Get-P5aGeneratorFileHash $artifactPaths[1]
        importAssemblySha256 = Get-P5aGeneratorFileHash $artifactPaths[2]
        coreAssemblySha256 = Get-P5aGeneratorFileHash $artifactPaths[3]
        depsSha256 = Get-P5aGeneratorFileHash $artifactPaths[4]
        runtimeConfigSha256 = Get-P5aGeneratorFileHash $artifactPaths[5]
    }
    foreach ($entry in $actualHashes.GetEnumerator())
    {
        if ([string]$values[$entry.Key] -cne [string]$entry.Value)
        {
            throw "Oracle evidence hash mismatch for '$($entry.Key)'."
        }
    }
    $newestSource = @($closure.Paths | ForEach-Object {
        [IO.File]::GetLastWriteTimeUtc($_)
    } | Sort-Object -Descending)[0]
    foreach ($path in $artifactPaths[0..3])
    {
        if ([IO.File]::GetLastWriteTimeUtc($path) -lt $newestSource)
        {
            throw "Oracle runtime artifact is stale relative to its source closure: $path"
        }
    }

    return [pscustomobject]@{
        Evidence = [pscustomobject][ordered]@{
            sdkVersion = [string]$values.sdkVersion
            sourceTreeSha256 = [string]$actualHashes.sourceTreeSha256
            buildManifestSha256 = Get-P5aGeneratorFileHash $manifestPath
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

function Open-P5aOracleEvidenceLease
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $snapshot = Read-P5aOracleEvidence -RepositoryRoot $RepositoryRoot
    $handles = [Collections.Generic.List[object]]::new()
    try
    {
        foreach ($path in @($snapshot.SourcePaths) + @($snapshot.ManifestPath) +
                         @($snapshot.ArtifactPaths))
        {
            $handles.Add((Open-P5aGeneratorPinnedFile $path))
        }
        $confirmed = Read-P5aOracleEvidence -RepositoryRoot $RepositoryRoot
        if (($confirmed.Evidence | ConvertTo-Json -Compress) -cne
                ($snapshot.Evidence | ConvertTo-Json -Compress) -or
            @(Compare-Object -CaseSensitive -ReferenceObject @($snapshot.SourcePaths) `
                -DifferenceObject @($confirmed.SourcePaths)).Count -ne 0 -or
            [string]$confirmed.ManifestPath -cne [string]$snapshot.ManifestPath -or
            @(Compare-Object -CaseSensitive -ReferenceObject @($snapshot.ArtifactPaths) `
                -DifferenceObject @($confirmed.ArtifactPaths)).Count -ne 0)
        {
            throw 'P5A Oracle evidence changed while its pinned leases were opening.'
        }
        $snapshot = $confirmed
        $lease = [pscustomobject]@{
            Evidence = $snapshot.Evidence
            AppHostPath = $snapshot.AppHostPath
            SourcePaths = @($snapshot.SourcePaths)
            ManifestPath = $snapshot.ManifestPath
            ArtifactPaths = @($snapshot.ArtifactPaths)
            Handles = @($handles)
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

function Assert-P5aOracleEvidenceUnchanged
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][object]$ExpectedEvidence
    )

    $actual = Read-P5aOracleEvidence -RepositoryRoot $RepositoryRoot
    if (($actual.Evidence | ConvertTo-Json -Compress) -cne
        ($ExpectedEvidence | ConvertTo-Json -Compress))
    {
        throw 'Oracle evidence changed while the P5A workflow was running.'
    }
}

function Get-P5aDeployedBuildEvidence
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$UnrealProject
    )

    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $uproject = Get-P5aGeneratorFullPath $UnrealProject
    if (-not (Test-Path -LiteralPath $uproject -PathType Leaf))
    {
        throw "Unreal project is missing: $uproject"
    }
    $projectRoot = Split-Path -Parent $uproject
    $ownedRoot = Join-Path $root 'tools\unreal\AlsLocomotionTrace'
    $deployedRoot = Join-Path $projectRoot 'Plugins\AlsLocomotionTrace'
    $closures = @()
    foreach ($treeRoot in @($ownedRoot, $deployedRoot))
    {
        Assert-P5aGeneratorNoReparsePoint $treeRoot
        if (-not (Test-Path -LiteralPath $treeRoot -PathType Container))
        {
            throw "P5A owned plugin tree is missing: $treeRoot"
        }
        $relativePaths = [string[]]@(Get-ChildItem -LiteralPath $treeRoot -File -Recurse |
            Where-Object {
                $relative = [IO.Path]::GetRelativePath($treeRoot, $_.FullName).Replace('\', '/')
                -not @($relative.Split('/') | Where-Object {
                    $_.Equals('Binaries', [StringComparison]::OrdinalIgnoreCase) -or
                    $_.Equals('Intermediate', [StringComparison]::OrdinalIgnoreCase)
                }).Count
            } | ForEach-Object {
                Assert-P5aGeneratorNoReparsePoint $_.FullName
                [IO.Path]::GetRelativePath($treeRoot, $_.FullName).Replace('\', '/')
            })
        [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
        if ($relativePaths.Count -eq 0) { throw 'P5A owned plugin tree is empty.' }
        $closures += [pscustomobject]@{
            Root = $treeRoot
            RelativePaths = $relativePaths
            Paths = @($relativePaths | ForEach-Object { Join-Path $treeRoot $_ })
            TreeSha256 = Get-P5aGeneratorTreeHash -Root $treeRoot -RelativePaths $relativePaths
        }
    }
    if (@(Compare-Object @($closures[0].RelativePaths) @($closures[1].RelativePaths) `
            -CaseSensitive).Count -ne 0 -or
        $closures[0].TreeSha256 -cne $closures[1].TreeSha256)
    {
        throw 'P5A owned and deployed plugin source trees are not byte-identical.'
    }

    $targets = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Source') `
        -Filter '*Editor.Target.cs' -File -Recurse -ErrorAction SilentlyContinue)
    if ($targets.Count -ne 1) { throw 'P5A Editor target receipt selection is ambiguous.' }
    $targetName = $targets[0].Name.Substring(0, $targets[0].Name.Length - '.Target.cs'.Length)
    $receiptPath = Join-Path $projectRoot "Binaries\Win64\$targetName.target"
    try { $receipt = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json -Depth 30 }
    catch { throw "P5A Editor target receipt is missing or malformed: $receiptPath" }
    if ([int]$receipt.Version.MajorVersion -ne 5 -or
        [int]$receipt.Version.MinorVersion -ne 9 -or
        [int]$receipt.Version.PatchVersion -ne 0 -or
        [string]::IsNullOrEmpty([string]$receipt.Version.BuildId))
    {
        throw 'P5A Editor receipt must identify engine 5.9.0 and a BuildId.'
    }
    $buildId = [string]$receipt.Version.BuildId
    $alsManifestPath = Join-Path $projectRoot 'Plugins\ALS\Binaries\Win64\UnrealEditor.modules'
    $traceManifestPath = Join-Path $deployedRoot 'Binaries\Win64\UnrealEditor.modules'
    $manifestRows = @(
        @{
            Path = $alsManifestPath
            Modules = [ordered]@{
                ALS = 'UnrealEditor-ALS.dll'
                ALSCamera = 'UnrealEditor-ALSCamera.dll'
                ALSEditor = 'UnrealEditor-ALSEditor.dll'
                ALSExtras = 'UnrealEditor-ALSExtras.dll'
            }
        },
        @{
            Path = $traceManifestPath
            Modules = [ordered]@{
                AlsLocomotionTrace = 'UnrealEditor-AlsLocomotionTrace.dll'
            }
        })
    $moduleDllPaths = [ordered]@{}
    foreach ($row in $manifestRows)
    {
        try { $manifest = [IO.File]::ReadAllText($row.Path) | ConvertFrom-Json -Depth 30 }
        catch { throw "P5A module manifest is missing or malformed: $($row.Path)" }
        $modules = @($manifest.Modules.PSObject.Properties)
        $expectedNames = @($row.Modules.Keys)
        $actualNames = @($modules | ForEach-Object { $_.Name })
        if ([string]$manifest.BuildId -cne $buildId -or
            @(Compare-Object -CaseSensitive -ReferenceObject $expectedNames `
                -DifferenceObject $actualNames).Count -ne 0)
        {
            throw "P5A module manifest BuildId or module set is invalid: $($row.Path)"
        }
        foreach ($moduleName in $expectedNames)
        {
            $match = @($modules | Where-Object { $_.Name -ceq $moduleName })
            $mappedName = if ($match.Count -eq 1) { [string]$match[0].Value } else { '' }
            if ($mappedName -cne [string]$row.Modules[$moduleName] -or
                [IO.Path]::GetFileName($mappedName) -cne $mappedName)
            {
                throw "P5A module manifest mapping is invalid for '$moduleName'."
            }
            $dllPath = Join-Path (Split-Path -Parent $row.Path) $mappedName
            if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf))
            {
                throw "P5A mapped module DLL is missing: $dllPath"
            }
            $moduleDllPaths[$moduleName] = $dllPath
        }
    }
    $newestDeployedSource = @($closures[1].Paths | ForEach-Object {
        [IO.File]::GetLastWriteTimeUtc($_)
    } | Sort-Object -Descending)[0]
    if ([IO.File]::GetLastWriteTimeUtc($moduleDllPaths.AlsLocomotionTrace) -lt $newestDeployedSource)
    {
        throw 'P5A deployed trace module DLL is stale relative to deployed source.'
    }
    $evidencePaths = @(
        $receiptPath, $alsManifestPath, $traceManifestPath,
        $moduleDllPaths.ALS, $moduleDllPaths.ALSCamera, $moduleDllPaths.ALSEditor,
        $moduleDllPaths.ALSExtras, $moduleDllPaths.AlsLocomotionTrace)
    foreach ($path in $evidencePaths) { Assert-P5aGeneratorNoReparsePoint $path }
    return [pscustomobject]@{
        Evidence = [pscustomobject][ordered]@{
            ownedPluginTreeSha256 = $closures[0].TreeSha256
            targetReceiptSha256 = Get-P5aGeneratorFileHash $receiptPath
            alsModuleManifestSha256 = Get-P5aGeneratorFileHash $alsManifestPath
            traceModuleManifestSha256 = Get-P5aGeneratorFileHash $traceManifestPath
            alsModuleDllSha256 = Get-P5aGeneratorFileHash $moduleDllPaths.ALS
            alsCameraModuleDllSha256 = Get-P5aGeneratorFileHash $moduleDllPaths.ALSCamera
            alsEditorModuleDllSha256 = Get-P5aGeneratorFileHash $moduleDllPaths.ALSEditor
            alsExtrasModuleDllSha256 = Get-P5aGeneratorFileHash $moduleDllPaths.ALSExtras
            traceModuleDllSha256 = Get-P5aGeneratorFileHash $moduleDllPaths.AlsLocomotionTrace
        }
        OwnedPaths = @($closures[0].Paths)
        DeployedPaths = @($closures[1].Paths)
        EvidencePaths = @($evidencePaths)
    }
}

function Open-P5aDeployedBuildEvidenceLease
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$UnrealProject
    )

    $snapshot = Get-P5aDeployedBuildEvidence -RepositoryRoot $RepositoryRoot `
        -UnrealProject $UnrealProject
    $handles = [Collections.Generic.List[object]]::new()
    try
    {
        foreach ($path in @($snapshot.OwnedPaths) + @($snapshot.DeployedPaths) +
                         @($snapshot.EvidencePaths))
        {
            $handles.Add((Open-P5aGeneratorPinnedFile $path))
        }
        $confirmed = Get-P5aDeployedBuildEvidence `
            -RepositoryRoot $RepositoryRoot -UnrealProject $UnrealProject
        if (($confirmed.Evidence | ConvertTo-Json -Compress) -cne
                ($snapshot.Evidence | ConvertTo-Json -Compress) -or
            @(Compare-Object -CaseSensitive -ReferenceObject @($snapshot.OwnedPaths) `
                -DifferenceObject @($confirmed.OwnedPaths)).Count -ne 0 -or
            @(Compare-Object -CaseSensitive -ReferenceObject @($snapshot.DeployedPaths) `
                -DifferenceObject @($confirmed.DeployedPaths)).Count -ne 0 -or
            @(Compare-Object -CaseSensitive -ReferenceObject @($snapshot.EvidencePaths) `
                -DifferenceObject @($confirmed.EvidencePaths)).Count -ne 0)
        {
            throw 'P5A deployed build evidence changed while its pinned leases were opening.'
        }
        $snapshot = $confirmed
        $lease = [pscustomobject]@{
            Evidence = $snapshot.Evidence
            OwnedPaths = @($snapshot.OwnedPaths)
            DeployedPaths = @($snapshot.DeployedPaths)
            EvidencePaths = @($snapshot.EvidencePaths)
            Handles = @($handles)
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

function Assert-P5aDeployedBuildEvidenceUnchanged
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$UnrealProject,
        [Parameter(Mandatory)][object]$ExpectedEvidence
    )

    $actual = Get-P5aDeployedBuildEvidence -RepositoryRoot $RepositoryRoot `
        -UnrealProject $UnrealProject
    if (($actual.Evidence | ConvertTo-Json -Compress) -cne
        ($ExpectedEvidence | ConvertTo-Json -Compress))
    {
        throw 'P5A deployed-build evidence changed while the workflow was running.'
    }
}

function Assert-P5aChildGateOutput
{
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$StdOutLines,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$StdErrLines,
        [Parameter(Mandatory)][int]$ExitCode,
        [Parameter(Mandatory)][bool]$TimedOut,
        [bool]$OutputLimitExceeded = $false,
        [Parameter(Mandatory)][string]$ExpectedMarker,
        [switch]$AllowUeWrapper
    )

    if ($TimedOut) { throw "P5A child '$PhaseName' timed out." }
    if ($OutputLimitExceeded) { throw "P5A child '$PhaseName' exceeded its output limit." }
    if ($ExitCode -ne 0) { throw "P5A child '$PhaseName' exited non-zero ($ExitCode)." }
    $markers = [Collections.Generic.List[string]]::new()
    $ordinary = [Collections.Generic.List[object]]::new()
    foreach ($lineObject in $StdErrLines)
    {
        $line = [string]$lineObject
        if ($line.IndexOf('P5A_', [StringComparison]::Ordinal) -ge 0)
        {
            throw "P5A child '$PhaseName' emitted a marker on stderr."
        }
        if ($line -match '(?i)(^|:\s*)(warning|error|fatal)(\s|:|$)')
        {
            throw "P5A child '$PhaseName' emitted a warning, error, or fatal line."
        }
    }
    foreach ($lineObject in $StdOutLines)
    {
        $line = [string]$lineObject
        $payload = $null
        if ($line -ceq $ExpectedMarker)
        {
            $payload = $line
        }
        elseif ($AllowUeWrapper -and
                $line -cmatch '^\[[0-9.:-]+\]\[\s*[0-9]+\]LogTemp: Display: (?<payload>.*)$' -and
                $Matches.payload -ceq $ExpectedMarker)
        {
            $payload = $Matches.payload
        }
        elseif ($line.IndexOf('P5A_', [StringComparison]::Ordinal) -ge 0)
        {
            throw "P5A child '$PhaseName' emitted an invalid marker line."
        }
        if ($null -ne $payload) { $markers.Add($payload); continue }
        if ($line -match '(?i)(^|:\s*)(warning|error|fatal)(\s|:|$)')
        {
            throw "P5A child '$PhaseName' emitted a warning, error, or fatal line."
        }
        $ordinary.Add($lineObject)
    }
    if ($markers.Count -ne 1)
    {
        throw "P5A child '$PhaseName' did not emit exactly one expected marker."
    }
    return @($ordinary)
}

function Assert-P5aGeneratorNoDescendants
{
    param(
        [Parameter(Mandatory)][object]$ProcessResult,
        [object]$HelperLease,
        [switch]$AllowUeHelpers
    )

    $rootProcessId = [int]$ProcessResult.ProcessId
    if ($rootProcessId -le 0 -or
        $null -eq $ProcessResult.PSObject.Properties['DescendantProcesses'] -or
        $null -eq $ProcessResult.DescendantProcesses)
    {
        throw 'P5A generator descendant root or metadata collection is invalid.'
    }
    if ($null -ne $HelperLease)
    {
        Assert-P5aGeneratorHelperLease $HelperLease
    }
    $conhostPath = Get-P5aGeneratorFullPath (Join-Path ([Environment]::SystemDirectory) 'conhost.exe')
    $records = @($ProcessResult.DescendantProcesses)
    $byId = @{}
    $rolesById = @{}
    foreach ($record in $records)
    {
        if ($null -eq $record) { throw 'P5A generator descendant metadata record is null.' }
        foreach ($name in @('processId', 'parentProcessId', 'ancestorProcessIds', 'imageName', 'executablePath'))
        {
            if ($null -eq $record.PSObject.Properties[$name])
            {
                throw "P5A generator descendant metadata '$name' is missing."
            }
        }
        $recordId = [int]$record.processId
        if ($recordId -le 0 -or $recordId -eq $rootProcessId -or $byId.ContainsKey($recordId))
        {
            throw 'P5A generator descendant process identity is invalid or duplicated.'
        }
        $byId[$recordId] = $record
        $path = [string]$record.executablePath
        $image = [string]$record.imageName
        if ([string]::IsNullOrWhiteSpace($image) -or
            -not [IO.Path]::IsPathFullyQualified($path) -or
            [IO.Path]::GetFileName($path) -cne $image)
        {
            throw 'P5A generator descendant image or executable path is invalid.'
        }
        $path = Get-P5aGeneratorFullPath $path
        if ($image -ceq 'conhost.exe' -and $path -ieq $conhostPath)
        {
            $rolesById[$recordId] = 'SystemConhost'
        }
        elseif ($AllowUeHelpers -and $null -ne $HelperLease -and
            $HelperLease.EntriesByPath.ContainsKey($path))
        {
            $rolesById[$recordId] = [string]$HelperLease.EntriesByPath[$path].Role
        }
        else
        {
            throw "P5A generator descendant process '$image' is forbidden (path=$path)."
        }
        Assert-P5aGeneratorNoReparsePoint $path
    }
    foreach ($record in $records)
    {
        $recordId = [int]$record.processId
        $parentId = [int]$record.parentProcessId
        $ancestors = @($record.ancestorProcessIds | ForEach-Object { [int]$_ })
        $expected = [Collections.Generic.List[int]]::new()
        $seen = [Collections.Generic.HashSet[int]]::new()
        $null = $seen.Add($recordId)
        $cursor = $parentId
        while ($cursor -ne $rootProcessId)
        {
            if (-not $seen.Add($cursor) -or -not $byId.ContainsKey($cursor))
            {
                throw 'P5A generator descendant parent is missing or cyclic.'
            }
            $expected.Insert(0, $cursor)
            $cursor = [int]$byId[$cursor].parentProcessId
        }
        $expected.Insert(0, $rootProcessId)
        if ($ancestors.Count -ne $expected.Count -or
            @((Compare-Object @($expected) $ancestors -SyncWindow 0)).Count -ne 0)
        {
            throw 'P5A generator descendant ancestor chain is incoherent.'
        }
        $role = [string]$rolesById[$recordId]
        $parentRole = if ($parentId -eq $rootProcessId) { 'Root' } else { [string]$rolesById[$parentId] }
        $allowed = switch ($role)
        {
            'SystemConhost' { $parentRole -cne 'SystemConhost' }
            'UnrealTraceServer.exe' {
                $parentRole -ceq 'Root' -or
                    ($parentRole -ceq 'UnrealTraceServer.exe' -and $ancestors.Count -eq 2 -and
                     $HelperLease.EntriesByPath[(Get-P5aGeneratorFullPath $record.executablePath)].Installed)
            }
            'zen.exe' { $parentRole -ceq 'Root' }
            'zenserver.exe' { $parentRole -ceq 'Root' }
            'crashpad_handler.exe' {
                ($parentRole -ceq 'zen.exe' -or $parentRole -ceq 'zenserver.exe') -and
                    (Split-Path -Parent $record.executablePath) -ieq
                    (Split-Path -Parent $byId[$parentId].executablePath)
            }
            default { $false }
        }
        if (-not $allowed -or
            ($role -ceq 'SystemConhost' -and @($records | Where-Object {
                [int]$_.parentProcessId -eq $recordId
            }).Count -ne 0))
        {
            throw "P5A generator descendant helper role '$role' has a forbidden parent '$parentRole' or child."
        }
    }
}

function Assert-P5aGeneratorFileBytesEqual
{
    param(
        [Parameter(Mandatory)][string]$First,
        [Parameter(Mandatory)][string]$Second,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $First -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Second -PathType Leaf) -or
        -not [Linq.Enumerable]::SequenceEqual(
            [byte[]][IO.File]::ReadAllBytes($First), [byte[]][IO.File]::ReadAllBytes($Second)))
    {
        throw "$Description same-engine byte determinism check failed."
    }
}

function Invoke-P5aGeneratorProcessProtocol
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$OracleAppHost,
        [Parameter(Mandatory)][string]$UnrealEditorCmd,
        [Parameter(Mandatory)][string]$UnrealProject,
        [Parameter(Mandatory)][string]$ReferenceRoot,
        [Parameter(Mandatory)][string]$StagingRoot,
        [object]$StagingLease,
        [Parameter(Mandatory)][scriptblock]$ProcessInvoker,
        [switch]$RetainOutputLeases,
        [int]$TimeoutSeconds = 600
    )

    $protocolOwnsStaging = $null -eq $StagingLease
    if ($RetainOutputLeases -and $protocolOwnsStaging)
    {
        throw 'P5A retained output leases require a caller-owned staging lease.'
    }
    if ($protocolOwnsStaging)
    {
        Assert-P5aGeneratorInvocationContract `
            -RepositoryRoot $RepositoryRoot -UnrealEditorCmd $UnrealEditorCmd `
            -UnrealProject $UnrealProject -ReferenceRoot $ReferenceRoot `
            -StagingRoot $StagingRoot -OracleAppHost $OracleAppHost `
            -TimeoutSeconds $TimeoutSeconds
        $StagingLease = Open-P5aGeneratorOwnedStaging $StagingRoot
    }
    else
    {
        Assert-P5aGeneratorInvocationContract `
            -RepositoryRoot $RepositoryRoot -UnrealEditorCmd $UnrealEditorCmd `
            -UnrealProject $UnrealProject -ReferenceRoot $ReferenceRoot `
            -StagingRoot $StagingRoot -OracleAppHost $OracleAppHost `
            -StagingAlreadyOwned -TimeoutSeconds $TimeoutSeconds
        if (-not $StagingLease.MatchesPath())
        {
            throw 'P5A staging directory identity changed before child execution.'
        }
    }
    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $staging = Get-P5aGeneratorFullPath $StagingRoot
    $paths = [ordered]@{
        PlanA = Join-Path $staging 'plan-a.json'
        PlanB = Join-Path $staging 'plan-b.json'
        ReadyOutput = Join-Path $staging 'ready-check.json'
        RawA = Join-Path $staging 'raw-a.json'
        RawB = Join-Path $staging 'raw-b.json'
        NativeA = Join-Path $staging 'native-a.json'
        NativeB = Join-Path $staging 'native-b.json'
        PortA = Join-Path $staging 'port-a.json'
        PortB = Join-Path $staging 'port-b.json'
        ReadyLog = Join-Path $staging 'ready-check.log'
        CaptureALog = Join-Path $staging 'capture-a.log'
        CaptureBLog = Join-Path $staging 'capture-b.log'
    }
    $commit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
    $patch = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'
    $digestPrefix = 'P5A_ORACLE_DIGESTS layout=f2336240d749284b bindings=40f33e59692dfd38 graph=44403c2869d8f615 plan='
    $protocolSucceeded = $false
    $retainedOutputLeases = [Collections.Generic.List[object]]::new()
    $retainedNativeLease = $null
    $helperLease = $null
    try
    {
        Assert-P5aGeneratorPhysicalStagingBoundary `
            -RepositoryRoot $root -StagingLease $StagingLease
        $helperLease = Open-P5aGeneratorHelperLease -UnrealEditorCmd $UnrealEditorCmd
        foreach ($planRow in @(
            @{ Name = 'A'; Path = $paths.PlanA },
            @{ Name = 'B'; Path = $paths.PlanB }))
        {
            Assert-P5aGeneratorHelperLease $helperLease
            $result = Invoke-P5aGeneratorChild -ProcessInvoker $ProcessInvoker `
                -FilePath $OracleAppHost `
                -Arguments @('--write-native-plan', '--repository-root', $root,
                             '--output', $planRow.Path) `
                -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds `
                -PhaseName "P5A native plan $($planRow.Name)"
            Assert-P5aGeneratorNoDescendants $result -HelperLease $helperLease
            $planHash = Get-P5aGeneratorFileHash $planRow.Path
            Assert-P5aChildGateOutput -PhaseName "P5A native plan $($planRow.Name)" `
                -StdOutLines @($result.StdOutLines) -StdErrLines @($result.StdErrLines) `
                -ExitCode $result.ExitCode -OutputLimitExceeded $result.OutputLimitExceeded `
                -TimedOut $result.TimedOut -ExpectedMarker ($digestPrefix + $planHash) | Out-Null
        }
        Assert-P5aGeneratorFileBytesEqual $paths.PlanA $paths.PlanB 'Plan'
        $planHash = Get-P5aGeneratorFileHash $paths.PlanA

        $ueRows = @(
            @{ Name = 'ReadyCheck'; Output = $paths.ReadyOutput; Log = $paths.ReadyLog; Ready = $true },
            @{ Name = 'capture A'; Output = $paths.RawA; Log = $paths.CaptureALog; Ready = $false },
            @{ Name = 'capture B'; Output = $paths.RawB; Log = $paths.CaptureBLog; Ready = $false })
        foreach ($row in $ueRows)
        {
            $arguments = @($UnrealProject, '-run=AlsLocomotionTrace', '-TraceKind=P5A')
            if ($row.Ready) { $arguments += '-ReadyCheck' }
            $arguments += @(
                "-Output=$($row.Output)", "-ReferenceRoot=$ReferenceRoot",
                "-ReferenceCommit=$commit", "-PatchHashes=$patch",
                "-P5ATracePlan=$($paths.PlanA)", "-P5ATracePlanSha256=$planHash",
                '-stdout', '-FullStdOutLogOutput', '-unattended', '-nosplash',
                '-nullrhi', '-nosound', '-Multiprocess', "-abslog=$($row.Log)")
            Assert-P5aGeneratorHelperLease $helperLease
            $result = Invoke-P5aGeneratorChild -ProcessInvoker $ProcessInvoker `
                -FilePath $UnrealEditorCmd -Arguments $arguments `
                -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds `
                -PhaseName "P5A UE $($row.Name)"
            Assert-P5aGeneratorNoDescendants $result `
                -HelperLease $helperLease -AllowUeHelpers
            $expectedMarker = if ($row.Ready) {
                "P5A_TRACE_READY_OK cases=8 commit=$commit"
            } else {
                "P5A_TRACE_GENERATION_OK cases=8 commit=$commit"
            }
            Assert-P5aChildGateOutput -PhaseName "P5A UE $($row.Name)" `
                -StdOutLines @($result.StdOutLines) -StdErrLines @($result.StdErrLines) `
                -ExitCode $result.ExitCode -OutputLimitExceeded $result.OutputLimitExceeded `
                -TimedOut $result.TimedOut -ExpectedMarker $expectedMarker `
                -AllowUeWrapper | Out-Null
            if ((Get-P5aGeneratorFileHash $paths.PlanA) -cne $planHash)
            {
                throw 'P5A verified plan A changed during child execution.'
            }
            if ($row.Ready -and (Test-Path -LiteralPath $paths.ReadyOutput))
            {
                throw 'P5A ReadyCheck produced a forbidden output artifact.'
            }
        }
        Assert-P5aGeneratorFileBytesEqual $paths.RawA $paths.RawB 'Raw'

        foreach ($pairRow in @(
            @{ Name = 'A'; Raw = $paths.RawA; Native = $paths.NativeA; Port = $paths.PortA },
            @{ Name = 'B'; Raw = $paths.RawB; Native = $paths.NativeB; Port = $paths.PortB }))
        {
            Assert-P5aGeneratorHelperLease $helperLease
            $result = Invoke-P5aGeneratorChild -ProcessInvoker $ProcessInvoker `
                -FilePath $OracleAppHost `
                -Arguments @(
                    '--write-canonical-pair', '--repository-root', $root,
                    '--trace-plan', $paths.PlanA, '--raw', $pairRow.Raw,
                    '--native-canonical', $pairRow.Native, '--port-canonical', $pairRow.Port) `
                -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds `
                -PhaseName "P5A canonical pair $($pairRow.Name)"
            Assert-P5aGeneratorNoDescendants $result -HelperLease $helperLease
            Assert-P5aChildGateOutput -PhaseName "P5A canonical pair $($pairRow.Name)" `
                -StdOutLines @($result.StdOutLines) -StdErrLines @($result.StdErrLines) `
                -ExitCode $result.ExitCode -OutputLimitExceeded $result.OutputLimitExceeded `
                -TimedOut $result.TimedOut -ExpectedMarker ($digestPrefix + $planHash) | Out-Null
            if ((Get-P5aGeneratorFileHash $paths.PlanA) -cne $planHash)
            {
                throw 'P5A verified plan A changed during child execution.'
            }
        }
        Assert-P5aGeneratorFileBytesEqual $paths.NativeA $paths.NativeB 'Native'
        Assert-P5aGeneratorFileBytesEqual $paths.PortA $paths.PortB 'Port'
        if ($RetainOutputLeases)
        {
            $requiredOutputPaths = @(
                $paths.PlanA, $paths.PlanB, $paths.RawA, $paths.RawB,
                $paths.NativeA, $paths.NativeB, $paths.PortA, $paths.PortB)
            $allowedOutputPaths = @($requiredOutputPaths) + @(
                $paths.ReadyLog, $paths.CaptureALog, $paths.CaptureBLog)
            $actualItems = @(Get-ChildItem -LiteralPath $staging -Force -Recurse)
            foreach ($item in $actualItems)
            {
                Assert-P5aGeneratorNoReparsePoint $item.FullName
                if ($item.PSIsContainer)
                {
                    throw "P5A staging contains an unexpected child directory: $($item.FullName)"
                }
            }
            $actualOutputPaths = @($actualItems | ForEach-Object {
                Get-P5aGeneratorFullPath $_.FullName
            })
            if (@($requiredOutputPaths | Where-Object {
                    $_ -cnotin $actualOutputPaths
                }).Count -ne 0 -or
                @($actualOutputPaths | Where-Object {
                    $_ -cnotin $allowedOutputPaths
                }).Count -ne 0)
            {
                throw 'P5A staging output path closure changed before it could be pinned.'
            }
            foreach ($outputPath in $actualOutputPaths)
            {
                $retainedOutputLeases.Add((Open-P5aGeneratorPinnedFile $outputPath))
            }
            Assert-P5aGeneratorFileBytesEqual $paths.PlanA $paths.PlanB 'Plan'
            if ((Get-P5aGeneratorFileHash $paths.PlanA) -cne $planHash)
            {
                throw 'P5A verified plan A changed before output leases were retained.'
            }
            Assert-P5aGeneratorFileBytesEqual $paths.RawA $paths.RawB 'Raw'
            Assert-P5aGeneratorFileBytesEqual $paths.NativeA $paths.NativeB 'Native'
            Assert-P5aGeneratorFileBytesEqual $paths.PortA $paths.PortB 'Port'
            $nativeLeaseMatches = @($retainedOutputLeases | Where-Object {
                [string]$_.Name -ceq (Get-P5aGeneratorFullPath $paths.NativeA)
            })
            if ($nativeLeaseMatches.Count -ne 1)
            {
                throw 'P5A native-a output lease closure is invalid.'
            }
            $retainedNativeLease = $nativeLeaseMatches[0]
        }
        $protocolSucceeded = $true
        $result = [pscustomobject]$paths
        if ($RetainOutputLeases)
        {
            Add-Member -InputObject $result -NotePropertyName OutputLeases `
                -NotePropertyValue @($retainedOutputLeases)
            Add-Member -InputObject $result -NotePropertyName NativeLease `
                -NotePropertyValue $retainedNativeLease
            Add-Member -InputObject $result -NotePropertyName NativeSha256 `
                -NotePropertyValue ([string]$retainedNativeLease.HashSha256())
        }
        return $result
    }
    finally
    {
        if ($null -ne $helperLease) { $helperLease.Dispose() }
        if (-not $protocolSucceeded)
        {
            foreach ($outputLease in $retainedOutputLeases)
            {
                try { $outputLease.Dispose() } catch { }
            }
        }
        if ($protocolOwnsStaging)
        {
            if ($protocolSucceeded)
            {
                try { $StagingLease.Dispose() } catch { }
            }
            else { try { Close-P5aGeneratorOwnedStaging $StagingLease } catch { } }
        }
    }
}
function Assert-P5aGeneratorEvidenceObject
{
    param(
        [Parameter(Mandatory)][object]$Evidence,
        [Parameter(Mandatory)][string[]]$Names,
        [Parameter(Mandatory)][string]$Description
    )

    $properties = @($Evidence.PSObject.Properties)
    if (@($properties | ForEach-Object { $_.Name }).Count -ne $Names.Count -or
        @(Compare-Object -CaseSensitive -ReferenceObject $Names `
            -DifferenceObject @($properties | ForEach-Object { $_.Name })).Count -ne 0)
    {
        throw "$Description evidence property closure is invalid."
    }
    foreach ($property in $properties)
    {
        if ($property.Name -ceq 'sdkVersion')
        {
            if ([string]::IsNullOrEmpty([string]$property.Value))
            {
                throw "$Description SDK version is empty."
            }
        }
        elseif ([string]$property.Value -cnotmatch '^[0-9a-f]{64}$')
        {
            throw "$Description evidence hash '$($property.Name)' is malformed."
        }
    }
}

function Write-P5aReadyManifest
{
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][object]$OracleEvidence,
        [Parameter(Mandatory)][object]$BuildEvidence
    )

    $staging = Get-P5aGeneratorFullPath $StagingRoot
    if (-not (Test-Path -LiteralPath $staging -PathType Container))
    {
        throw "P5A staging root is missing: $staging"
    }
    Assert-P5aGeneratorNoReparsePoint $staging
    $oracleNames = @(
        'sdkVersion', 'sourceTreeSha256', 'buildManifestSha256',
        'executableSha256', 'oracleAssemblySha256', 'importAssemblySha256',
        'coreAssemblySha256', 'depsSha256', 'runtimeConfigSha256')
    $buildNames = @(
        'ownedPluginTreeSha256', 'targetReceiptSha256',
        'alsModuleManifestSha256', 'traceModuleManifestSha256',
        'alsModuleDllSha256', 'alsCameraModuleDllSha256',
        'alsEditorModuleDllSha256', 'alsExtrasModuleDllSha256',
        'traceModuleDllSha256')
    Assert-P5aGeneratorEvidenceObject $OracleEvidence $oracleNames 'Oracle'
    Assert-P5aGeneratorEvidenceObject $BuildEvidence $buildNames 'Build'

    $manifestPath = Join-Path $staging 'ready-manifest.json'
    if (Test-Path -LiteralPath $manifestPath)
    {
        throw 'P5A ready manifest already exists.'
    }
    $stagedItems = @(Get-ChildItem -LiteralPath $staging -Force -Recurse)
    foreach ($item in $stagedItems)
    {
        Assert-P5aGeneratorNoReparsePoint $item.FullName
    }
    $payloadFiles = @($stagedItems | Where-Object { -not $_.PSIsContainer } | Sort-Object {
        [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\', '/')
    })
    if ($payloadFiles.Count -eq 0) { throw 'P5A staging payload is empty.' }
    $payloads = @($payloadFiles | ForEach-Object {
        [pscustomobject][ordered]@{
            path = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\', '/')
            sha256 = Get-P5aGeneratorFileHash $_.FullName
        }
    })
    $document = [pscustomobject][ordered]@{
        schemaVersion = 1
        kind = 'p5a_ready_manifest'
        oracleEvidence = $OracleEvidence
        buildEvidence = $BuildEvidence
        payloads = @($payloads)
    }
    $json = ($document | ConvertTo-Json -Depth 20 -Compress) + "`n"
    [IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
    return $manifestPath
}

function Assert-P5aReadyManifest
{
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$ManifestPath,
        [switch]$PassThru
    )

    $staging = Get-P5aGeneratorFullPath $StagingRoot
    $manifest = Get-P5aGeneratorFullPath $ManifestPath
    if ((Split-Path -Parent $manifest) -cne $staging -or
        (Split-Path -Leaf $manifest) -cne 'ready-manifest.json' -or
        -not (Test-Path -LiteralPath $manifest -PathType Leaf))
    {
        throw 'P5A ready manifest path is invalid.'
    }
    Assert-P5aGeneratorNoReparsePoint $staging
    Assert-P5aGeneratorNoReparsePoint $manifest
    $bytes = [IO.File]::ReadAllBytes($manifest)
    if ($bytes.Length -eq 0 -or
        ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and
         $bytes[2] -eq 0xbf))
    {
        throw 'P5A ready manifest encoding is invalid.'
    }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    if ($text.Contains("`r", [StringComparison]::Ordinal) -or
        -not $text.EndsWith("`n", [StringComparison]::Ordinal) -or
        $text.EndsWith("`n`n", [StringComparison]::Ordinal))
    {
        throw 'P5A ready manifest newline contract is invalid.'
    }
    try { $document = $text | ConvertFrom-Json -Depth 30 }
    catch { throw "P5A ready manifest JSON is invalid: $($_.Exception.Message)" }
    $rootNames = @('schemaVersion', 'kind', 'oracleEvidence', 'buildEvidence', 'payloads')
    if (@(Compare-Object $rootNames `
            @($document.PSObject.Properties | ForEach-Object { $_.Name }) `
            -CaseSensitive).Count -ne 0)
    {
        throw 'P5A ready manifest root property closure is invalid.'
    }
    if ([int]$document.schemaVersion -ne 1 -or
        [string]$document.kind -cne 'p5a_ready_manifest')
    {
        throw 'P5A ready manifest identity is invalid.'
    }
    $oracleNames = @(
        'sdkVersion', 'sourceTreeSha256', 'buildManifestSha256',
        'executableSha256', 'oracleAssemblySha256', 'importAssemblySha256',
        'coreAssemblySha256', 'depsSha256', 'runtimeConfigSha256')
    $buildNames = @(
        'ownedPluginTreeSha256', 'targetReceiptSha256',
        'alsModuleManifestSha256', 'traceModuleManifestSha256',
        'alsModuleDllSha256', 'alsCameraModuleDllSha256',
        'alsEditorModuleDllSha256', 'alsExtrasModuleDllSha256',
        'traceModuleDllSha256')
    Assert-P5aGeneratorEvidenceObject $document.oracleEvidence $oracleNames 'Oracle'
    Assert-P5aGeneratorEvidenceObject $document.buildEvidence $buildNames 'Build'

    $rows = @($document.payloads)
    $rowPaths = [Collections.Generic.List[string]]::new()
    $seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows)
    {
        if (@(Compare-Object @('path', 'sha256') `
                @($row.PSObject.Properties | ForEach-Object { $_.Name }) `
                -CaseSensitive).Count -ne 0)
        {
            throw 'P5A ready manifest payload property closure is invalid.'
        }
        $relative = [string]$row.path
        if ([string]::IsNullOrEmpty($relative) -or [IO.Path]::IsPathFullyQualified($relative) -or
            $relative.Contains('\', [StringComparison]::Ordinal) -or
            @($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0)
        {
            throw 'P5A ready manifest payload path is unsafe.'
        }
        if (-not $seenPaths.Add($relative)) { throw 'P5A ready manifest payload is duplicated.' }
        $rowPaths.Add($relative)
        $payloadPath = Get-P5aGeneratorFullPath (Join-Path $staging $relative)
        Assert-P5aGeneratorNoReparsePoint $payloadPath
        $prefix = $staging + [IO.Path]::DirectorySeparatorChar
        if (-not $payloadPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
            $payloadPath -ceq $manifest -or
            (Get-P5aGeneratorFileHash $payloadPath) -cne [string]$row.sha256)
        {
            throw "P5A ready manifest payload changed or escaped staging: $relative"
        }
    }
    $actualItems = @(Get-ChildItem -LiteralPath $staging -Force -Recurse)
    foreach ($item in $actualItems)
    {
        Assert-P5aGeneratorNoReparsePoint $item.FullName
    }
    $actualRelativePaths = @(
        $actualItems |
            Where-Object { -not $_.PSIsContainer -and $_.FullName -cne $manifest } |
            ForEach-Object { [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\', '/') })
    if (@(Compare-Object -CaseSensitive -ReferenceObject @($rowPaths | Sort-Object) `
            -DifferenceObject @($actualRelativePaths | Sort-Object)).Count -ne 0)
    {
        throw 'P5A ready manifest does not close over the exact staged payload set.'
    }
    if ($PassThru) { return $document }
}

function Invoke-P5aGeneratorDefaultFileSystem
{
    param(
        $Operation,
        $SourcePath,
        $DestinationPath,
        $OracleLease,
        $DeploymentLease,
        $ValidatedFixtureLease,
        $PublicationLease
    )

    switch ([string]$Operation)
    {
        'File.Copy' {
            if ($null -eq $ValidatedFixtureLease)
            {
                [IO.File]::Copy($SourcePath, $DestinationPath)
                return
            }
            $ValidatedFixtureLease.CopyTo([string]$DestinationPath)
            return
        }
        'File.Replace' {
            if ($null -ne $PublicationLease)
            {
                $PublicationLease.CommitTo([string]$DestinationPath, $true)
                return
            }
            if ($false)
            {
                [IO.File]::Replace($SourcePath, $DestinationPath, $null)
                return
            }
            $method = [IO.File].GetMethod(
                'Replace', [type[]]@([string], [string], [string]))
            $arguments = [object[]]@([string]$SourcePath, [string]$DestinationPath, $null)
            try { [void]$method.Invoke($null, $arguments) }
            catch
            {
                if ($null -ne $_.Exception.InnerException) { throw $_.Exception.InnerException }
                throw
            }
            return
        }
        'File.Move' {
            if ($null -ne $PublicationLease)
            {
                $PublicationLease.CommitTo([string]$DestinationPath, $false)
                return
            }
            [IO.File]::Move($SourcePath, $DestinationPath)
            return
        }
        default { throw "Unsupported P5A filesystem operation: $Operation" }
    }
}

function Remove-P5aGeneratorPathBestEffort
{
    param([string]$Path)

    if ([string]::IsNullOrEmpty($Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { return }
    if ($item.PSIsContainer) { [IO.Directory]::Delete($item.FullName, $false) }
    else { [IO.File]::Delete($item.FullName) }
}

function Publish-P5aFixtureAtomically
{
    param(
        [Parameter(Mandatory)][string]$ValidatedFixturePath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [scriptblock]$FileSystemInvoker = ${function:Invoke-P5aGeneratorDefaultFileSystem},
        [object]$OracleLease,
        [object]$DeploymentLease,
        [object]$StagingLease,
        [object]$ValidatedFixtureLease,
        [object[]]$StagingPayloadLeases = @(),
        [string]$ExpectedFixtureSha256
    )

    $source = Get-P5aGeneratorFullPath $ValidatedFixturePath
    $destination = Get-P5aGeneratorFullPath $DestinationPath
    $sourceRoot = Split-Path -Parent $source
    $destinationRoot = Split-Path -Parent $destination
    $temporaryPath = Join-Path $destinationRoot (
        '.' + (Split-Path -Leaf $destination) + '.p5a-incoming.' +
        [Guid]::NewGuid().ToString('N') + '.tmp')
    $usesDefaultFileSystem = -not $PSBoundParameters.ContainsKey('FileSystemInvoker')
    $publicationLease = $null
    try
    {
        $prepareError = $null
        $stagingCleanupError = $null
        try
        {
            if (($null -eq $StagingLease) -ne ($null -eq $ValidatedFixtureLease))
            {
                throw 'P5A validated fixture lease and owned staging lease must be supplied together.'
            }
            if ($null -ne $StagingLease -and
                $ExpectedFixtureSha256 -cnotmatch '^[0-9a-f]{64}$')
            {
                throw 'P5A strict staging publication requires the validated fixture SHA-256.'
            }
            if ($null -eq $StagingLease -and
                -not [string]::IsNullOrEmpty($ExpectedFixtureSha256))
            {
                throw 'P5A validated fixture SHA-256 requires an owned staging lease.'
            }
            if ($null -ne $StagingLease -and @($StagingPayloadLeases).Count -eq 0)
            {
                throw 'P5A strict staging publication requires pinned payload leases.'
            }
            if ($null -eq $StagingLease -and @($StagingPayloadLeases).Count -ne 0)
            {
                throw 'P5A pinned payload leases require an owned staging lease.'
            }
            if (-not (Test-Path -LiteralPath $source -PathType Leaf))
            {
                throw "Validated P5A fixture is missing: $source"
            }
            [void][IO.Directory]::CreateDirectory($destinationRoot)
            & $FileSystemInvoker 'File.Copy' $source $temporaryPath $OracleLease `
                $DeploymentLease $ValidatedFixtureLease
            if ($usesDefaultFileSystem)
            {
                $publicationLease = Open-P5aGeneratorPinnedFile `
                    -Path $temporaryPath -AllowAtomicRename
            }
            $copiedHash = if ($null -ne $publicationLease) {
                [string]$publicationLease.HashSha256()
            } else {
                Get-P5aGeneratorFileHash $temporaryPath
            }
            if (-not [string]::IsNullOrEmpty($ExpectedFixtureSha256) -and
                $copiedHash -cne $ExpectedFixtureSha256)
            {
                throw 'P5A copied fixture bytes do not match the validated manifest payload.'
            }
        }
        catch { $prepareError = $_ }
        if ($null -ne $StagingLease)
        {
            if ($null -eq $ValidatedFixtureLease -or @($StagingPayloadLeases).Count -eq 0)
            {
                $stagingCleanupError = [InvalidOperationException]::new(
                    'P5A strict staging cleanup requires pinned payload leases.')
            }
            else
            {
                foreach ($payloadLease in @($StagingPayloadLeases))
                {
                    try { $payloadLease.Dispose() }
                    catch { if ($null -eq $stagingCleanupError) { $stagingCleanupError = $_ } }
                }
            }
            try { Close-P5aGeneratorOwnedStaging $StagingLease }
            catch { if ($null -eq $stagingCleanupError) { $stagingCleanupError = $_ } }
        }
        if ($null -ne $prepareError) { throw $prepareError }
        if ($null -ne $stagingCleanupError) { throw $stagingCleanupError }
        if (Test-Path -LiteralPath $destination -PathType Leaf)
        {
            & $FileSystemInvoker 'File.Replace' $temporaryPath $destination $OracleLease `
                $DeploymentLease $ValidatedFixtureLease $publicationLease
            return
        }
        & $FileSystemInvoker 'File.Move' $temporaryPath $destination $OracleLease `
            $DeploymentLease $ValidatedFixtureLease $publicationLease
        return
    }
    finally
    {
        try { if ($null -ne $publicationLease) { $publicationLease.Dispose() } } catch { }
        try { Remove-P5aGeneratorPathBestEffort $temporaryPath } catch { }
        try { Remove-P5aGeneratorPathBestEffort $source } catch { }
        try { Remove-P5aGeneratorPathBestEffort $sourceRoot } catch { }
    }
}

function Invoke-P5aGeneratorCheckpoint
{
    param(
        [scriptblock]$CheckpointInvoker,
        [Parameter(Mandatory)][string]$Checkpoint,
        [object]$OracleLease,
        [object]$DeploymentLease
    )

    if ($null -ne $CheckpointInvoker)
    {
        & $CheckpointInvoker $Checkpoint $OracleLease $DeploymentLease | Out-Null
    }
}

function Invoke-P5aGeneratorWorkflow
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$UnrealEditorCmd,
        [Parameter(Mandatory)][string]$UnrealProject,
        [Parameter(Mandatory)][string]$ReferenceRoot,
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$DestinationPath,
        [scriptblock]$ProcessInvoker = ${function:Invoke-P5aGeneratorDefaultProcess},
        [scriptblock]$FileSystemInvoker,
        [scriptblock]$CheckpointInvoker,
        [int]$TimeoutSeconds = 600
    )
    Assert-P5aGeneratorInvocationContract `
        -RepositoryRoot $RepositoryRoot -UnrealEditorCmd $UnrealEditorCmd `
        -UnrealProject $UnrealProject -ReferenceRoot $ReferenceRoot `
        -StagingRoot $StagingRoot -DestinationPath $DestinationPath `
        -TimeoutSeconds $TimeoutSeconds
    $root = Get-P5aGeneratorFullPath $RepositoryRoot
    $staging = Get-P5aGeneratorFullPath $StagingRoot
    $stagingLease = Open-P5aGeneratorOwnedStaging $staging
    $oracleLease = $null
    $deploymentLease = $null
    $nativeLease = $null
    $stagingPayloadLeases = [Collections.Generic.List[object]]::new()
    try
    {
        Assert-P5aGeneratorPhysicalStagingBoundary `
            -RepositoryRoot $root -StagingLease $stagingLease
        $oracleLease = Open-P5aOracleEvidenceLease -RepositoryRoot $root
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'OracleEvidenceOpened' `
            $oracleLease $null
        $deploymentLease = Open-P5aDeployedBuildEvidenceLease `
            -RepositoryRoot $root -UnrealProject $UnrealProject
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'DeploymentEvidenceOpened' `
            $oracleLease $deploymentLease
        $paths = Invoke-P5aGeneratorProcessProtocol `
            -RepositoryRoot $root -OracleAppHost $oracleLease.AppHostPath `
            -UnrealEditorCmd $UnrealEditorCmd -UnrealProject $UnrealProject `
            -ReferenceRoot $ReferenceRoot -StagingRoot $staging `
            -StagingLease $stagingLease -ProcessInvoker $ProcessInvoker `
            -RetainOutputLeases `
            -TimeoutSeconds $TimeoutSeconds
        foreach ($outputLease in @($paths.OutputLeases))
        {
            [void]$stagingPayloadLeases.Add($outputLease)
        }
        $nativeLease = $paths.NativeLease
        $trustedNativeHash = [string]$paths.NativeSha256
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'ChildrenCompleted' `
            $oracleLease $deploymentLease
        $manifestPath = Write-P5aReadyManifest -StagingRoot $staging `
            -OracleEvidence $oracleLease.Evidence -BuildEvidence $deploymentLease.Evidence
        $manifestLease = Open-P5aGeneratorPinnedFile $manifestPath
        [void]$stagingPayloadLeases.Add($manifestLease)
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'ReadyManifestWritten' `
            $oracleLease $deploymentLease
        $initialManifest = Assert-P5aReadyManifest -StagingRoot $staging `
            -ManifestPath $manifestPath -PassThru
        $initialNativePayload = @($initialManifest.payloads | Where-Object {
            [string]$_.path -ceq 'native-a.json'
        })
        if ($initialNativePayload.Count -ne 1 -or
            [string]$initialNativePayload[0].sha256 -cne $trustedNativeHash -or
            ($initialManifest.oracleEvidence | ConvertTo-Json -Compress) -cne
                ($oracleLease.Evidence | ConvertTo-Json -Compress) -or
            ($initialManifest.buildEvidence | ConvertTo-Json -Compress) -cne
                ($deploymentLease.Evidence | ConvertTo-Json -Compress))
        {
            throw 'P5A ready manifest does not match the pinned generation evidence.'
        }
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'ReadyManifestReopened' `
            $oracleLease $deploymentLease
        Assert-P5aOracleEvidenceUnchanged -RepositoryRoot $root `
            -ExpectedEvidence $oracleLease.Evidence
        Assert-P5aDeployedBuildEvidenceUnchanged -RepositoryRoot $root `
            -UnrealProject $UnrealProject -ExpectedEvidence $deploymentLease.Evidence
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'EvidenceRechecked' `
            $oracleLease $deploymentLease
        Invoke-P5aGeneratorCheckpoint $CheckpointInvoker 'BeforePublication' `
            $oracleLease $deploymentLease
        $validatedManifest = Assert-P5aReadyManifest -StagingRoot $staging `
            -ManifestPath $manifestPath -PassThru
        $nativePayload = @($validatedManifest.payloads | Where-Object {
            [string]$_.path -ceq 'native-a.json'
        })
        if ($nativePayload.Count -ne 1)
        {
            throw 'P5A ready manifest must contain exactly one native-a.json payload.'
        }
        if ([string]$nativePayload[0].sha256 -cne $trustedNativeHash -or
            [string]$nativeLease.HashSha256() -cne $trustedNativeHash)
        {
            throw 'P5A pinned native-a bytes changed before publication.'
        }
        Assert-P5aReadyManifest -StagingRoot $staging -ManifestPath $manifestPath
        $publicationParameters = @{
            ValidatedFixturePath = $paths.NativeA
            DestinationPath = $DestinationPath
            OracleLease = $oracleLease
            DeploymentLease = $deploymentLease
            StagingLease = $stagingLease
            ValidatedFixtureLease = $nativeLease
            StagingPayloadLeases = @($stagingPayloadLeases)
            ExpectedFixtureSha256 = $trustedNativeHash
        }
        if ($PSBoundParameters.ContainsKey('FileSystemInvoker'))
        {
            $publicationParameters.FileSystemInvoker = $FileSystemInvoker
        }
        Publish-P5aFixtureAtomically @publicationParameters
        "P5A_GOLDEN_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17"
    }
    finally
    {
        foreach ($payloadLease in $stagingPayloadLeases)
        {
            try { $payloadLease.Dispose() } catch { }
        }
        try { if ($null -ne $deploymentLease) { $deploymentLease.Dispose() } } catch { }
        try { if ($null -ne $oracleLease) { $oracleLease.Dispose() } } catch { }
        try { Close-P5aGeneratorOwnedStaging $stagingLease } catch { }
    }
}

if ($MyInvocation.InvocationName -ne '.')
{
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot))
    {
        $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    }
    if ([string]::IsNullOrWhiteSpace($DestinationPath))
    {
        $DestinationPath = Join-Path $RepositoryRoot `
            'tests\Als.Core.Tests\Fixtures\P5A\trace_p5a_runtime.json'
    }
    if ([string]::IsNullOrWhiteSpace($StagingRoot))
    {
        $StagingRoot = Join-Path ([IO.Path]::GetTempPath()) `
            ('godotals-p5a-generator-' + [Guid]::NewGuid().ToString('N'))
    }
    $workflowParameters = @{
        RepositoryRoot = $RepositoryRoot
        UnrealEditorCmd = $UnrealEditorCmd
        UnrealProject = $UnrealProject
        ReferenceRoot = $ReferenceRoot
        StagingRoot = $StagingRoot
        DestinationPath = $DestinationPath
        TimeoutSeconds = $TimeoutSeconds
    }
    if ($null -ne $ProcessInvoker) { $workflowParameters.ProcessInvoker = $ProcessInvoker }
    if ($PSBoundParameters.ContainsKey('FileSystemInvoker'))
    {
        $workflowParameters.FileSystemInvoker = $FileSystemInvoker
    }
    if ($null -ne $CheckpointInvoker) { $workflowParameters.CheckpointInvoker = $CheckpointInvoker }
    Invoke-P5aGeneratorWorkflow @workflowParameters
}
