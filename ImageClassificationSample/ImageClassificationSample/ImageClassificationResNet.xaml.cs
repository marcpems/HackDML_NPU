using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DXCore;
using Windows.Win32.AI.MachineLearning.DirectML;
using System.Diagnostics;
using System.Text;

namespace ResNet_Image_ClassificationSample
{
    internal sealed partial class ImageClassificationResNet : Page
    {
        private InferenceSession? _inferenceSession;

        public ImageClassificationResNet()
        {
            this.Unloaded += (s, e) => _inferenceSession?.Dispose();

            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is SampleNavigationParameters sampleParams)
            {
                sampleParams.RequestWaitForCompletion();
                await InitModel(sampleParams.ModelPath);
                sampleParams.NotifyCompletion();

                await ClassifyImage(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "team.jpg"));
            }
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

        private Task InitModel(string modelPath)
        {
            return Task.Run(() =>
            {
                if (_inferenceSession != null)
                {
                    return;
                }

                SessionOptions sessionOptions = new SessionOptions();
                sessionOptions.RegisterOrtExtensions();

                var (dmlDevice, commandQueue) = CreateDmlDeviceAndCommandQueue("NPU");

                var handle = sessionOptions.DangerousGetHandle();

                //sessionOptions.DisableMemPattern();
                sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

                static byte[] StringToZeroTerminatedUtf8(string s)
                {
                    int arraySize = UTF8Encoding.UTF8.GetByteCount(s);
                    byte[] utf8Bytes = new byte[arraySize + 1];
                    var bytesWritten = UTF8Encoding.UTF8.GetBytes(s, 0, s.Length, utf8Bytes, 0);
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

                NativeMethods.OrtSessionOptionsAppendExecutionProvider_DML1 = (NativeMethods.DOrtSessionOptionsAppendExecutionProvider_DML1)Marshal.GetDelegateForFunctionPointer(ortDmlApi.SessionOptionsAppendExecutionProvider_DML1, typeof(NativeMethods.DOrtSessionOptionsAppendExecutionProvider_DML1));

                NativeMethods.OrtSessionOptionsAppendExecutionProvider_DML1(sessionOptions.DangerousGetHandle(), Marshal.GetIUnknownForObject(dmlDevice), Marshal.GetIUnknownForObject(commandQueue));
                //Ort::ThrowOnError(
                //ortDmlApi.SessionOptionsAppendExecutionProvider_DML1(
                //    sessionOptions, 
                //    dmlDevice,
                //    commandQueue
                //);
                //);

                //NativeMethods.OrtSessionOptionsAppendExecutionProvider_DML1(handle, dmlDevice, commandQueue);

                _inferenceSession = new InferenceSession(modelPath, sessionOptions);
            });
        }

        internal static class NativeMethods
        {
            internal const uint ORT_API_VERSION = 14;

            static NativeMethods()
            {
                DOrtGetApi OrtGetApi = (DOrtGetApi)Marshal.GetDelegateForFunctionPointer(OrtGetApiBase().GetApi, typeof(DOrtGetApi));

                api_ = OrtGetApi(ORT_API_VERSION);

                OrtGetExecutionProviderApi = (DOrtGetExecutionProviderApi)Marshal.GetDelegateForFunctionPointer(api_.GetExecutionProviderApi, typeof(DOrtGetExecutionProviderApi));

                //SessionOptionsAppendExecutionProvider = (DSessionOptionsAppendExecutionProvider)Marshal.GetDelegateForFunctionPointer(
                //    api_.SessionOptionsAppendExecutionProvider,
                //    typeof(DSessionOptionsAppendExecutionProvider));
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

            //[UnmanagedFunctionPointer(CallingConvention.Winapi)]
            //            public delegate IntPtr /*(OrtStatus*)*/ DSessionOptionsAppendExecutionProvider(
            //    IntPtr /*(OrtSessionOptions*)*/ options,
            //    byte[] /*(const char*)*/ providerName,
            //    IntPtr[] /*(const char* const *)*/ providerOptionsKeys,
            //    IntPtr[] /*(const char* const *)*/ providerOptionsValues,
            //    UIntPtr /*(size_t)*/ numKeys);

            //public static DSessionOptionsAppendExecutionProvider SessionOptionsAppendExecutionProvider;

            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            public delegate IntPtr /*(OrtStatus*)*/ DOrtSessionOptionsAppendExecutionProvider_DML1(IntPtr /*(OrtSessionOptions*) */ options, IntPtr dml_device, IntPtr cmd_queue);

            public static DOrtSessionOptionsAppendExecutionProvider_DML1 OrtSessionOptionsAppendExecutionProvider_DML1;
        }

        (IDXCoreAdapter, D3D_FEATURE_LEVEL) SelectAdapter(string adapterNameFilter)
        {
            IDXCoreAdapterFactory adapterFactory;
            if (PInvoke.DXCoreCreateAdapterFactory(typeof(IDXCoreAdapterFactory).GUID, out var adapterFactoryObj) != HRESULT.S_OK)
            {
                throw new Exception("Failed to create adapter factory");
            }

            adapterFactory = (IDXCoreAdapterFactory)adapterFactoryObj;

            // First try getting all GENERIC_ML devices, which is the broadest set of adapters 
            // and includes both GPUs and NPUs; however, running this sample on an older build of 
            // Windows may not have drivers that report GENERIC_ML.
            IDXCoreAdapterList adapterList;

            var DXCORE_ADAPTER_ATTRIBUTE_D3D12_GENERIC_ML = new Guid(0xb71b0d41, 0x1088, 0x422f, 0xa2, 0x7c, 0x2, 0x50, 0xb7, 0xd3, 0xa9, 0x88);
            var DXCORE_ADAPTER_ATTRIBUTE_D3D12_CORE_COMPUTE = new Guid(0x248e2800, 0xa793, 0x4724, 0xab, 0xaa, 0x23, 0xa6, 0xde, 0x1b, 0xe0, 0x90);

            adapterFactory.CreateAdapterList([DXCORE_ADAPTER_ATTRIBUTE_D3D12_GENERIC_ML], typeof(IDXCoreAdapterList).GUID, out var adapterListObj);
            adapterList = (IDXCoreAdapterList)adapterListObj;

            D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_1_0_GENERIC;

            // Fall back to CORE_COMPUTE if GENERIC_ML devices are not available. This is a more restricted
            // set of adapters and may filter out some NPUs.
            if (adapterList.GetAdapterCount() == 0)
            {
                featureLevel = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_1_0_CORE;
                adapterFactory.CreateAdapterList(
                    [DXCORE_ADAPTER_ATTRIBUTE_D3D12_CORE_COMPUTE],
                    typeof(IDXCoreAdapterList).GUID,
                    out adapterListObj);
                adapterList = (IDXCoreAdapterList)adapterListObj;
            }

            if (adapterList.GetAdapterCount() == 0)
            {
                throw new Exception("No compatible adapters found.");
            }

            // Sort the adapters by preference, with hardware and high-performance adapters first.
            ReadOnlySpan<DXCoreAdapterPreference> preferences =
                [
                DXCoreAdapterPreference.Hardware,
                DXCoreAdapterPreference.HighPerformance
            ];

            adapterList.Sort(preferences);

            List<IDXCoreAdapter> adapters = new();
            List<string> adapterDescriptions = new();
            int? firstAdapterMatchingNameFilter = null;

            for (uint i = 0; i < adapterList.GetAdapterCount(); i++)
            {
                IDXCoreAdapter adapter;
                adapterList.GetAdapter(i, typeof(IDXCoreAdapter).GUID, out var adapterObj);
                adapter = (IDXCoreAdapter)adapterObj;

                adapter.GetPropertySize(
                    DXCoreAdapterProperty.DriverDescription,
                    out var descriptionSize
                );

                string adapterDescription;
                IntPtr buffer = IntPtr.Zero;
                try
                {
                    buffer = Marshal.AllocHGlobal((int)descriptionSize);
                    unsafe
                    {
                        adapter.GetProperty(
                            DXCoreAdapterProperty.DriverDescription,
                            descriptionSize,
                            buffer.ToPointer()
                        );
                    }
                    adapterDescription = Marshal.PtrToStringAnsi(buffer) ?? string.Empty;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                // Remove trailing null terminator written by DXCore.
                while (!string.IsNullOrEmpty(adapterDescription) && adapterDescription[^1] == '\0')
                {
                    adapterDescription = adapterDescription[..^1];
                }

                adapters.Add(adapter);
                adapterDescriptions.Add(adapterDescription);

                if (!firstAdapterMatchingNameFilter.HasValue &&
                    adapterDescription.Contains(adapterNameFilter))
                {
                    firstAdapterMatchingNameFilter = (int)i;
                    Debug.WriteLine("Adapter[" + i + "]: " + adapterDescription + " (SELECTED)");
                }
                else
                {
                    Debug.WriteLine("Adapter[" + i + "]: " + adapterDescription);
                }
            }

            if (!firstAdapterMatchingNameFilter.HasValue)
            {
                //throw new Exception("No adapters match the provided name filter.");
                Debug.WriteLine("No adapters match the provided name filter. Using the first adapter.");
                firstAdapterMatchingNameFilter = 0;
            }

            return (adapters[firstAdapterMatchingNameFilter.Value], featureLevel);
        }

        (IDMLDevice, ID3D12CommandQueue) CreateDmlDeviceAndCommandQueue(string adapterNameFilter)
        {
            var (adapter, featureLevel) = SelectAdapter(adapterNameFilter);

            ID3D12Device d3d12Device;
            if (PInvoke.D3D12CreateDevice(adapter, featureLevel, typeof(ID3D12Device).GUID, out object d3d12DeviceObj) != HRESULT.S_OK)
            {
                throw new Exception("Failed to create D3D12 device");
            }
            d3d12Device = (ID3D12Device)d3d12DeviceObj;

            IDMLDevice dmlDevice;
            if (PInvoke.DMLCreateDevice(d3d12Device, DML_CREATE_DEVICE_FLAGS.DML_CREATE_DEVICE_FLAG_NONE, typeof(IDMLDevice).GUID, out object dmlDeviceObj) != HRESULT.S_OK)
            {
                throw new Exception("Failed to create DML device");
            }
            dmlDevice = (IDMLDevice)dmlDeviceObj;

            D3D_FEATURE_LEVEL[] featureLevelsRequested =
            [
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_1_0_GENERIC,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_1_0_CORE,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0,
                D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1
            ];

            D3D12_FEATURE_DATA_FEATURE_LEVELS featureLevelSupport;
            unsafe
            {
                fixed(D3D_FEATURE_LEVEL* pFeatureLevelsRequested = featureLevelsRequested)
                {
                    featureLevelSupport = new D3D12_FEATURE_DATA_FEATURE_LEVELS
                    {
                        NumFeatureLevels = (uint)featureLevelsRequested.Length,
                        pFeatureLevelsRequested = pFeatureLevelsRequested
                    };

                    d3d12Device.CheckFeatureSupport(
                        D3D12_FEATURE.D3D12_FEATURE_FEATURE_LEVELS,
                        &featureLevelSupport,
                        (uint)sizeof(D3D12_FEATURE_DATA_FEATURE_LEVELS)
                    );
                }
            }

            // The feature level returned by SelectAdapter is the MINIMUM feature level required for the adapter.
            // However, some adapters may support higher feature levels. For compatibility reasons, this sample
            // uses a direct queue for graphics-capable adapters that support feature levels > CORE.
            var queueType = (featureLevelSupport.MaxSupportedFeatureLevel <= D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_1_0_CORE) ?
                D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_COMPUTE :
                D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT;

            D3D12_COMMAND_QUEUE_DESC queueDesc = new D3D12_COMMAND_QUEUE_DESC
            {
                Type = queueType,
                Priority = (int)D3D12_COMMAND_QUEUE_PRIORITY.D3D12_COMMAND_QUEUE_PRIORITY_NORMAL,
                Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE,
                NodeMask = 0
            };

            ID3D12CommandQueue commandQueue;
            d3d12Device.CreateCommandQueue(queueDesc, typeof(ID3D12CommandQueue).GUID, out object commandQueueObj);
            commandQueue = (ID3D12CommandQueue)commandQueueObj;

            return (dmlDevice, commandQueue);
        }

        private async void UploadImageButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new Window();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            // Create a FileOpenPicker
            var picker = new FileOpenPicker();

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // Set the file type filter
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".jpg");

            picker.ViewMode = PickerViewMode.Thumbnail;

            // Pick a file
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                // Call function to run inference and classify image
                await ClassifyImage(file.Path);
            }
            else
            {
                PredictionsStackPanel.Children.Clear();

                TextBlock feedbackTextBlock = new TextBlock
                {
                    Text = "No image selected",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                PredictionsStackPanel.Children.Add(feedbackTextBlock);
            }
        }

        private async Task ClassifyImage(string filePath)
        {
            if (!Path.Exists(filePath))
            {
                return;
            }

            // Display the selected image
            BitmapImage bitmapImage = new BitmapImage(new Uri(filePath));
            UploadedImage.Source = bitmapImage;

            var predictions = await Task.Run(() =>
            {
                Bitmap image = new Bitmap(filePath);

                // Resize image
                int width = 224;
                int height = 224;
                image = BitmapFunctions.ResizeBitmap(image, width, height);

                // Preprocess image
                Tensor<float> input = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
                input = BitmapFunctions.PreprocessBitmapWithStdDev(image, input);

                // Setup inputs
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor((string?)"data", input)
                };

                // Run inference
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _inferenceSession!.Run(inputs);

                // Postprocess to get softmax vector
                IEnumerable<float> output = results[0].AsEnumerable<float>();
                return ImageNet.GetSoftmax(output);
            });

            // Populates table of results
            ImageNet.DisplayPredictions(predictions, PredictionsStackPanel);
        }
    }
}