using NAudio.Wave;

namespace VoiceClient;

// Przetwarzanie dźwięku z mikrofonu (jak w TeamSpeaku): usuwanie szumów, kasowanie echa, redukcja echa.
// Wszystko liczone na 48 kHz, mono, próbki float -1..1. Bez zależności od WPF.

/// <summary>FFT radix-2 (rozmiar będący potęgą dwójki), w miejscu. Odwrotna jest skalowana przez 1/N.</summary>
sealed class FftPlan
{
    readonly int _n;
    readonly float[] _cos, _sin;
    readonly int[] _rev;

    public FftPlan(int n)
    {
        _n = n;
        _cos = new float[n / 2]; _sin = new float[n / 2];
        for (int i = 0; i < n / 2; i++)
        {
            double a = -2 * Math.PI * i / n;
            _cos[i] = (float)Math.Cos(a); _sin[i] = (float)Math.Sin(a);
        }
        _rev = new int[n];
        int bits = (int)Math.Round(Math.Log2(n));
        for (int i = 0; i < n; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _rev[i] = r;
        }
    }

    public void Transform(float[] re, float[] im, bool inverse)
    {
        int n = _n;
        for (int i = 0; i < n; i++)
        {
            int j = _rev[i];
            if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1, step = n / len;
            for (int i = 0; i < n; i += len)
                for (int j = 0; j < half; j++)
                {
                    float wr = _cos[j * step], wi = inverse ? -_sin[j * step] : _sin[j * step];
                    int a = i + j, b = a + half;
                    float xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                }
        }
        if (inverse)
        {
            float s = 1f / n;
            for (int i = 0; i < n; i++) { re[i] *= s; im[i] *= s; }
        }
    }
}

/// <summary>
/// Usuwanie szumów w tle: odejmowanie widmowe z filtrem Wienera (decision-directed).
/// Okno 512 próbek, krok 256 (opóźnienie ~5 ms). Poziom szumu jest śledzony osobno dla każdego pasma
/// i uczy się go w przerwach w mowie.
/// </summary>
public sealed class NoiseSuppressor
{
    const int N = 512, H = 256, K = N / 2 + 1;
    const float Alpha = 0.92f;          // wygładzanie decision-directed
    const float NoiseBias = 2.2f;       // kompensacja niedoszacowania szumu przez śledzenie minimum
    const float MinGain = 0.08f;        // maks. tłumienie ok. -20 dB
    const float RiseRate = 1.0012f;     // jak szybko estymata szumu może rosnąć (na ramkę)

    readonly FftPlan _fft = new(N);
    readonly float[] _win = new float[N];
    readonly float[] _re = new float[N], _im = new float[N];
    readonly float[] _noise = new float[K], _smooth = new float[K], _clean = new float[K], _gain = new float[K], _g2 = new float[K];
    readonly float[] _prevIn = new float[H], _cur = new float[H], _olap = new float[H], _out = new float[H];
    int _idx, _frames;

