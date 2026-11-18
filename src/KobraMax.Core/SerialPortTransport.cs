using System.IO.Ports;
using System.Text;

namespace KobraMax.Core;

public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            NewLine = "\n",
            Encoding = new UTF8Encoding(false),
            ReadTimeout = 200,
            WriteTimeout = 3000,
            // Wie pyserial: DTR/RTS setzen. Das löst auch den Neustart des Druckers aus.
            DtrEnable = true,
            RtsEnable = true,
        };
    }

    public void Open() => _port.Open();

    public void DiscardInput() => _port.DiscardInBuffer();

    public void WriteLine(string zeile) => _port.Write(zeile + "\n");

    public string? ReadLine()
    {
        try
        {
            return _port.ReadLine();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }
        finally
        {
            _port.Dispose();
        }
    }
}
