namespace KobraMax.Core;

/// <summary>Die Leitung zum Drucker. Echt: <see cref="SerialPortTransport"/>, in Tests: ein simulierter Marlin.</summary>
public interface ISerialTransport : IDisposable
{
    void Open();

    /// <summary>Alles verwerfen, was der Drucker bisher geschickt hat (z. B. Startmeldungen nach dem Reset).</summary>
    void DiscardInput();

    void WriteLine(string zeile);

    /// <summary>Eine Zeile lesen. Gibt null zurück, wenn kurz (ca. 200 ms) nichts kam.</summary>
    string? ReadLine();
}
