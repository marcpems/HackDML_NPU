using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DXCore;
using Windows.Win32.AI.MachineLearning.DirectML;

namespace ImageClassificationSample
{
    internal class DMLHelpers
    {
        private static (IDXCoreAdapter, D3D_FEATURE_LEVEL) SelectAdapter(string adapterNameFilter)
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

        public static (IDMLDevice, ID3D12CommandQueue) CreateDmlDeviceAndCommandQueue(string adapterNameFilter)
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
                fixed (D3D_FEATURE_LEVEL* pFeatureLevelsRequested = featureLevelsRequested)
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
    }
}
