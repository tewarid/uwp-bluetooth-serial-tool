using System;
using System.Text;

namespace UwpBluetoothSerialTool.Core.Models
{
    public enum MessageDirection
    {
        Send,
        Receive
    };

    public class Message
    {
        public MessageDirection Direction { get; private set; }
        public byte[] Data { get; private set; }
        public string Text { get; private set; }
        public string Hexadecimal { get; private set; }
        public DateTime DateCreated { get; private set; } = DateTime.Now;

        public Message(MessageDirection direction, byte[] data)
        {
            Direction = direction;
            Data = data;
            Text = Encoding.UTF8.GetString(data);
            Hexadecimal = BinaryToHex(data);
        }

        public static string BinaryToHex(byte[] data)
        {
            StringBuilder sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(string.Format("{0:X2} ", data[i]));
            }
            return sb.ToString();
        }
    }
}