    public NoiseSuppressor()
    {
        for (int i = 0; i < N; i++) _win[i] = (float)Math.Sqrt(0.5 * (1 - Math.Cos(2 * Math.PI * i / N)));
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_noise); Array.Clear(_smooth); Array.Clear(_clean);
        Array.Fill(_gain, 1f);
        Array.Clear(_prevIn); Array.Clear(_cur); Array.Clear(_olap); Array.Clear(_out);
        _idx = 0; _frames = 0;
    }

    /// <summary>Przetwarza próbki w miejscu. Wprowadza stałe opóźnienie 256 próbek.</summary>
    public void Process(float[] x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            _cur[_idx] = x[i];
            x[i] = _out[_idx];
            if (++_idx == H) { _idx = 0; Block(); }
        }
    }

    void Block()
    {
        for (int i = 0; i < H; i++) { _re[i] = _prevIn[i] * _win[i]; _re[H + i] = _cur[i] * _win[H + i]; }
        Array.Clear(_im);
        Array.Copy(_cur, _prevIn, H);
        _fft.Transform(_re, _im, false);

        for (int k = 0; k < K; k++)
        {
            float p = _re[k] * _re[k] + _im[k] * _im[k];
            if (_frames == 0) { _smooth[k] = p; _noise[k] = p; }
            _smooth[k] = 0.75f * _smooth[k] + 0.25f * p;
            if (_frames < 12) _noise[k] = (_noise[k] * _frames + _smooth[k]) / (_frames + 1);   // pierwsze ramki: zakładamy ciszę
            else if (_smooth[k] < _noise[k]) _noise[k] = _smooth[k];
            else _noise[k] *= RiseRate;
            float nz = _noise[k] * NoiseBias + 1e-9f;
            float post = p / nz;
            float prio = Alpha * _clean[k] / nz + (1 - Alpha) * Math.Max(post - 1f, 0f);
            float g = prio / (1f + prio);
            _g2[k] = g < MinGain ? MinGain : g;
        }
        _frames++;

        // wygładzenie po częstotliwości (mniej "metalicznych" artefaktów)
        for (int k = 0; k < K; k++)
        {
            float a = _g2[Math.Max(k - 1, 0)], b = _g2[k], c = _g2[Math.Min(k + 1, K - 1)];
            float g = 0.25f * a + 0.5f * b + 0.25f * c;
            _gain[k] = g;
            float p = _re[k] * _re[k] + _im[k] * _im[k];
            _clean[k] = g * g * p;
            _re[k] *= g; _im[k] *= g;
            if (k > 0 && k < N / 2) { _re[N - k] *= g; _im[N - k] *= g; }
        }

        _fft.Transform(_re, _im, true);
        for (int i = 0; i < H; i++)
        {
            _out[i] = _olap[i] + _re[i] * _win[i];
            _olap[i] = _re[H + i] * _win[H + i];
        }
    }
}

/// <summary>
/// Kasowanie echa (AEC): adaptacyjny filtr w dziedzinie częstotliwości (partycjonowany NLMS, overlap-save).
/// Sygnałem odniesienia jest dokładnie to, co leci do głośników (<see cref="PushFar"/>), a filtr uczy się, jak
/// to brzmi po drodze głośnik -> pokój -> mikrofon, i odejmuje od mikrofonu. Pokrywa echo do ~250 ms.
/// Podczas rozmowy obu stron (double-talk) adaptacja jest spowalniana, żeby nie psuć własnego głosu.
/// </summary>
public sealed class EchoCanceller
{
    public const int B = 512, N = 1024, K = N / 2 + 1, P = 24;
    const int RingSize = 1 << 17, RingMask = RingSize - 1;
    const float MuFast = 0.8f,      // do czasu pierwszej zbieżności filtra
                MuNormal = 0.4f,    // po zbieżności (mniejsze zaburzenia od własnego głosu)
                MuSlow = 0.03f,     // rozmowa obu stron: prawie zamrożone
                RegRel = 0.05f, Delta = 1e-6f;

    readonly FftPlan _fft = new(N);
    readonly float[][] _xr = new float[P][], _xi = new float[P][], _wr = new float[P][], _wi = new float[P][];
    int _head;
    readonly float[] _ring = new float[RingSize];
    long _farW;
    readonly float[] _farPrev = new float[B], _f = new float[B];
    readonly float[] _tr = new float[N], _ti = new float[N], _yr = new float[K], _yi = new float[K], _s = new float[K];
    readonly float[] _cur = new float[B], _out = new float[B], _e = new float[B];
    int _idx, _cursor;
    long _blockStart;
    float _edl, _eel;
    bool _converged;

    public EchoCanceller()
    {
        for (int p = 0; p < P; p++) { _xr[p] = new float[K]; _xi[p] = new float[K]; _wr[p] = new float[K]; _wi[p] = new float[K]; }
    }

