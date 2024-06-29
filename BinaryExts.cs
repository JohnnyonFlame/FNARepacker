namespace System.IO
{
    public static class BinaryReaderExt
    {
        public static int Read7BitEncodedIntExt(this BinaryReader br)
        {
            sbyte b;
            int r = -7, v = 0;
            do
                v |= ((b = br.ReadSByte()) & 0x7F) << (r += 7);
            while (b < 0);
            return v;
        }
    }

    public static class BinaryWriterExt
    {
        // From referencesource/mscorlib/system/io/binarywriter.cs
        public static void Write7BitEncodedIntExt(this BinaryWriter bw, int value)
        {
            // Write out an int 7 bits at a time.  The high bit of the byte,
            // when on, tells reader to continue reading more bytes.
            uint v = (uint) value;   // support negative numbers
            while (v >= 0x80) {
                bw.Write((byte) (v | 0x80));
                v >>= 7;
            }
            bw.Write((byte)v);
        }
    }
}