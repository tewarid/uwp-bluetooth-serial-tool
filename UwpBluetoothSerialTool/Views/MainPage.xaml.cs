using HexToBinLib;
using Microsoft.Toolkit.Uwp.Extensions;
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
        private static string AqsFilter { get; set; } = BluetoothDevice.GetDeviceSelector();
        // could also set this to RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort)
        // but then the DeviceWatcher.Removed event does not fire

        private static string AepProtocolIdProperty { get; set; } = "System.Devices.Aep.ProtocolId";
        private static string VendorIdProperty { get; set; } = "System.DeviceInterface.Bluetooth.VendorId";
        private static string ProductIdProperty { get; set; } = "System.DeviceInterface.Bluetooth.ProductId";
        private static string BluetoothAddressProperty { get; set; } = "System.DeviceInterface.Bluetooth.DeviceAddress";
        private static Guid BluetoothSerialProtocolUuid = new Guid("{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}");
        private string[] RequestedProperties { get; set; } = new string[]
        {
            AepProtocolIdProperty,
            ProductIdProperty,
            VendorIdProperty,
            BluetoothAddressProperty
        };

        private DeviceWatcher _watcher;

        private void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
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
            get { return _status; }
            set
            {
                Set<ConnectionStatus>(ref _status, value);
                OnPropertyChanged("Connected");
                OnPropertyChanged("Disconnected");
            }
        }
        private bool Connected
        {
            get { return _status == ConnectionStatus.Connected; }
        }
        private bool Disconnected
        {
            get { return !Connected; }
        }

        private ObservableCollection<Device> Devices = new ObservableCollection<Device>();

        private Device _device;
        private Device Device
        {
            get { return _device; }
            set { Set<Device>(ref _device, value); }
        }

        private string _deviceToolTipText;
        private string DeviceToolTipText
        {
            get { return _deviceToolTipText; }
            set { Set<string>(ref _deviceToolTipText, value); }
        }

        private StreamSocket _socket;

        private IBuffer _readBuffer = new Windows.Storage.Streams.Buffer(1024);

        private RfcommServiceProvider _rfcommProvider;
        private StreamSocketListener _socketListener;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
            CreateDeviceWatcher();
        }

        private void CreateDeviceWatcher()
        {
            _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelector());
            _watcher.Added += async (w, deviceInfo) =>
            {
                await AddDeviceAsync(deviceInfo);
            };
            _watcher.Removed += async (w, deviceInfoUpdate) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    RemoveDevice(deviceInfoUpdate.Id);
                });
            };
            _watcher.Start();
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
                DeviceInformationCollection collection = await DeviceInformation.FindAllAsync(AqsFilter);
                foreach (var deviceInfo in collection)
                {
                    await AddDeviceAsync(deviceInfo);
                }
            });
        }

        private async Task AddDeviceAsync(DeviceInformation deviceInfo)
        {
            // workaround for https://github.com/MicrosoftDocs/windows-uwp/issues/2646
            deviceInfo = await DeviceInformation.CreateFromIdAsync(deviceInfo.Id, RequestedProperties);
            if (deviceInfo == null)
            {
                return;
            }
            deviceInfo.Properties.TryGetValue(AepProtocolIdProperty, out object protocolId);
            if (!BluetoothSerialProtocolUuid.Equals(protocolId))
            {
                return;
            }
            deviceInfo.Properties.TryGetValue(VendorIdProperty, out object vendorId);
            deviceInfo.Properties.TryGetValue(ProductIdProperty, out object productId);
            deviceInfo.Properties.TryGetValue(BluetoothAddressProperty, out object bluetoothAddress);
            Device device = new Device()
            {
                Name = deviceInfo.Name,
                Id = deviceInfo.Id,
                VendorId = (ushort)(vendorId ?? (ushort)0),
                ProductId = (ushort)(productId ?? (ushort)0),
                Address = bluetoothAddress
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
                Device device = (Device)e.AddedItems[0];
                SetDeviceToolTipText(device);
                Device = device;
            }
        }

        private void SetDeviceToolTipText(Device device)
        {
            string deviceIdLabel = "DeviceId".GetLocalized();
            string vendorIdLabel = "VendorId".GetLocalized();
            string productIdLabel = "ProductId".GetLocalized();
            string addressLabel = "BluetoothAddress".GetLocalized();
            string nameLabel = "DeviceName".GetLocalized();
            DeviceToolTipText = $"{deviceIdLabel}: {device.Id}{Environment.NewLine}{vendorIdLabel}: 0x{device.VendorId:x4}{Environment.NewLine}{productIdLabel}: 0x{device.ProductId:x4}{Environment.NewLine}{addressLabel}: {device.Address}{Environment.NewLine}{nameLabel}: {device.Name}";
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
            BluetoothDevice bluetoothDevice;
            try
            {
                bluetoothDevice = await BluetoothDevice.FromIdAsync(Device.Id);
            }
            catch
            {
                await NoBluetoothContentDialog.ShowAsync();
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
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
            _ = Task.Run(ReadLoop).ConfigureAwait(false);
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
                Disconnect();
                return;
            }
            if (buffer.Length != 0)
            {
                var data = new byte[buffer.Length];
                using (DataReader dataReader = DataReader.FromBuffer(buffer))
                {
                    dataReader.ReadBytes(data);
                    Message message = new Message(MessageDirection.Receive, data);
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        Device.Messages.Add(message);
                    });
                }
            }
            else
            {
                await ReadFailedContentDialog.ShowAsync();
                Disconnect();
                return;
            }
            _ = Task.Run(ReadLoop).ConfigureAwait(false);
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
            ConnectionStatus = ConnectionStatus.Disconnected;
            var _socketCopy = _socket;
            _socket = null;
            _socketCopy.Dispose();
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

        private async void Listen_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var toggleButton = (AppBarToggleButton)sender;
            if (toggleButton.IsChecked == false)
            {
                if (_rfcommProvider != null)
                {
                    _rfcommProvider.StopAdvertising();
                    _rfcommProvider = null;
                }
                if (_socketListener != null)
                {
                    _socketListener.Dispose();
                    _socketListener = null;
                }
                return;
            }
            try
            {
                _rfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.SerialPort);
                _socketListener = new StreamSocketListener();
                _socketListener.ConnectionReceived += OnConnectionReceived;
                await _socketListener.BindServiceNameAsync(_rfcommProvider.ServiceId.AsString(),
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
                _rfcommProvider.StartAdvertising(_socketListener, true);
            }
            catch
            {
                await NoBluetoothContentDialog.ShowAsync();
                toggleButton.IsChecked = false;
                return;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
            if (_socketListener == null)
            {
                return;
            }
            _socket = args.Socket;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var remoteDevice = await BluetoothDevice.FromHostNameAsync(_socket.Information.RemoteHostName);
                Device device = (from d in Devices where d.Id.Equals(remoteDevice.DeviceId) select d).FirstOrDefault();
                if (device != null)
                {
                    DevicesComboBox.SelectedItem = device;
                }
                else
                {
                    device = new Device()
                    {
                        Name = remoteDevice.Name,
                        Id = remoteDevice.DeviceId,
                        Address = remoteDevice.HostName
                    };
                }
                SetDeviceToolTipText(device);
                Device = device;
                ConnectionStatus = ConnectionStatus.Connected;
                _ = Task.Run(ReadLoop).ConfigureAwait(false);
            });
        }
    }
}
