using HexToBinLib;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
using Windows.UI.Xaml;
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
        private ConnectionStatus status;
        private ConnectionStatus ConnectionStatus
        {
            get
            {
                return status;
            }
            set
            {
                Set<ConnectionStatus>(ref status, value);
                OnPropertyChanged("Connected");
                OnPropertyChanged("Disconnected");
            }
        }
        private bool Connected
        {
            get
            {
                return status == ConnectionStatus.Connected;
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
        private Device device;
        private Device Device
        {
            get
            {
                return device;
            }
            set
            {
                Set<Device>(ref device, value);
            }
        }
        private StreamSocket socket;
        private IBuffer readBuffer = new Windows.Storage.Streams.Buffer(1024);

        public MainPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            string aqsFilter = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort);
            string[] requestedProperties = new string[] { "System.DeviceInterface.Bluetooth.ProductId",
                "System.DeviceInterface.Bluetooth.VendorId", "System.DeviceInterface.Bluetooth.DeviceAddress" };
            DeviceInformationCollection collection = null;
            Task.Run(async () =>
            {
                collection = await DeviceInformation.FindAllAsync(aqsFilter, requestedProperties);
            }).Wait();
            Devices.Clear();
            foreach (var deviceInfo in collection)
            {
                string name = deviceInfo.Name;
                Task.Run(async () =>
                {
                    BluetoothDevice btDevice = await BluetoothDevice.FromIdAsync(deviceInfo.Id);
                    if (btDevice != null)
                    {
                        name = btDevice.Name;
                    }
                }).Wait();
                Device device = new Device()
                {
                    Name = name,
                    Id = deviceInfo.Id,
                    VendorId = (UInt16)deviceInfo.Properties["System.DeviceInterface.Bluetooth.VendorId"],
                    ProductId = (UInt16)deviceInfo.Properties["System.DeviceInterface.Bluetooth.ProductId"]
                };
                Devices.Add(device);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private void RefreshButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            RefreshDevices();
            Device = null;
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
            socket = new StreamSocket();
            try
            {
                await socket.ConnectAsync(rfcommDeviceService.ConnectionHostName, rfcommDeviceService.ConnectionServiceName);
            }
            catch
            {
                await DeviceNotAvailableContentDialog.ShowAsync();
                socket.Dispose();
                socket = null;
                ConnectionStatus = ConnectionStatus.Disconnected;
                return;
            }
            ConnectionStatus = ConnectionStatus.Connected;
            ReadLoop();
        }

        private async Task ReadLoop()
        {
            if (!Connected || socket == null)
            {
                return;
            }
            IBuffer buffer = null;
            try
            {
                buffer = await socket.InputStream.ReadAsync(readBuffer, 1024, InputStreamOptions.Partial);
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
                    device.Messages.Add(message);
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
            if (!Connected || socket == null)
            {
                return;
            }
            socket.Dispose();
            socket = null;
            ConnectionStatus = ConnectionStatus.Disconnected;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Event handler")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
        private async void SendButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (!Connected || socket == null)
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
                    await socket.OutputStream.WriteAsync(buffer);
                    Message message = new Message(MessageDirection.Send, data);
                    device.Messages.Add(message);
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
