/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

#if WINDOWS
using NAudio.Wave;   // ToSampleProvider() lives here as an extension method

namespace DiceAudio
{
    /// <summary>
    /// Float-sample file reader with frame-accurate seeking — a drop-in stand-in
    /// for NAudio's <c>AudioFileReader</c>.
    ///
    /// It exists because of how AudioFileReader picks a decoder for MP3: on any
    /// Windows ≥ Vista it hands the file to Media Foundation, which locates a seek
    /// target by scaling the requested time by the file's *average* bitrate. On a
    /// constant-bitrate file that is exact; on a variable-bitrate one it is a
    /// guess, and a bad one — measured against a straight decode of the same
    /// files, seeks landed up to four seconds late while CurrentTime cheerfully
    /// reported the position that had been asked for. Nothing downstream could
    /// detect the drift: the waveform is built by decoding the file start to
    /// finish, so it was right, and playback through the seek was wrong. A clip
    /// bound dropped exactly on a visible transition then began seconds past it.
    ///
    /// NAudio's own <c>Mp3FileReader</c> does not guess: it walks the frame
    /// headers once at construction and keeps a table of contents, so a seek
    /// resolves to the frame that genuinely holds that sample. The scan costs
    /// about 170 ms on an hour-long file and a handful of ms on a normal track —
    /// it reads headers, not audio. That is the trade this class makes, for MP3
    /// only; every other container keeps the AudioFileReader path it already had.
    /// </summary>
    internal sealed class DAFloatFileReader : NAudio.Wave.ISampleProvider, IDisposable
    {
        // Exactly one of these is non-null.
        private readonly NAudio.Wave.AudioFileReader? _generic;
        private readonly NAudio.Wave.Mp3FileReader? _mp3;
        private readonly NAudio.Wave.ISampleProvider _samples;   // float view of _mp3
        private readonly int _mp3BytesPerSample;

        private float _volume = 1.0f;

        public DAFloatFileReader(string filePath)
        {
            if (filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _mp3 = new NAudio.Wave.Mp3FileReader(filePath);
                }
                catch
                {
                    // Mp3FileReader parses frame headers itself and refuses what it
                    // cannot make sense of — a mis-named file, a truncated download.
                    // Media Foundation is more forgiving, so fall back to it rather
                    // than turning a track that used to play into one that does not.
                    // Such a file seeks as inaccurately as it always did.
                    _mp3 = null;
                }

                if (_mp3 != null)
                {
                    // Mp3FileReader decodes to PCM (16-bit in practice);
                    // ToSampleProvider is the matching byte→float converter and keeps
                    // no state between calls, so seeking underneath it is safe.
                    _samples = _mp3.ToSampleProvider();
                    _mp3BytesPerSample = Math.Max(1, _mp3.WaveFormat.BitsPerSample / 8);
                    return;
                }

                _generic = new NAudio.Wave.AudioFileReader(filePath);
                _samples = _generic;
                _mp3BytesPerSample = 4;
            }
            else
            {
                _generic = new NAudio.Wave.AudioFileReader(filePath);
                _samples = _generic;
                _mp3BytesPerSample = 4;
            }
        }

        public NAudio.Wave.WaveFormat WaveFormat => _samples.WaveFormat;

        /// <summary>
        /// Per-stream sample multiplier (fades, crossfades, layer levels). Applied
        /// here rather than by the caller so both decoder paths behave alike —
        /// AudioFileReader has this property built in, Mp3FileReader does not.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                if (_generic != null) _generic.Volume = value;
            }
        }

        public TimeSpan TotalTime => _generic?.TotalTime ?? _mp3!.TotalTime;

        public TimeSpan CurrentTime
        {
            get => _generic?.CurrentTime ?? _mp3!.CurrentTime;
            set
            {
                if (_generic != null) _generic.CurrentTime = value;
                else _mp3!.CurrentTime = value;
            }
        }

        /// <summary>
        /// Total number of float samples in the file, counting every channel —
        /// the unit <see cref="Read"/> deals in. Derived from the same Length the
        /// duration comes from, so sample index and time stay on one clock.
        /// </summary>
        public long TotalSampleCount =>
            _generic != null ? _generic.Length / 4 : _mp3!.Length / _mp3BytesPerSample;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _samples.Read(buffer, offset, count);
            // AudioFileReader has already scaled its own output.
            if (_generic == null && _volume != 1.0f)
                for (int i = 0; i < read; i++) buffer[offset + i] *= _volume;
            return read;
        }

        public void Dispose()
        {
            _generic?.Dispose();
            _mp3?.Dispose();
        }
    }
}
#endif
