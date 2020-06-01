using HexToBinLib;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UwpBluetoothSerialTool.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace UwpBluetoothSerialTool.Views
{
    enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected
    }

    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        static string AqsFilter { get; set; } = BluetoothDevice.GetDeviceSelector(); // could also
        // set this to RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort) but
        // the DeviceWatcher.Removed does not fire then

        static string VendorIdProperty { get; set; } = "System.DeviceInterface.Bluetooth.VendorId";
        static string ProductIdProperty { get; set; } = "System.DeviceInterface.Bluetooth.ProductId";
        static string AepProtocolIdProperty { get; set; } = "System.Devices.Aep.ProtocolId";
        static Guid BluetoothSerialProtocolId = new Guid("{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}");
        string[] RequestedProperties { get; set; } = new string[]
        {
            AepProtocolIdProperty,
            ProductIdProperty,
            VendorIdProperty,
            "System.DeviceInterface.Bluetooth.DeviceAddress"
        };

        private void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private ConnectionStatus _status;
        private ConnectionStatus ConnectionStatus
        {
            get
            {
                return _status;
            }
            set
            {
                Set<ConnectionStatus>(ref _status, value);
                OnPropertyChanged("Connected");
                OnPropertyChanged("Disconnected");
            }
        }
        private bool Connected
        {
            get
            {
                return _status == ConnectionStatus.Connected;
            }
        }
        private bool Disconnected
        {
            get
            {
                return !Connected;
            }
        }

        private ObservableCollection<Device> Devices = new ObservableCollection<Device>();

        private Device _device;
        private Device Device
        {
            get
            {
                return _device;
            }
            set
            {
                Set<Device>(ref _device, value);
            }
        }

        private StreamSocket _socket;

        private IBuffer _readBuffer = new Windows.Storage.Streams.Buffer(1024);

        public event PropertyChangedEventHandler PropertyChanged;

        public MainPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
            CreateDeviceWatcher();
        }

        private void CreateDeviceWatcher()
        {
            DeviceWatcher watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelector(), RequestedProperties);
            watcher.Added += async (w, deviceInfo) =>
            {
                await AddDeviceAsync(deviceInfo);
            };
            watcher.Removed += async (w, deviceInfoUpdate) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    RemoveDevice(deviceInfoUpdate.Id);
                });
            };
            watcher.Start();
        }

        private void RemoveDevice(string id)
        {
            var result = from d in Devices where d.Id.Equals(id) select d;
            Device device = result.FirstOrDefault();
            if (device != null)
            {
                Devices.Remove(result.FirstOrDefault());
                if (device.Equals(Device))
                {
                    Disconnect();
                    Device = null;
                }
            }
        }

        private void RefreshDevices()
        {
            Devices.Clear();
            Task.Run(async () =>
            {
                DeviceInformationCollection collection =
                    await DeviceInformation.FindAllAsync(AqsFilter, RequestedProperties);
                foreach (var deviceInfo in collection)
                {
                    await AddDeviceAsync(deviceInfo);
                }
            });
        }

        private async Task AddDeviceAsync(DeviceInformation deviceInfo)
        {
            if (!deviceInfo.Properties.ContainsKey(AepProtocolIdProperty))
            {
                return;
            }
            Guid protocolId = (Guid)deviceInfo.Properties[AepProtocolIdProperty];
            if (!protocolId.Equals(BluetoothSerialProtocolId))
            {
                return;
            }
            string name = deviceInfo.Name;
            BluetoothDevice btDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
            if (btDevice != null)
            {
                name = btDevice.Name;
            }
            Device device = new Device()
            {
                Name = name,
                Id = deviceInfo.Id,
                VendorId = (ushort)deviceInfo.Properties[VendorIdProperty],
                ProductId = (ushort)deviceInfo.Properties[ProductIdProperty]
            };
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Devices.Add(device);
            });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private void RefreshButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            RefreshDevices();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private void DevicesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                Device = (Device)e.AddedItems[0];
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private async void ConnectButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }
            ConnectionStatus = ConnectionStatus.Connecting;
            if (Device == null)
            {
                await NoDeviceSelectedContentDialog.ShowAsync();
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            BluetoothDevice bluetoothDevice = await BluetoothDevice.FromIdAsync(Device.Id);
            if (bluetoothDevice == null)
            {
                DeviceAccessStatus accessStatus = DeviceAccessInformation.CreateFromId(Device.Id).CurrentStatus;
                if (accessStatus == DeviceAccessStatus.DeniedByUser)
                {
                    await NoBluetoothContentDialog.ShowAsync();
                }
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            var rfcommServices = await bluetoothDevice.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached);
            if (rfcommServices.Services.Count == 0)
            {
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            RfcommDeviceService rfcommDeviceService = rfcommServices.Services[0];
            if (rfcommDeviceService.DeviceAccessInformation.CurrentStatus != DeviceAccessStatus.Allowed)
            {
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            _socket = new StreamSocket();
            try
            {
                await _socket.ConnectAsync(rfcommDeviceService.ConnectionHostName, rfcommDeviceService.ConnectionServiceName);
            }
            catch
            {
                await DeviceNotAvailableContentDialog.ShowAsync();
                _socket.Dispose();
                _socket = null;
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            ConnectionStatus = ConnectionStatus.Connected;
            ReadLoop();
        }

        private async Task ReadLoop()
        {
            if (!Connected || _socket == null)
            {
                return;
            }
            IBuffer buffer = null;
            try
            {
                buffer = await _socket.InputStream.ReadAsync(_readBuffer, 1024, InputStreamOptions.Partial);
            }
            catch
            {
                return;
            }
            if (buffer.Length != 0)
            {
                var data = new byte[buffer.Length];
                using (DataReader dataReader = DataReader.FromBuffer(buffer))
                {
                    dataReader.ReadBytes(data);
                    string text;
                    Message message = new Message(MessageDirection.Receive, data);
                    Device.Messages.Add(message);
                }
            }
            else
            {
                await ReadFailedContentDialog.ShowAsync();
                Disconnect();
                return;
            }
            ReadLoop();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private void DisconnectButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Disconnect();
        }

        private void Disconnect()
        {
            if (!Connected || _socket == null)
            {
                return;
            }
            _socket.Dispose();
            _socket = null;
            ConnectionStatus = ConnectionStatus.Disconnected;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private async void SendButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (!Connected || _socket == null)
            {
                return;
            }
            byte[] data;
            string text = SendTextBox.Text;
            if (SendTextBoxContainsHex.IsChecked == true)
            {
                MemoryStream memoryStream = new MemoryStream();
                if (HexToBin.DefaultInstance.Convert(new StringReader(text), memoryStream) == -1)
                {
                    await InvalidHexContentDialog.ShowAsync();
                    return;
                }
                data = memoryStream.ToArray();
            }
            else
            {
                string eol = "\n";
                if (EndOfLineDos.IsChecked == true)
                {
                    eol = "\r\n";
                }
                else if (EndOfLineMacOs.IsChecked == true)
                {
                    eol = "\r";
                }
                if (!Environment.NewLine.Equals(eol))
                {
                    text.Replace(Environment.NewLine, eol);
                }
                data = Encoding.UTF8.GetBytes(text);
            }
            using (DataWriter dataWriter = new DataWriter())
            {
                dataWriter.WriteBytes(data);
                IBuffer buffer = dataWriter.DetachBuffer();
                try
                {
                    await _socket.OutputStream.WriteAsync(buffer);
                    Message message = new Message(MessageDirection.Send, data);
                    Device.Messages.Add(message);
                }
                catch
                {
                    await SendFailedContentDialog.ShowAsync();
                    Disconnect();
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private void ClearButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Device.Messages.Clear();
        }

        private void SettingsAppBarButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private static bool IsControlKeyPressed()
        {
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            return (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        private void MessagesListView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.C && IsControlKeyPressed())
            {
                if (MessagesListView.SelectedItem != null)
                {
                    Message selectedMessage = (Message)MessagesListView.SelectedItem;
                    DataPackage dataPackage = new DataPackage();
                    dataPackage.SetText(selectedMessage.Hexadecimal);
                    Clipboard.SetContent(dataPackage);
                }
            }
        }
    }
}
