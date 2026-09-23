using System.Buffers.Binary;
using System.Text;

namespace Monica.Core.Passkeys;

internal sealed class CborBuilder
{
    private readonly List<byte> _bytes = [];

    public CborBuilder WriteInteger(long value) =>
        WriteHead(value < 0 ? (byte)1 : (byte)0, value < 0 ? -1 - value : value);

    public CborBuilder WriteByteString(ReadOnlySpan<byte> value)
    {
        WriteHead(2, value.Length);
        foreach (var item in value)
        {
            _bytes.Add(item);
        }

        return this;
    }

    public CborBuilder WriteTextString(string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        WriteHead(3, encoded.Length);
        _bytes.AddRange(encoded);
        return this;
    }

    public CborBuilder WriteStartMap(int count) => WriteHead(5, count);

    public CborBuilder WriteStartArray(int count) => WriteHead(4, count);

    public CborBuilder WriteRaw(byte[] encoded)
    {
        _bytes.AddRange(encoded);
        return this;
    }

    public byte[] Build() => [.. _bytes];

    private CborBuilder WriteHead(byte major, long argument)
    {
        var head = (byte)(major << 5);
        switch (argument)
        {
            case < 24:
                _bytes.Add((byte)(head | (byte)argument));
                break;
            case <= byte.MaxValue:
                _bytes.Add((byte)(head | 24));
                _bytes.Add((byte)argument);
                break;
            case <= ushort.MaxValue:
                _bytes.Add((byte)(head | 25));
                Span<byte> uint16 = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(uint16, (ushort)argument);
                _bytes.AddRange(uint16.ToArray());
                break;
            case <= uint.MaxValue:
                _bytes.Add((byte)(head | 26));
                Span<byte> uint32 = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(uint32, (uint)argument);
                _bytes.AddRange(uint32.ToArray());
                break;
            default:
                _bytes.Add((byte)(head | 27));
                Span<byte> uint64 = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(uint64, argument);
                _bytes.AddRange(uint64.ToArray());
                break;
        }

        return this;
    }
}

internal sealed class CborReader(byte[] payload)
{
    private readonly byte[] _payload = payload;
    private int _position;

    public int Remaining => _payload.Length - _position;

    public int Position => _position;

    public byte[] ReadToEnd()
    {
        var rest = _payload.AsSpan(_position).ToArray();
        _position = _payload.Length;
        return rest;
    }

    public (byte Major, long Argument) ReadHead()
    {
        if (_position >= _payload.Length)
        {
            throw new FormatException("CBOR payload ended before a head byte.");
        }

        var initial = _payload[_position++];
        var major = (byte)(initial >> 5);
        var low = (byte)(initial & 0x1F);
        if (low < 24)
        {
            return (major, low);
        }

        var width = low switch
        {
            24 => 1,
            25 => 2,
            26 => 4,
            27 => 8,
            _ => throw new FormatException("CBOR uses an unsupported additional-information value.")
        };

        if (Remaining < width)
        {
            throw new FormatException("CBOR payload ended inside a head.");
        }

        var view = new ReadOnlySpan<byte>(_payload, _position, width);
        _position += width;
        var argument = width switch
        {
            1 => (long)view[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(view),
            4 => BinaryPrimitives.ReadUInt32BigEndian(view),
            _ => BinaryPrimitives.ReadInt64BigEndian(view)
        };

        if (argument < 0)
        {
            throw new FormatException("CBOR integers above 2^63 are not supported.");
        }

        return (major, argument);
    }

    public long ReadInteger()
    {
        var (major, argument) = ReadHead();
        return major switch
        {
            0 => argument,
            1 => -1 - argument,
            _ => throw new FormatException($"Expected a CBOR integer but found major type {major}.")
        };
    }

    public byte[] ReadByteString()
    {
        var (major, length) = ReadHead();
        if (major != 2)
        {
            throw new FormatException($"Expected a CBOR byte string but found major type {major}.");
        }

        return ReadBytes((int)length);
    }

    public string ReadTextString()
    {
        var (major, length) = ReadHead();
        if (major != 3)
        {
            throw new FormatException($"Expected a CBOR text string but found major type {major}.");
        }

        return Encoding.UTF8.GetString(ReadBytes((int)length));
    }

    public int ReadStartMap()
    {
        var (major, count) = ReadHead();
        if (major != 5)
        {
            throw new FormatException($"Expected a CBOR map but found major type {major}.");
        }

        return (int)count;
    }

    public CborValue ReadValue()
    {
        var (major, argument) = ReadHead();
        return major switch
        {
            0 => CborValue.OfInteger(argument),
            1 => CborValue.OfInteger(-1 - argument),
            2 => CborValue.OfBytes(ReadBytes((int)argument)),
            3 => CborValue.OfText(Encoding.UTF8.GetString(ReadBytes((int)argument))),
            _ => throw new FormatException($"CBOR major type {major} is not a scalar value.")
        };
    }

    public void Skip()
    {
        var (major, argument) = ReadHead();
        switch (major)
        {
            case 0:
            case 1:
            case 7:
                return;
            case 2:
            case 3:
                ReadBytes((int)argument);
                return;
            case 4:
                for (var index = 0; index < argument; index++)
                {
                    Skip();
                }

                return;
            case 5:
                for (var index = 0; index < argument * 2; index++)
                {
                    Skip();
                }

                return;
            default:
                throw new FormatException($"CBOR major type {major} is not supported.");
        }
    }

    private byte[] ReadBytes(int length)
    {
        if (length < 0 || Remaining < length)
        {
            throw new FormatException("CBOR payload ended inside a string.");
        }

        var result = _payload.AsSpan(_position, length).ToArray();
        _position += length;
        return result;
    }
}

internal readonly record struct CborValue(long Integer, byte[]? Bytes, string? Text)
{
    public static CborValue OfInteger(long value) => new(value, null, null);

    public static CborValue OfBytes(byte[] value) => new(0, value, null);

    public static CborValue OfText(string value) => new(0, null, value);

    public int AsInteger() => checked((int)Integer);
}
