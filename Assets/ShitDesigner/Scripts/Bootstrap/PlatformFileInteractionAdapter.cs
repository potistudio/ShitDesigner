using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ShitDesigner.Presentation;

namespace ShitDesigner.Bootstrap {
	/// <summary>Small platform boundary for native file/folder selection.
	/// It deliberately knows nothing about Project or Media and never uses an
	/// Editor-only API. Implementations invoke the callback when the native
	/// dialog closes; the adapter validates request/session identity before it
	/// reaches Presentation.</summary>
	public interface IPlatformFileDialogBackend : IDisposable {
		bool IsSupported { get; }
		void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed);
		void Cancel(Guid requestId);
	}

	public sealed class PlatformFileInteractionAdapter : IPlatformFileInteractionAdapter, IDisposable {
		private readonly IPlatformFileDialogBackend _backend;
		private readonly SynchronizationContext _mainContext;
		private readonly object _gate = new object();
		private readonly Dictionary<Guid, PlatformPathRequest> _active = new Dictionary<Guid, PlatformPathRequest>();
		private bool _disposed;

		public bool IsSupported => !_disposed && _backend != null && _backend.IsSupported;
		public int ActiveRequestCount { get { lock (_gate) return _active.Count; } }

		public PlatformFileInteractionAdapter(IPlatformFileDialogBackend backend = null) {
			_backend = backend ?? PlatformFileDialogBackend.CreateDefault();
			_mainContext = SynchronizationContext.Current;
		}

		public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) {
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (completed == null) throw new ArgumentNullException(nameof(completed));
			lock (_gate) {
				if (_disposed) { Dispatch(completed, Failure(request, "The platform file dialog is unavailable after shutdown.")); return; }
				if (_active.ContainsKey(request.RequestId)) { Dispatch(completed, Failure(request, "A file dialog request with this ID is already active.")); return; }
				_active.Add(request.RequestId, request);
			}

			if (_backend == null || !_backend.IsSupported) {
				Complete(request, completed, Failure(request, "Native file selection is unavailable on this Player platform."));
				return;
			}
			try { _backend.PickPath(request, result => Complete(request, completed, result)); }
			catch (Exception exception) { Complete(request, completed, Failure(request, "The native file dialog could not be opened.", exception)); }
		}

		public void Cancel(Guid requestId) {
			PlatformPathRequest request = null;
			lock (_gate) {
				if (!_active.TryGetValue(requestId, out request)) return;
				_active.Remove(requestId);
			}
			try { _backend?.Cancel(requestId); } catch { }
		}

		private void Complete(PlatformPathRequest expected, Action<PlatformPathResult> completed, PlatformPathResult result) {
			PlatformPathResult normalized = null;
			lock (_gate) {
				if (_disposed || !_active.Remove(expected.RequestId)) return;
				if (result == null || result.RequestId != expected.RequestId || result.ProjectSessionId != expected.ProjectSessionId)
					normalized = Failure(expected, "The native file dialog returned a stale result.");
				else
					normalized = result;
			}
			Dispatch(completed, normalized);
		}

		private void Dispatch(Action<PlatformPathResult> completed, PlatformPathResult result) {
			if (_mainContext != null && !ReferenceEquals(SynchronizationContext.Current, _mainContext)) {
				_mainContext.Post(_ => completed(result), null);
				return;
			}
			completed(result);
		}

		public void Dispose() {
			List<Guid> requests;
			lock (_gate) {
				if (_disposed) return;
				_disposed = true;
				requests = _active.Keys.ToList();
				_active.Clear();
			}
			foreach (var request in requests) try { _backend?.Cancel(request); } catch { }
			try { _backend?.Dispose(); } catch { }
		}

		private static PlatformPathResult Failure(PlatformPathRequest request, string message, Exception exception = null)
			=> new PlatformPathResult(request.RequestId, request.ProjectSessionId, false, error: message + (exception == null ? string.Empty : " " + exception.Message));
	}

	public static class PlatformFileDialogBackend {
		public static IPlatformFileDialogBackend CreateDefault() {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			return new WindowsNativeFileDialogBackend();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return new MacNativeFileDialogBackend();
#else
            return new UnsupportedPlatformFileDialogBackend();
#endif
		}
	}

	internal sealed class UnsupportedPlatformFileDialogBackend : IPlatformFileDialogBackend {
		public bool IsSupported => false;
		public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) { }
		public void Cancel(Guid requestId) { }
		public void Dispose() { }
	}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	internal sealed class WindowsNativeFileDialogBackend : IPlatformFileDialogBackend {
		private readonly Dictionary<Guid, bool> _active = new Dictionary<Guid, bool>();
		public bool IsSupported => true;

		public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) {
			if (request == null || completed == null) return;
			_active[request.RequestId] = true;
			try {
				var paths = request.Kind == PlatformPathRequestKind.Folder ? PickFolder(request.Title) : PickFiles(request);
				if (_active.ContainsKey(request.RequestId))
					completed(new PlatformPathResult(request.RequestId, request.ProjectSessionId, paths.Count > 0, paths, paths.Count == 0 ? "The native dialog was cancelled." : null));
			}
			finally { _active.Remove(request.RequestId); }
		}

		public void Cancel(Guid requestId) => _active.Remove(requestId);

		private static IReadOnlyList<string> PickFiles(PlatformPathRequest request) {
			var buffer = new StringBuilder(64 * 1024);
			var options = new OpenFileName {
				StructSize = Marshal.SizeOf(typeof(OpenFileName)),
				Owner = GetActiveWindow(),
				Filter = "All files\0*.*\0\0",
				File = buffer,
				FileTitle = new StringBuilder(1024),
				MaxFile = buffer.Capacity,
				Title = request.Title ?? string.Empty,
				Flags = OpenFileNameFlags.Explorer | OpenFileNameFlags.FileMustExist | OpenFileNameFlags.PathMustExist
				| (request.Kind == PlatformPathRequestKind.MultiFile ? OpenFileNameFlags.AllowMultiSelect : 0)
			};
			if (!GetOpenFileName(ref options)) return Array.Empty<string>();
			return ParseWindowsSelection(buffer.ToString());
		}

		private static IReadOnlyList<string> ParseWindowsSelection(string value) {
			var parts = value.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length <= 1) return parts.Length == 0 ? Array.Empty<string>() : new[] { parts[0] };
			var directory = parts[0];
			return parts.Skip(1).Select(path => Path.Combine(directory, path)).ToList();
		}

		private static IReadOnlyList<string> PickFolder(string title) {
			var display = new StringBuilder(32 * 1024);
			var info = new BrowseInfo { Owner = GetActiveWindow(), DisplayName = display, Title = title ?? string.Empty, Flags = BrowseFlags.ReturnOnlyFileSystem | BrowseFlags.NewDialogStyle };
			var item = BrowseForFolder(ref info);
			if (item == IntPtr.Zero) return Array.Empty<string>();
			try {
				var path = new StringBuilder(32 * 1024);
				return GetPathFromItem(item, path) ? new[] { path.ToString() } : Array.Empty<string>();
			}
			finally { CoTaskMemFree(item); }
		}

		public void Dispose() => _active.Clear();

		[Flags]
		private enum OpenFileNameFlags : int { AllowMultiSelect = 0x200, Explorer = 0x80000, FileMustExist = 0x1000, PathMustExist = 0x800 }
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct OpenFileName {
			public int StructSize; public IntPtr Owner; public IntPtr Instance;
			[MarshalAs(UnmanagedType.LPWStr)] public string Filter; [MarshalAs(UnmanagedType.LPWStr)] public string CustomFilter;
			public int MaxCustFilter; public int FilterIndex; public StringBuilder File; public int MaxFile;
			public StringBuilder FileTitle; public int MaxFileTitle; [MarshalAs(UnmanagedType.LPWStr)] public string InitialDir;
			[MarshalAs(UnmanagedType.LPWStr)] public string Title; public OpenFileNameFlags Flags; public short FileOffset;
			public short FileExtension; [MarshalAs(UnmanagedType.LPWStr)] public string DefExt; public IntPtr CustomData; public IntPtr Hook;
			[MarshalAs(UnmanagedType.LPWStr)] public string TemplateName; public IntPtr Reserved; public int Reserved2; public IntPtr ExHook;
		}
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct BrowseInfo { public IntPtr Owner; public IntPtr Root; public StringBuilder DisplayName; [MarshalAs(UnmanagedType.LPWStr)] public string Title; public BrowseFlags Flags; public IntPtr Callback; public IntPtr Param; public int Image; }
		[Flags] private enum BrowseFlags : uint { ReturnOnlyFileSystem = 0x1, NewDialogStyle = 0x40 }
		[DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetOpenFileName(ref OpenFileName options);
		[DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr BrowseForFolder(ref BrowseInfo info);
		[DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool GetPathFromItem(IntPtr item, StringBuilder path);
		[DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr item);
		[DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
	}
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    internal sealed class MacNativeFileDialogBackend : IPlatformFileDialogBackend
    {
        private readonly Dictionary<Guid, Process> _active = new Dictionary<Guid, Process>();
        public bool IsSupported => File.Exists("/usr/bin/osascript");

        public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed)
        {
            if (request == null || completed == null) return;
            var script = request.Kind == PlatformPathRequestKind.Folder
                ? "set picked to choose folder with prompt " + Quote(request.Title) + "\nposix path of picked"
                : request.Kind == PlatformPathRequestKind.MultiFile
                    ? "set picked to choose file with prompt " + Quote(request.Title) + " with multiple selections allowed\nset output to {}\nrepeat with itemRef in picked\nset end of output to (POSIX path of itemRef)\nend repeat\nset AppleScript's text item delimiters to linefeed\noutput as text"
                    : "set picked to choose file with prompt " + Quote(request.Title) + "\nposix path of picked";
            var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/osascript", "-e " + Quote(script)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            _active[request.RequestId] = process;
            try
            {
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (_active.ContainsKey(request.RequestId))
                {
                    var paths = process.ExitCode == 0 ? output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList() : new List<string>();
                    completed(new PlatformPathResult(request.RequestId, request.ProjectSessionId, paths.Count > 0, paths, paths.Count == 0 ? "The native dialog was cancelled or failed." : null));
                }
            }
            finally { _active.Remove(request.RequestId); process.Dispose(); }
        }

        public void Cancel(Guid requestId)
        {
            if (_active.TryGetValue(requestId, out var process)) try { if (!process.HasExited) process.Kill(); } catch { }
        }
        public void Dispose() { foreach (var key in _active.Keys.ToList()) Cancel(key); _active.Clear(); }
        private static string Quote(string value) => "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
    }
#endif
}
