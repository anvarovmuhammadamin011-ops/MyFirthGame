using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AndijonSaltanatlar.Core.Graphics;

namespace AndijonSaltanatlar.Core
{
    /// <summary>
    /// Development-only performance overlay. Production'da sukut bo'yicha o'chirilgan.
    /// F3 tugmasi bilan yoqiladi/o'chiriladi (PROMPT 21).
    /// </summary>
    public sealed class PerformanceMonitor
    {
        private readonly Stopwatch _frame = new Stopwatch();
        private readonly Stopwatch _update = new Stopwatch();
        private readonly Stopwatch _draw = new Stopwatch();
        private int _frames;
        private double _fpsAccum;
        private float _fps;
        private float _frameMs;
        private float _updateMs;
        private float _drawMs;
        private int _visibleSprites;
        private int _activeNpcs;
        private long _memoryMb;
        private float _netLatencyMs;
        private DateTime _lastSample = DateTime.UtcNow;

        public bool Enabled { get; set; } = false;

        public void BeginFrame()
        {
            _frame.Restart();
        }

        public void BeginUpdate() => _update.Restart();
        public void EndUpdate() { _update.Stop(); _updateMs = (float)_update.Elapsed.TotalMilliseconds; }

        public void BeginDraw() => _draw.Restart();
        public void EndDraw()
        {
            _draw.Stop();
            _drawMs = (float)_draw.Elapsed.TotalMilliseconds;
            // Sessiya statistikasi (sinov hisobot uchun): o'rtacha va eng yomon yangilash+chizish vaqti
            float work = _updateMs + _drawMs;
            _sumWork += work; _samples++;
            if (work > _worstWork) _worstWork = work;
        }

        private double _sumWork;
        private long _samples;
        private float _worstWork;

        /// <summary>Sinov hisobot: kadrlar soni, o'rtacha va eng yomon (yangilash + chizish) ms, ko'rinadigan spraytlar, NPC.</summary>
        public string Summary() =>
            "kadrlar=" + _samples + " ortacha_ms=" + (_samples > 0 ? (_sumWork / _samples).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "0")
            + " eng_yomon_ms=" + _worstWork.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + " spraytlar=" + _visibleSprites + " npc=" + _activeNpcs + " xotira_mb=" + (GC.GetTotalMemory(false) / (1024 * 1024));

        public void SetVisibleSprites(int n) => _visibleSprites = n;
        public void SetActiveNpcs(int n) => _activeNpcs = n;
        public void SetNetworkLatency(float ms) => _netLatencyMs = ms;

        public void EndFrame()
        {
            _frame.Stop();
            _frameMs = (float)_frame.Elapsed.TotalMilliseconds;
            _frames++;
            _fpsAccum += _frame.Elapsed.TotalSeconds;
            if (_fpsAccum >= 1.0)
            {
                _fps = (float)(_frames / _fpsAccum);
                _frames = 0;
                _fpsAccum = 0;
                _memoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
            }
        }

        public void Draw(SpriteBatch sb, PixelFont font, int x, int y)
        {
            if (!Enabled) return;
            Color c = new Color(220, 230, 245);
            int line = 14;
            font.Draw(sb, "FPS " + _fps.ToString("F1"), new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "FRAME " + _frameMs.ToString("F1") + " MS", new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "UPDATE " + _updateMs.ToString("F1") + " MS", new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "DRAW " + _drawMs.ToString("F1") + " MS", new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "SPRITES " + _visibleSprites, new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "NPCS " + _activeNpcs, new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "MEM " + _memoryMb + " MB", new Vector2(x, y), c, 1); y += line;
            font.Draw(sb, "NET " + _netLatencyMs.ToString("F0") + " MS", new Vector2(x, y), c, 1);
        }
    }
}
