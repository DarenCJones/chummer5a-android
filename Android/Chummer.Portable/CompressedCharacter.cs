// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using SevenZip;
namespace Chummer.Portable;
public sealed partial class CharacterDocument
{
    private const int MaxXmlBytes = 64 * 1024 * 1024;
    public static CharacterDocument LoadCompressed(Stream source)
    {
        Span<byte> header = stackalloc byte[13]; source.ReadExactly(header);
        var dictionary = BinaryPrimitives.ReadUInt32LittleEndian(header[1..5]);
        var length = BinaryPrimitives.ReadInt64LittleEndian(header[5..13]);
        if (header[0] >= 225 || dictionary > 128 * 1024 * 1024 || length < 0 || length > MaxXmlBytes)
            throw new InvalidDataException("Unsupported or oversized compressed character.");
        var decoder = new SevenZip.Compression.LZMA.Decoder();
        decoder.SetDecoderProperties(header[..5].ToArray());
        using var output = new LimitedOutput((int)length);
        using var exact = new ExactInput(source);
        decoder.Code(exact, output, -1, length, null);
        if (output.Length != length) throw new InvalidDataException("Incomplete compressed character.");
        output.Position = 0; return Load(output);
    }
    public void SaveCompressed(Stream destination)
    {
        using var xml = new LimitedOutput(MaxXmlBytes); Save(xml); xml.Position = 0;
        var encoder = new SevenZip.Compression.LZMA.Encoder();
        encoder.SetCoderProperties([CoderPropID.DictionarySize, CoderPropID.PosStateBits,
            CoderPropID.LitContextBits, CoderPropID.LitPosBits, CoderPropID.Algorithm,
            CoderPropID.NumFastBytes, CoderPropID.MatchFinder, CoderPropID.EndMarker],
            [1 << 22, 2, 3, 0, 2, 32, "BT2", false]);
        encoder.WriteCoderProperties(destination);
        Span<byte> length = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(length, xml.Length);
        destination.Write(length); encoder.Code(xml, destination, -1, -1, null);
    }
    private sealed class LimitedOutput(int maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        private void Check(int count)
        { if (Position + count > maximum) throw new InvalidDataException("Character exceeds the decompressed size limit."); }
    }
    // Legacy decoder reads assume full buffers. Enforce the contract for file-provider streams.
    private sealed class ExactInput(Stream source) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { source.ReadExactly(buffer.AsSpan(offset, count)); return count; }
        public override int ReadByte() { var value = source.ReadByte(); return value < 0 ? throw new EndOfStreamException() : value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
