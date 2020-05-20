using System;
using UwpBluetoothSerialTool.Core.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace UwpBluetoothSerialTool.Views
{
    public class MessageDirectionToAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if ((MessageDirection)value == MessageDirection.Send)
            {
                return HorizontalAlignment.Right;
            }
            else
            {
                return HorizontalAlignment.Left;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
