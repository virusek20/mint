using NAudio.Wave;
using System.CommandLine;

namespace MetalMintSolid.Vox;

public static class VoxCommand
{
    public static readonly double[] DecodingTable = [0.0, 0.0, 0.9375, 0, 1.79687, -0.8125, 1.53125, -0.859375, 1.90625, -0.9375];
    public static readonly byte[] AudioStartHeader = [0x01, 0x04, 0x20, 0x00];

    public static Command AddVox(this Command command)
    {
        var voxCommand = new Command("vox", "Manipulate VOX audio files (PC codec)");

        var voxDecodeCommand = new Command("decode", "Decodes audio from a VOX file");
        var voxSourceArgument = new Argument<FileInfo>("source", "Source VOX file");
        var voxTargetArgument = new Argument<FileInfo?>("target", "Target WAV file")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        voxDecodeCommand.AddArgument(voxSourceArgument);
        voxDecodeCommand.AddArgument(voxTargetArgument);
        voxDecodeCommand.SetHandler(DecodeHandler, voxSourceArgument, voxTargetArgument);

        voxCommand.Add(voxDecodeCommand);

        command.AddCommand(voxCommand);
        return voxCommand;
    }

    private static void DecodeHandler(FileInfo source, FileInfo? target)
    {
        if (!source.Exists) throw new FileNotFoundException("Specified VOX file was not found", source.FullName);
        target ??= new FileInfo($"{Path.GetFileNameWithoutExtension(source.FullName)}.wav");

        var data = File.ReadAllBytes(source.FullName).AsSpan();

        // TODO: This can actually vary from file to file
        var waveFormat = new WaveFormat(22050, 16, 1);
        using var writer = new WaveFileWriter(target.FullName, waveFormat);

        double previousSample = 1;
        double previousSample2 = 1;
        double[] decodedAudio = new double[28];

        
        // Find start of audio data, header contains other things like text
        while (true)
        {
            var block = data[..4];
            var audioStart = new byte[] { 0x01, 0x04, 0x20, 0x00 };

            if (block.SequenceEqual(audioStart) == false) data = data.Slice(1);
            else break;
        }

        // Each "block" is 16 bytes, first 2 are some parameters and then 14 bytes of actual audio data
        // Each "block" produces 28 double (converted to 16-bit) samples
        // These are grouped within 8192 byte "sectors" divided by a 4 byte tag
        for (var i = 0; true; i += 16)
        {
            if (data.Length < 16) break;
            if (i % 8192 == 0) data = data.Slice(4); // 4 byte padding between blocks, not sure what it means yet

            int lowerNibble = data[0] & 0xf;
            int upperNibble = data[0] >> 4;
            int secondByte = data[1];

            data = data.Slice(2); // data += 2

            if (secondByte == 7)
            {
                for (int m = 0; m < 14; m++) data[m] = 0; // memset(data, 0, 14);
                break; // End of stream?
            }

            if (lowerNibble == 0)
            {
                // Just silence
                for (int m = 0; m < 14; m++) data[m] = 0; // memset(data, 0, 14);
            }

            for (var j = 0; j < 28; j += 2)
            {
                // I'm gonna be honest, I have no idea how this works
                uint compressedSample = data[0];
                data = data.Slice(1); // data += 1

                uint subSample = (compressedSample & 0xf) << 0xc;
                if ((subSample & 0x8000) != 0) subSample |= 0xffff0000;
                decodedAudio[j + 0] = (int)subSample >> ((byte)lowerNibble & 0x1f);

                subSample = (compressedSample & 0xf0) << 8;
                if ((subSample & 0x8000) != 0) subSample |= 0xffff0000;
                decodedAudio[j + 1] = (int)subSample >> ((byte)lowerNibble & 0x1f);
            }

            for (var j = 0; j < 28; j++)
            {
                decodedAudio[j] =
                     previousSample2 * DecodingTable[upperNibble * 2 + 1] +
                     previousSample  * DecodingTable[upperNibble * 2 + 0] + decodedAudio[j];
                previousSample2 = previousSample;
                previousSample = decodedAudio[j];

                // TODO: Stereo
                // For stereo files the first 4096 bytes of sector and ch1 and other 4096 are ch2
                // i.e. (i % 8096) > 4096 is other channel

                short sample = (short)(int)Math.Round(decodedAudio[j]);
                writer.WriteSamples(new short[] { sample }, 0, 1);
            }
        }
    }
}