    public void Reset()
    {
        for (int p = 0; p < P; p++) { Array.Clear(_xr[p]); Array.Clear(_xi[p]); Array.Clear(_wr[p]); Array.Clear(_wi[p]); }
        Array.Clear(_farPrev); Array.Clear(_cur); Array.Clear(_out);
        _idx = 0; _blockStart = 0; _head = 0; _cursor = 0; _edl = _eel = 0; _converged = false;
        Volatile.Write(ref _farW, 0);
    }

    /// <summary>Wątek odtwarzania: próbki, które właśnie trafiły do głośników (sygnał odniesienia).</summary>
    public void PushFar(float[] buf, int offset, int count)
    {
        long w = Volatile.Read(ref _farW);
        for (int i = 0; i < count; i++) _ring[(int)((w + i) & RingMask)] = buf[offset + i];
        Volatile.Write(ref _farW, w + count);
    }

    /// <summary>Wątek mikrofonu: przetwarza próbki w miejscu. Stałe opóźnienie 512 próbek (~11 ms).</summary>
    public void Process(float[] x, int n)
    {
        for (int i = 0; i < n; i++)
        {
            _cur[_idx] = x[i];
            x[i] = _out[_idx];
            if (++_idx == B) { _idx = 0; Block(); }
        }
    }

    void Block()
    {
        long s = _blockStart; _blockStart += B;
        long fw = Volatile.Read(ref _farW);
        bool haveFar = fw >= s + B && fw - s <= RingSize - B;
        double farE = 0;
        if (haveFar)
            for (int i = 0; i < B; i++) { float v = _ring[(int)((s + i) & RingMask)]; _f[i] = v; farE += v * v; }
        else Array.Clear(_f);
        farE /= B;

        // widmo bieżącego bloku odniesienia [poprzedni | bieżący]
        _head = (_head + P - 1) % P;
        for (int i = 0; i < B; i++) { _tr[i] = _farPrev[i]; _tr[B + i] = _f[i]; }
        Array.Clear(_ti);
        Array.Copy(_f, _farPrev, B);
        _fft.Transform(_tr, _ti, false);
        Array.Copy(_tr, _xr[_head], K); Array.Copy(_ti, _xi[_head], K);

        // estymata echa: Y = sum W[p] * X[p]
        Array.Clear(_yr); Array.Clear(_yi);
        for (int p = 0; p < P; p++)
        {
            int slot = (_head + p) % P;
            float[] wr = _wr[p], wi = _wi[p], xr = _xr[slot], xi = _xi[slot];
            for (int k = 0; k < K; k++)
            {
                _yr[k] += wr[k] * xr[k] - wi[k] * xi[k];
                _yi[k] += wr[k] * xi[k] + wi[k] * xr[k];
            }
        }
        Array.Copy(_yr, _tr, K); Array.Copy(_yi, _ti, K);
        for (int k = 1; k < N / 2; k++) { _tr[N - k] = _yr[k]; _ti[N - k] = -_yi[k]; }
        _fft.Transform(_tr, _ti, true);

        double ed = 0, ee = 0;
        for (int i = 0; i < B; i++)
        {
            float d = _cur[i], e = d - _tr[B + i];
            _e[i] = e; ed += d * d; ee += e * e;
        }
        ed /= B; ee /= B;
        Array.Copy(_e, _out, B);

        if (!haveFar || farE < 1e-6) return;     // brak sygnału odniesienia: nic nie uczymy

        // sterowanie adaptacją: po zbieżności nagły spadek tłumienia = rozmowa obu stron
        float mu = _converged ? MuNormal : MuFast;
        bool dt = false;
        if (_converged && ee > 0.5 * ed) { mu = MuSlow; dt = true; }
        if (!dt)
        {
            _edl = _edl == 0 ? (float)ed : 0.9f * _edl + 0.1f * (float)ed;
            _eel = _eel == 0 ? (float)ee : 0.9f * _eel + 0.1f * (float)ee;
            _converged = _edl > 1e-8f && _edl > 4f * _eel;
        }

        // gradient: W[p] += mu * conj(X[p]) * E / sum|X|^2
        float mean = 0;
        for (int k = 0; k < K; k++)
        {
            float sum = 0;
            for (int p = 0; p < P; p++) sum += _xr[p][k] * _xr[p][k] + _xi[p][k] * _xi[p][k];
            _s[k] = sum; mean += sum;
        }
        // regularyzacja względem średniej mocy: pasma niemal bez energii (między harmonicznymi głosu) nie mogą wprowadzać szumu adaptacji
        float reg = Delta + RegRel * mean / K;
        for (int k = 0; k < K; k++) _s[k] = mu / (_s[k] + reg);
        Array.Clear(_tr); Array.Clear(_ti);
        for (int i = 0; i < B; i++) _tr[B + i] = _e[i];
        _fft.Transform(_tr, _ti, false);
        for (int p = 0; p < P; p++)
        {
            int slot = (_head + p) % P;
            float[] wr = _wr[p], wi = _wi[p], xr = _xr[slot], xi = _xi[slot];
            for (int k = 0; k < K; k++)
            {
                float gr = xr[k] * _tr[k] + xi[k] * _ti[k], gi = xr[k] * _ti[k] - xi[k] * _tr[k];
                wr[k] += _s[k] * gr; wi[k] += _s[k] * gi;
            }
        }

        // ograniczenie przyczynowości jednej partycji na blok (cyklicznie): odrzuca drugą połowę odpowiedzi impulsowej
        int c = _cursor; _cursor = (_cursor + 1) % P;
        Array.Copy(_wr[c], _tr, K); Array.Copy(_wi[c], _ti, K);
        for (int k = 1; k < N / 2; k++) { _tr[N - k] = _wr[c][k]; _ti[N - k] = -_wi[c][k]; }
        _fft.Transform(_tr, _ti, true);
        Array.Clear(_tr, B, B); Array.Clear(_ti);
        _fft.Transform(_tr, _ti, false);
        Array.Copy(_tr, _wr[c], K); Array.Copy(_ti, _wi[c], K);
    }
}

