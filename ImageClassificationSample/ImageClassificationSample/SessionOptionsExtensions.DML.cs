using Microsoft.ML.OnnxRuntime;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ImageClassificationSample
{
    internal static class SessionOptionsExtensions
    {
        public static void AppendExecutionProvider_DML1(this SessionOptions sessionOptions, IntPtr dmlDevice, IntPtr commandQueue)
		{
			static byte[] StringToZeroTerminatedUtf8(string s)
			{
				int arraySize = Encoding.UTF8.GetByteCount(s);
				byte[] utf8Bytes = new byte[arraySize + 1];
				var bytesWritten = Encoding.UTF8.GetBytes(s, 0, s.Length, utf8Bytes, 0);
				Debug.Assert(arraySize == bytesWritten);
				utf8Bytes[utf8Bytes.Length - 1] = 0;
				return utf8Bytes;
			}

			var utf8ProviderName = StringToZeroTerminatedUtf8("DML");

			// By passing in an explicitly created DML device & queue, the DML execution provider sends work
			// to the desired device. If not used, the DML execution provider will create its own device & queue.
			OrtDmlApi ortDmlApi;
			NativeMethods.OrtGetExecutionProviderApi(utf8ProviderName, NativeMethods.ORT_API_VERSION, out var ortDmlApiPtr);
			ortDmlApi = Marshal.PtrToStructure<OrtDmlApi>(ortDmlApiPtr);

			var ortSessionOptionsAppendExecutionProvider_DML1 = (NativeMethods.DOrtSessionOptionsAppendExecutionProvider_DML1)Marshal.GetDelegateForFunctionPointer(ortDmlApi.SessionOptionsAppendExecutionProvider_DML1, typeof(NativeMethods.DOrtSessionOptionsAppendExecutionProvider_DML1));

			ortSessionOptionsAppendExecutionProvider_DML1(sessionOptions.DangerousGetHandle(), dmlDevice, commandQueue);
		}

		internal static class NativeMethods
		{
			internal const uint ORT_API_VERSION = 14;

			static NativeMethods()
			{
				DOrtGetApi OrtGetApi = (DOrtGetApi)Marshal.GetDelegateForFunctionPointer(OrtGetApiBase().GetApi, typeof(DOrtGetApi));

				api_ = OrtGetApi(ORT_API_VERSION);

				OrtGetExecutionProviderApi = (DOrtGetExecutionProviderApi)Marshal.GetDelegateForFunctionPointer(api_.GetExecutionProviderApi, typeof(DOrtGetExecutionProviderApi));
			}

			static OrtApi api_;

			[UnmanagedFunctionPointer(CallingConvention.Winapi)]
			public delegate ref OrtApi DOrtGetApi(UInt32 version);

			internal const string DllName = "onnxruntime";

			[DllImport(DllName, CharSet = CharSet.Ansi)]
			public static extern ref OrtApiBase OrtGetApiBase();

			[UnmanagedFunctionPointer(CallingConvention.Winapi)]
			public delegate IntPtr /*(OrtStatus*)*/ DOrtGetExecutionProviderApi(byte[] /*(const char*)*/ provider_name,
																				uint /*(uint32_t)*/ version,
																				out IntPtr /* const OrtMemoryInfo** */ provider_api);

			public static DOrtGetExecutionProviderApi OrtGetExecutionProviderApi;

			[UnmanagedFunctionPointer(CallingConvention.Winapi)]
			public delegate IntPtr /*(OrtStatus*)*/ DOrtSessionOptionsAppendExecutionProvider_DML1(IntPtr /*(OrtSessionOptions*) */ options, IntPtr dml_device, IntPtr cmd_queue);
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct OrtDmlApi
		{
			public IntPtr SessionOptionsAppendExecutionProvider_DML;
			public IntPtr SessionOptionsAppendExecutionProvider_DML1;
			public IntPtr CreateGPUAllocationFromD3DResource;
			public IntPtr FreeGPUAllocation;
			public IntPtr GetD3D12ResourceFromAllocation;
			public IntPtr SessionOptionsAppendExecutionProvider_DML2;
		}
	}
}
