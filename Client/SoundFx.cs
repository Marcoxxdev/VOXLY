using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiceClient;

/// <summary>Krótkie dźwięki powiadomień generowane w kodzie (bez plików). Grają na wybranym urządzeniu wyjściowym.</summary>
public static class SoundFx
{
    const int Rate = 44100;

    static readonly Dictionary<string, short[]> Sounds = new()
    {
        ["join"] = Tone((660, 70), (880, 120)),                                  // ktoś wszedł na Twój kanał
        ["leave"] = Tone((880, 70), (587, 130)),                                 // ktoś wyszedł z Twojego kanału
        ["rank"] = Tone((523, 80), (659, 80), (784, 80), (1047, 220)),           // przyznano Ci rangę
        ["lost"] = Tone((392, 150), (330, 150), (262, 300)),                     // utrata połączenia
        ["dm"] = Tone((988, 60), (1319, 120)),                                   // wiadomość prywatna
        ["ring"] = Tone((880, 120), (0, 50), (880, 120), (0, 50), (880, 120)),   // dzwonek rozmowy
    };

    static short[] Tone(params (double freq, double ms)[] notes)
    {
        var data = new List<short>();
        foreach (var (freq, ms) in notes)
        {
            int n = (int)(Rate * ms / 1000);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                // krótkie narastanie i wyciszanie, żeby dźwięk nie "trzaskał"
                double env = Math.Min(1.0, i / (Rate * 0.006)) * Math.Min(1.0, (n - i) / (Rate * 0.03));
                double v = freq <= 0 ? 0 : Math.Sin(2 * Math.PI * freq * t) + 0.25 * Math.Sin(4 * Math.PI * freq * t);
                data.Add((short)(v * env * 0.3 * short.MaxValue / 1.25));
            }
        }
        return data.ToArray();
    }

    public static void Play(string name, int device, float volume)
    {
        if (!Sounds.TryGetValue(name, out var samples)) return;
        try
        {
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            var raw = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(Rate, 16, 1));
            var vol = new VolumeSampleProvider(raw.ToSampleProvider()) { Volume = Math.Clamp(volume, 0f, 1f) };
            var wo = new WaveOutEvent { DeviceNumber = device };
            wo.PlaybackStopped += (_, _) => { wo.Dispose(); raw.Dispose(); };
            wo.Init(vol);
            wo.Play();
        }
        catch { /* brak urządzenia wyjściowego: pomijamy dźwięk */ }
    }
}