/// <summary>
/// Redukcja echa (jak w TeamSpeaku): gdy mówisz, głośność rozmówców jest zmniejszana o zadaną liczbę dB,
/// więc mniej dźwięku z głośników wraca do mikrofonu. Płynnie: szybkie ściszanie, wolniejsze wracanie.
/// </summary>
public sealed class EchoDuck : ISampleProvider
{
    readonly ISampleProvider _src;
    float _gain = 1f;
    volatile float _target = 1f;
    public EchoDuck(ISampleProvider src) => _src = src;
    public WaveFormat WaveFormat => _src.WaveFormat;

    /// <summary>Wywoływane z wątku mikrofonu: czy właśnie nadajesz i o ile dB ściszyć rozmówców (0 = bez zmian).</summary>
    public void Set(bool talking, int reductionDb) =>
        _target = talking && reductionDb > 0 ? (float)Math.Pow(10, -reductionDb / 20.0) : 1f;

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        float t = _target;
        if (_gain == 1f && t == 1f) return n;
        for (int i = 0; i < n; i++)
        {
            _gain += (t - _gain) * (t < _gain ? 0.002f : 0.00008f);   // atak ~10 ms, powrót ~250 ms
            buffer[offset + i] *= _gain;
        }
        if (Math.Abs(_gain - 1f) < 1e-4f && t == 1f) _gain = 1f;
        return n;
    }
}

/// <summary>Podsłuch tego, co faktycznie idzie do głośników (sygnał odniesienia dla kasowania echa).</summary>
public sealed class FarTap : ISampleProvider
{
    readonly ISampleProvider _src;
    public volatile EchoCanceller? Target;
    public FarTap(ISampleProvider src) => _src = src;
    public WaveFormat WaveFormat => _src.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        Target?.PushFar(buffer, offset, n);
        return n;
    }
}
