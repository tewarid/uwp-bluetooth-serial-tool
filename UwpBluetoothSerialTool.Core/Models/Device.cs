using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace UwpBluetoothSerialTool.Core.Models
{
    public class Device
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public UInt16 VendorId { get; set; }
        public UInt16 ProductId { get; set; }
        public ObservableCollection<Message> Messages { get; private set; } = new ObservableCollection<Message>();
        public object Address { get; set; }

        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            if (obj is Device && obj != null)
            {
                return Id.Equals(((Device)obj).Id);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
