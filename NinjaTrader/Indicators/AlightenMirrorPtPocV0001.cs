// BUILD PtPoc-2026-11-08e  (the first line of this file; the engine stamps it into its log) - e: RowMode 3 = either the volume POC or the |delta| row, whichever is in the wick (Bias build l); d: callers stay one bar behind in history so a bar forming at load time is never read as closed; c: historical->realtime handoff re-evaluates the last historical bar; b: wrapper boxes span the full bar width
/* AlightenMirrorPtPocV0001 - "Pattern POC": the POC-at-the-extreme pivot read of AlightenBiasV0006 (build k) cut down
   to what drives a Mirror level, at the user's request (2026-11-07, "a new version of the Bias indicator that will act
   as a new Pattern indicator for use in a new Mirror ... get rid of everything that is not needed to drive the levels").

   TWO CLASSES IN THIS FILE
   1. AlightenPtPocEngineV0001 - a plain class (not an indicator). The Mirror (V0056+) creates one per timeframe
      (1-, 5-, 10-minute series), feeds it every tick of the Mirror's own 1-tick series (FeedTick) and tells it when a
      bar of its series has closed (ProcessTo). It answers with level records (Levels): one per pivot bar whose read
      passed - the POC row (price range) on that bar, the bar's time span, the side, provisional or confirmed.
      Why not a hosted indicator: a hosted NinjaScript indicator cannot AddDataSeries, so it could never see the ticks;
      and the Mirror's local build check cannot see a factory method NinjaTrader only generates on F5.
   2. AlightenMirrorPtPocV0001 - a thin standalone indicator around that engine for checking the pattern alone on a
      chart: adds its own 1-tick series, draws the same boxes the Mirror draws, writes PtPocV0001.log. Not meant to be
      hosted.

   WHAT IS KEPT FROM THE BIAS (same numbers, same rules; verified on 11/06 1-min 14:06 and 12:36 against the Bias)
   - the pivot rules (ZigZag): a bar is a pivot high when High > previous High and the two bars are not both red; a
     pivot low when Low < previous Low and the two bars are not both green; same-side more extreme bars replace the
     developing pivot; the opposite side starting confirms it.
   - the footprint rows: every tick binned per bar into Cell Size rows anchored at the TRUE session open (Bias build h:
     walk back to the session's first bar when the window starts mid-session) or the bar low; at/above the L1 ask =
     ask volume, at/below the bid = bid volume, mid prints dropped.
   - the read: POC Row (volume POC / largest |delta| / largest delta against the pivot), ties to the pivot side
     (build j), "in the wick" = the row does not reach into the body (build k: the body-side tick may sit on the
     open/close), wick in ticks, position of the row centre in the wick, Extreme Mode (wick OR position / both /
     position / wick), Bars After Pivot, Sessions To Process.
   - the two stages: PROVISIONAL at the pivot bar's close (removed if a more extreme same-side bar replaces the pivot
     or the complete read disagrees), CONFIRMED when the opposite pivot starts. History keeps one bar of slack.
   - the log line format of BiasPocV0006.log (same fields, plus tf=), so the same scripts read it.
   WHAT IS GONE: the bias value and its level lines, gain/loss and FTG/FTL markers, zigzag drawing, containment box,
   diamonds, sounds/alerts, Number of Levels / Relevance Factor (level selection only), Debug prints.
   - By Alighten */
#region Using declarations
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>The tuning of one Pattern POC engine - the Bias V0006 "4. POC Signal" knobs that matter to the read.</summary>
    public class AlightenPtPocConfigV0001
    {
        public int    SessionsToProcess   = 1;      // 0 = all bars; N = from the first bar of the Nth most recent session
        public int    CellTicks           = 4;      // footprint row height
        public bool   AlignToSessionOpen  = true;   // rows start at the session open price; off = each bar's low
        public int    ExtremeMode         = 0;      // 0 wick OR position, 1 both, 2 position only, 3 wick only
        public int    RowMode             = 1;      // 0 volume POC, 1 largest |delta|, 2 largest delta against the pivot, 3 either 0 or 1 (the one in the wick; both -> the more extreme)
        public int    MinWickTicks        = 10;     // pivot extreme to body extreme
        public double ExtremeFraction     = 0.5;    // row centre position in the wick, 0 body .. 1 extreme
        public int    BarsAfterPivot      = 0;      // 0 = pivot bar only
        public bool   Provisional         = true;   // stage 1 levels (at the pivot bar's close) are reported too
    }

    /// <summary>One Pattern POC level: the POC row of a pivot bar whose read passed.</summary>
    public class AlightenPtPocLevelV0001
    {
        public string   Key;          // pivotBar + "H"/"L" - one level per pivot
        public int      PivotBar;     // bar index of the pivot (the engine's series)
        public int      BarIndex;     // bar index of the bar read (pivot bar + offset)
        public DateTime BarStart;     // open time of the read bar
        public DateTime BarEnd;       // close time of the read bar (its Time)
        public bool     IsHigh;       // pivot high (a short level) / pivot low (a long level)
        public double   Pivot;        // pivot extreme price
        public double   PocLo;        // row price range (lowest tick .. highest tick)
        public double   PocHi;
        public double   PocF;         // row centre position in the wick
        public int      WickTicks;
        public bool     Confirmed;    // false = provisional (stage 1)
        public bool     Removed;      // the pivot was replaced or the complete read disagreed - caller erases it
        public bool     Dirty;        // changed since the caller last drew it
    }

    /// <summary>The Pattern POC engine: Bias V0006's pivot + footprint read on one bar series, fed by its owner.</summary>
    public class AlightenPtPocEngineV0001
    {
        public const string BUILD = "PtPoc-2026-11-08e";

        public readonly List<AlightenPtPocLevelV0001> Levels = new List<AlightenPtPocLevelV0001>();
        public readonly List<string> PendingLog = new List<string>();   // one line per bar read; the owner drains it
        public int LastProcessedBar { get { return _lastBar; } }

        private readonly Bars   _bars;
        private readonly double _tick;
        private readonly AlightenPtPocConfigV0001 _cfg;
        private readonly string _tfTag;
        private readonly string _loadId;
        private readonly int    _periodMinutes;

        private readonly List<int>    _pivotBars   = new List<int>();
        private readonly List<double> _pivotPrices = new List<double>();
        private readonly List<bool>   _pivotIsHigh = new List<bool>();
        private readonly List<int>    _pending     = new List<int>();     // pivot list indices confirmed, not yet read
        private readonly Dictionary<int, Dictionary<double, double>> _binAsk = new Dictionary<int, Dictionary<double, double>>();
        private readonly Dictionary<int, Dictionary<double, double>> _binBid = new Dictionary<int, Dictionary<double, double>>();
        private readonly Dictionary<string, AlightenPtPocLevelV0001> _byKey = new Dictionary<string, AlightenPtPocLevelV0001>();
        private double _sessionOpen = 0.0;
        private int    _cutoff      = -1;
        private int    _lastBar     = -1;
        private int    _pruneAt     = 500;

        public AlightenPtPocEngineV0001(Bars bars, double tickSize, AlightenPtPocConfigV0001 cfg, string tfTag, string loadId)
        {
            _bars   = bars;
            _tick   = tickSize > 0 ? tickSize : 0.25;
            _cfg    = cfg ?? new AlightenPtPocConfigV0001();
            _tfTag  = tfTag ?? "";
            _loadId = loadId ?? "";
            _periodMinutes = bars != null && bars.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? bars.BarsPeriod.Value : 0;
        }

        // ---------------------------------------------------------------- ticks
        /// <summary>One tick: binned into the row of the bar that holds its time. Call for every tick, in time order.</summary>
        public void FeedTick(DateTime time, double price, double volume, double askPx, double bidPx)
        {
            if (_bars == null || _bars.Count == 0) return;
            int idx = _bars.GetBar(time);
            if (idx < 0) return;
            if (idx < Cutoff() - 5) return;                                   // session window, 5 bars of slack for the tick/bar join

            if (_bars.IsFirstBarOfSessionByIndex(idx))
                _sessionOpen = _bars.GetOpen(idx);
            if (_sessionOpen <= 0.0)
            {   // not at a session start: walk back to this session's first bar (Bias build h)
                int s = idx;
                while (s > 0 && !_bars.IsFirstBarOfSessionByIndex(s)) s--;
                _sessionOpen = _bars.GetOpen(s);
            }

            bool isBuy  = askPx > 0 && price >= askPx;
            bool isSell = bidPx > 0 && price <= bidPx;
            if (!isBuy && !isSell) return;

            double cell   = Math.Max(1, _cfg.CellTicks) * _tick;
            double origin = _cfg.AlignToSessionOpen ? _sessionOpen : _bars.GetLow(idx);
            origin = Math.Round(origin / _tick) * _tick;
            double binStart = origin + Math.Floor(((price - origin) / cell) + 1e-9) * cell;
            binStart = Math.Round(binStart / _tick) * _tick;

            Dictionary<double, double> a, b;
            if (!_binAsk.TryGetValue(idx, out a)) { a = new Dictionary<double, double>(); _binAsk[idx] = a; }
            if (!_binBid.TryGetValue(idx, out b)) { b = new Dictionary<double, double>(); _binBid[idx] = b; }
            double va, vb;
            a.TryGetValue(binStart, out va); b.TryGetValue(binStart, out vb);
            if (isBuy) va += volume; else vb += volume;
            a[binStart] = va; b[binStart] = vb;
        }

        // ---------------------------------------------------------------- bars
        /// <summary>Process every closed bar up to lastCompleteBar (inclusive). realtime: no slack (a closed bar's rows are complete).</summary>
        public void ProcessTo(int lastCompleteBar, bool realtime)
        {
            if (_bars == null) return;
            if (lastCompleteBar > _bars.Count - 1) lastCompleteBar = _bars.Count - 1;
            if (realtime && !_handoff)
            {   // build c: the last HISTORICAL bar was evaluated with one bar of slack (its provisional read and any
                // just-due confirmed read deferred); its rows are complete, so evaluate it once more without slack
                // before the first new bar. Without this a pivot on the bar before a Market Replay 'Go To' time (or
                // a live chart's load time) got no provisional level and only appeared on its confirmed read.
                // build d: "complete" holds only because the callers now pass CurrentBars - 1 in history too, so the
                // bar forming at load time is never _lastBar here; it is processed by the loop below once it closes.
                _handoff = true;
                if (_lastBar >= 2 && _lastBar >= Cutoff()) Evaluate(_lastBar, 0);
            }
            for (int i = _lastBar + 1; i <= lastCompleteBar; i++)
            {
                _lastBar = i;
                if (i < 2 || i < Cutoff()) continue;
                OnClosedBar(i, realtime ? 0 : 1);
            }
        }

        private int Cutoff()
        {
            if (_cutoff >= 0) return _cutoff;
            if (_cfg.SessionsToProcess <= 0 || _bars == null) { _cutoff = 0; return 0; }
            int seen = 0, cut = 0;
            for (int i = _bars.Count - 1; i >= 0; i--)
            {
                if (!_bars.IsFirstBarOfSessionByIndex(i)) continue;
                seen++;
                if (seen >= _cfg.SessionsToProcess) { cut = i; break; }
            }
            _cutoff = cut;
            return cut;
        }

        private void OnClosedBar(int i, int slack)
        {
            double h = _bars.GetHigh(i), l = _bars.GetLow(i), o = _bars.GetOpen(i), c = _bars.GetClose(i);
            double h1 = _bars.GetHigh(i - 1), l1 = _bars.GetLow(i - 1), o1 = _bars.GetOpen(i - 1), c1 = _bars.GetClose(i - 1);
            bool isHigh = h > h1, isLow = l < l1;
            bool prevGreen = c1 >= o1, prevRed = c1 < o1, currGreen = c >= o, currRed = c < o;

            // the Bias's six rules, in its order (an outside bar after a green bar: high then low; after a red bar: low then high)
            if (prevGreen && currGreen && isHigh) ProcessPivot(i, h, true);
            if (prevRed   && currRed   && isLow)  ProcessPivot(i, l, false);
            if (prevGreen && currRed   && isHigh) ProcessPivot(i, h, true);
            if (prevRed   && currGreen && isLow)  ProcessPivot(i, l, false);
            if (prevRed   && currGreen && isHigh) ProcessPivot(i, h, true);
            if (prevGreen && currRed   && isLow)  ProcessPivot(i, l, false);

            if (i >= _pruneAt) { Prune(i); _pruneAt = i + 500; }
            Evaluate(i, slack);
        }

        private void ProcessPivot(int barIndex, double price, bool isHigh)
        {
            if (_pivotBars.Count == 0)
            {
                _pivotBars.Add(barIndex); _pivotPrices.Add(price); _pivotIsHigh.Add(isHigh);
                return;
            }
            int lastIdx = _pivotPrices.Count - 1;
            if (isHigh == _pivotIsHigh[lastIdx])
            {
                if ((isHigh && price > _pivotPrices[lastIdx]) || (!isHigh && price < _pivotPrices[lastIdx]))
                {
                    Drop(_pivotBars[lastIdx] + (isHigh ? "H" : "L"));     // the replaced pivot's provisional level goes
                    _pivotBars[lastIdx]   = barIndex;
                    _pivotPrices[lastIdx] = price;
                }
            }
            else
            {
                _pending.Add(lastIdx);                                      // the previous developing pivot is confirmed
                _pivotBars.Add(barIndex); _pivotPrices.Add(price); _pivotIsHigh.Add(isHigh);
            }
        }

        private void Prune(int cur)
        {
            int keep = cur - 400;
            foreach (int k in _binAsk.Keys.Where(k => k < keep).ToList()) { _binAsk.Remove(k); _binBid.Remove(k); }
            // removed levels the owner has already erased
            Levels.RemoveAll(x => x.Removed && !x.Dirty);
        }

        // ---------------------------------------------------------------- the read
        private void RowInWick(double start, double cell, bool isHigh, double hi, double lo, double bodyHi, double bodyLo,
                               out bool inWick, out double pocF)
        {
            double rowHi  = start + cell - _tick;
            double centre = start + (cell - _tick) / 2.0;
            if (isHigh)
            {
                double wick = hi - bodyHi;
                inWick = start >= bodyHi - _tick * 0.01;
                pocF   = wick > 0 ? Math.Max(0.0, Math.Min(1.0, (centre - bodyHi) / wick)) : 0.0;
            }
            else
            {
                double wick = bodyLo - lo;
                inWick = rowHi <= bodyLo + _tick * 0.01;
                pocF   = wick > 0 ? Math.Max(0.0, Math.Min(1.0, (bodyLo - centre) / wick)) : 0.0;
            }
        }

        private bool ReadBar(int barIndex, bool isHigh, out bool inWick, out int wickTicks, out double pocF,
                             out double pocLo, out double pocHi, out double vol, out string extra)
        {
            inWick = false; wickTicks = 0; pocF = 0.0; pocLo = 0.0; pocHi = 0.0; vol = 0.0; extra = "";
            Dictionary<double, double> a, b;
            if (!_binAsk.TryGetValue(barIndex, out a) || !_binBid.TryGetValue(barIndex, out b) || a.Count == 0)
                return false;

            double cell = Math.Max(1, _cfg.CellTicks) * _tick;
            double bestV = -1.0, startV = 0.0;
            double bestD = -1.0, startD = 0.0, deltaD = 0.0;
            double bestS = double.NegativeInfinity, startS = 0.0, deltaS = 0.0;
            foreach (double start in (isHigh ? a.Keys.OrderByDescending(x => x) : a.Keys.OrderBy(x => x)))   // ties -> pivot side
            {
                double vb; b.TryGetValue(start, out vb);
                double va = a[start];
                double v = va + vb, d = va - vb;
                vol += v;
                if (v > bestV) { bestV = v; startV = start; }
                if (Math.Abs(d) > bestD) { bestD = Math.Abs(d); startD = start; deltaD = d; }
                double against = isHigh ? d : -d;
                if (against > bestS) { bestS = against; startS = start; deltaS = d; }
            }
            if (bestV < 0) return false;

            double hi = _bars.GetHigh(barIndex), lo = _bars.GetLow(barIndex);
            double bodyHi = Math.Max(_bars.GetOpen(barIndex), _bars.GetClose(barIndex));
            double bodyLo = Math.Min(_bars.GetOpen(barIndex), _bars.GetClose(barIndex));
            wickTicks = (int)Math.Round((isHigh ? hi - bodyHi : bodyLo - lo) / _tick);

            bool inV, inD, inS; double fV, fD, fS;
            RowInWick(startV, cell, isHigh, hi, lo, bodyHi, bodyLo, out inV, out fV);
            RowInWick(startD, cell, isHigh, hi, lo, bodyHi, bodyLo, out inD, out fD);
            RowInWick(startS, cell, isHigh, hi, lo, bodyHi, bodyLo, out inS, out fS);

            double bestStart;
            switch (_cfg.RowMode)
            {
                case 1:  bestStart = startD; inWick = inD; pocF = fD; break;
                case 2:  bestStart = startS; inWick = inS; pocF = fS; break;
                case 3:  // either row: the one in the wick; both in the wick -> the more extreme (larger pocF), ties -> |delta|
                    if (inD && (!inV || fD >= fV)) { bestStart = startD; inWick = inD; pocF = fD; }
                    else if (inV)                  { bestStart = startV; inWick = inV; pocF = fV; }
                    else                           { bestStart = startD; inWick = false; pocF = fD; }
                    break;
                default: bestStart = startV; inWick = inV; pocF = fV; break;
            }
            pocLo = bestStart;
            pocHi = bestStart + cell - _tick;

            extra = " rowMode=" + _cfg.RowMode
                  + " vpoc=" + startV.ToString("F2") + "-" + (startV + cell - _tick).ToString("F2") + " vIn=" + (inV ? 1 : 0) + " vF=" + fV.ToString("F2")
                  + " dpoc=" + startD.ToString("F2") + "-" + (startD + cell - _tick).ToString("F2") + " dDelta=" + deltaD.ToString("F0") + " dIn=" + (inD ? 1 : 0) + " dF=" + fD.ToString("F2")
                  + " spoc=" + startS.ToString("F2") + "-" + (startS + cell - _tick).ToString("F2") + " sDelta=" + deltaS.ToString("F0") + " sIn=" + (inS ? 1 : 0) + " sF=" + fS.ToString("F2");
            return true;
        }

        private bool Passes(bool inWick, int wickTicks, double pocF)
        {
            if (!inWick) return false;
            bool w = wickTicks >= _cfg.MinWickTicks;
            bool p = pocF >= _cfg.ExtremeFraction;
            switch (_cfg.ExtremeMode)
            {
                case 1:  return w && p;
                case 2:  return p;
                case 3:  return w;
                default: return w || p;
            }
        }

        // One pivot's read over the pivot bar and the bars after it, at bar cur. Returns the first qualifying offset (-1 none)
        // and fills the level fields of the passing bar.
        private int ReadPivot(int idx, int cur, int minAgo, string stage, AlightenPtPocLevelV0001 fill)
        {
            int    sigOffset  = -1;
            bool   isHigh     = _pivotIsHigh[idx];
            int    pivotBar   = _pivotBars[idx];
            double pivotPrice = _pivotPrices[idx];
            double bodyLevel  = isHigh ? Math.Max(_bars.GetOpen(pivotBar), _bars.GetClose(pivotBar))
                                       : Math.Min(_bars.GetOpen(pivotBar), _bars.GetClose(pivotBar));

            for (int off = 0; off <= _cfg.BarsAfterPivot; off++)
            {
                int b = pivotBar + off;
                if (cur - b < minAgo) break;
                bool inWick; int wickTicks; double pocF, pocLo, pocHi, vol; string extra;
                if (!ReadBar(b, isHigh, out inWick, out wickTicks, out pocF, out pocLo, out pocHi, out vol, out extra))
                    continue;
                bool pass = Passes(inWick, wickTicks, pocF);
                if (pass && sigOffset < 0)
                {
                    sigOffset = off;
                    fill.PivotBar  = pivotBar;
                    fill.BarIndex  = b;
                    fill.BarEnd    = _bars.GetTime(b);
                    fill.BarStart  = _periodMinutes > 0 ? fill.BarEnd.AddMinutes(-_periodMinutes) : (b > 0 ? _bars.GetTime(b - 1) : fill.BarEnd);
                    fill.IsHigh    = isHigh;
                    fill.Pivot     = pivotPrice;
                    fill.PocLo     = pocLo;
                    fill.PocHi     = pocHi;
                    fill.PocF      = pocF;
                    fill.WickTicks = wickTicks;
                }
                PendingLog.Add(
                      "L" + _loadId + " [" + (minAgo == 0 ? "Realtime" : "Historical") + "] read=" + _bars.GetTime(cur).ToString("yyyy-MM-dd HH:mm:ss")
                    + " stage=" + stage + " PIVOT " + (isHigh ? "H" : "L")
                    + " pivotBar=" + _bars.GetTime(pivotBar).ToString("yyyy-MM-dd HH:mm:ss")
                    + " pivot=" + pivotPrice.ToString("F2") + " body=" + bodyLevel.ToString("F2")
                    + " off=" + off + " bar=" + _bars.GetTime(b).ToString("yyyy-MM-dd HH:mm:ss")
                    + " o=" + _bars.GetOpen(b).ToString("F2") + " h=" + _bars.GetHigh(b).ToString("F2") + " l=" + _bars.GetLow(b).ToString("F2") + " c=" + _bars.GetClose(b).ToString("F2")
                    + " wick=" + wickTicks + " pocF=" + pocF.ToString("F2") + " poc=" + pocLo.ToString("F2") + "-" + pocHi.ToString("F2")
                    + " vol=" + vol.ToString("F0") + " inWick=" + (inWick ? 1 : 0) + " pass=" + (pass ? 1 : 0)
                    + " cell=" + _cfg.CellTicks + " mode=" + _cfg.ExtremeMode + " minWick=" + _cfg.MinWickTicks + " frac=" + _cfg.ExtremeFraction.ToString("F2")
                    + " signal=" + (sigOffset == off ? (isHigh ? "S" : "L") : "-")
                    + extra + " tf=" + _tfTag);
            }
            return sigOffset;
        }

        // ---------------------------------------------------------------- levels
        private void Drop(string key)
        {
            AlightenPtPocLevelV0001 lv;
            if (!_byKey.TryGetValue(key, out lv)) return;
            lv.Removed = true; lv.Dirty = true;
            _byKey.Remove(key);
        }

        private void DropReplaced(bool isHigh, string keepKey)
        {
            string side = isHigh ? "H" : "L";
            foreach (string k in _byKey.Keys.Where(k => k.EndsWith(side) && k != keepKey).ToList())
            {
                // only PROVISIONAL levels of that side are "replaced"; confirmed ones belong to earlier pivots
                if (!_byKey[k].Confirmed) Drop(k);
            }
        }

        private void Put(string key, AlightenPtPocLevelV0001 read, bool confirmed)
        {
            AlightenPtPocLevelV0001 lv;
            if (_byKey.TryGetValue(key, out lv))
            {
                if (lv.BarIndex != read.BarIndex || lv.PocLo != read.PocLo || lv.PocHi != read.PocHi || lv.Confirmed != confirmed)
                {
                    lv.BarIndex = read.BarIndex; lv.BarStart = read.BarStart; lv.BarEnd = read.BarEnd;
                    lv.PocLo = read.PocLo; lv.PocHi = read.PocHi; lv.PocF = read.PocF; lv.WickTicks = read.WickTicks;
                    lv.Confirmed = confirmed; lv.Dirty = true;
                }
                return;
            }
            read.Key = key; read.Confirmed = confirmed; read.Dirty = true;
            _byKey[key] = read;
            Levels.Add(read);
        }

        // Stage 2 for every confirmed pivot whose bars are complete; stage 1 for the newest (developing) pivot.
        private void Evaluate(int cur, int slack)
        {
            for (int q = _pending.Count - 1; q >= 0; q--)
            {
                int idx = _pending[q];
                if (idx < 0 || idx >= _pivotBars.Count) { _pending.RemoveAt(q); continue; }
                int barsAgo = cur - _pivotBars[idx];
                if (barsAgo < _cfg.BarsAfterPivot + slack) continue;
                _pending.RemoveAt(q);

                string key  = _pivotBars[idx] + (_pivotIsHigh[idx] ? "H" : "L");
                var    read = new AlightenPtPocLevelV0001();
                if (ReadPivot(idx, cur, slack, "CONF", read) >= 0) Put(key, read, true);
                else Drop(key);
            }

            if (_cfg.Provisional && _pivotBars.Count > 0)
            {
                int    idx    = _pivotBars.Count - 1;
                bool   isHigh = _pivotIsHigh[idx];
                string key    = _pivotBars[idx] + (isHigh ? "H" : "L");
                DropReplaced(isHigh, key);
                // one provisional read per pivot bar (its rows are complete; the Bias re-read every bar with the same
                // answer and only logged the passing ones - the failing lines are dropped here the same way)
                if (cur - _pivotBars[idx] >= slack && !_byKey.ContainsKey(key) && _provTried.Add(key))
                {
                    int logMark = PendingLog.Count;
                    var read = new AlightenPtPocLevelV0001();
                    if (ReadPivot(idx, cur, slack, "PROV", read) >= 0) Put(key, read, false);
                    else PendingLog.RemoveRange(logMark, PendingLog.Count - logMark);
                }
            }
        }
        private readonly HashSet<string> _provTried = new HashSet<string>();
        private bool _handoff;   // build c: the last historical bar has been re-evaluated without slack
    }

    /// <summary>Standalone check of the Pattern POC on a chart: its own 1-tick series, the engine, boxes, a log.</summary>
    public class AlightenMirrorPtPocV0001 : Indicator
    {
        private AlightenPtPocEngineV0001 _engine;
        private string _loadId = "";
        private bool   _logOn  = true;

        #region Properties
        [NinjaScriptProperty] [Range(0, int.MaxValue)]
        [Display(Name = "1.1 Sessions To Process (0 = all)", GroupName = "1. Pattern POC", Order = 1)]
        public int SessionsToProcess { get; set; } = 1;

        [NinjaScriptProperty] [Range(1, 50)]
        [Display(Name = "1.2 Cell Size (ticks)", GroupName = "1. Pattern POC", Order = 2)]
        public int PocCellTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "1.3 Align Cells To Session Open", GroupName = "1. Pattern POC", Order = 3)]
        public bool PocAlignToSessionOpen { get; set; } = true;

        [NinjaScriptProperty] [Range(0, 3)]
        [Display(Name = "1.4 Extreme Mode: 0 Wick OR Position, 1 Both, 2 Position only, 3 Wick only", GroupName = "1. Pattern POC", Order = 4)]
        public int PocExtremeMode { get; set; } = 0;

        [NinjaScriptProperty] [Range(0, 3)]
        [Display(Name = "1.5 POC Row: 0 Heaviest row, 1 Largest |delta| row, 2 Largest delta against the pivot, 3 Either 0 or 1", GroupName = "1. Pattern POC", Order = 5)]
        public int PocRowMode { get; set; } = 1;

        [NinjaScriptProperty] [Range(0, 200)]
        [Display(Name = "1.6 Min Wick (ticks)", GroupName = "1. Pattern POC", Order = 6)]
        public int PocMinWickTicks { get; set; } = 10;

        [NinjaScriptProperty] [Range(0.0, 1.0)]
        [Display(Name = "1.7 POC Position In Wick (0 body .. 1 extreme)", GroupName = "1. Pattern POC", Order = 7)]
        public double PocExtremeFraction { get; set; } = 0.5;

        [NinjaScriptProperty] [Range(0, 5)]
        [Display(Name = "1.8 Bars After Pivot (also read)", GroupName = "1. Pattern POC", Order = 8)]
        public int PocBarsAfterPivot { get; set; } = 0;

        [NinjaScriptProperty]
        [Display(Name = "1.9 Show Provisional Levels", GroupName = "1. Pattern POC", Order = 9)]
        public bool ShowPocProvisional { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "2.1 Short Box Color (high pivot)", GroupName = "2. Boxes", Order = 1)]
        public Brush PocShortBrush { get; set; } = Brushes.Magenta;
        [Browsable(false)] public string PocShortBrushSerialize { get { return Serialize.BrushToString(PocShortBrush); } set { PocShortBrush = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "2.2 Long Box Color (low pivot)", GroupName = "2. Boxes", Order = 2)]
        public Brush PocLongBrush { get; set; } = Brushes.Lime;
        [Browsable(false)] public string PocLongBrushSerialize { get { return Serialize.BrushToString(PocLongBrush); } set { PocLongBrush = Serialize.StringToBrush(value); } }

        [Range(0, 100)]
        [Display(Name = "2.3 Fill Opacity (confirmed)", GroupName = "2. Boxes", Order = 3)]
        public int PocFillOpacity { get; set; } = 35;

        [Range(0, 100)]
        [Display(Name = "2.4 Fill Opacity (provisional)", GroupName = "2. Boxes", Order = 4)]
        public int PocProvisionalOpacity { get; set; } = 12;

        [Range(1, 10)]
        [Display(Name = "2.5 Outline Width", GroupName = "2. Boxes", Order = 5)]
        public int PocOutlineWidth { get; set; } = 2;

        [Display(Name = "3.1 Log Folder", GroupName = "3. Log", Order = 1)]
        public string LogFolder { get; set; } = "Mirror Logs";

        [Display(Name = "3.2 Log Historical Pivots", GroupName = "3. Log", Order = 2)]
        public bool LogHistoricalPivots { get; set; } = true;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "AlightenMirrorPtPocV0001: the Pattern POC engine (Bias V0006 build k's POC-at-the-extreme pivot read) on this chart's bars, drawn as boxes the width of the bar - the standalone check of what the Mirror V0056 draws. " + AlightenPtPocEngineV0001.BUILD;
                Name = "AlightenMirrorPtPocV0001";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                _loadId = DateTime.Now.ToString("yyyyMMddHHmmss");
                _engine = new AlightenPtPocEngineV0001(BarsArray[0], TickSize, Config(), TfTag(), _loadId);
                _logOn  = true;
            }
        }

        private AlightenPtPocConfigV0001 Config()
        {
            return new AlightenPtPocConfigV0001
            {
                SessionsToProcess = SessionsToProcess, CellTicks = PocCellTicks, AlignToSessionOpen = PocAlignToSessionOpen,
                ExtremeMode = PocExtremeMode, RowMode = PocRowMode, MinWickTicks = PocMinWickTicks,
                ExtremeFraction = PocExtremeFraction, BarsAfterPivot = PocBarsAfterPivot, Provisional = ShowPocProvisional
            };
        }

        protected override void OnBarUpdate()
        {
            if (_engine == null || BarsArray.Length < 2 || CurrentBars[0] < 0 || CurrentBars[1] < 0) return;

            if (BarsInProgress == 1)
            {
                int  indexOffset  = BarsArray[1].Count - 1 - CurrentBars[1];
                bool inTransition = State == State.Realtime && indexOffset > 1;
                bool useCurrent   = State == State.Historical || inTransition || Calculate != Calculate.OnBarClose;
                int  ti           = useCurrent ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);
                _engine.FeedTick(BarsArray[1].GetTime(ti), BarsArray[1].GetClose(ti), BarsArray[1].GetVolume(ti), BarsArray[1].GetAsk(ti), BarsArray[1].GetBid(ti));
                return;
            }
            if (BarsInProgress != 0) return;

            // build d: history stays one bar behind - the last historical bar is the bar forming at load time when the
            // chart (re)loads mid-bar, and the engine must not read it as closed (see the Mirror V0056 build h note).
            int lastComplete = State == State.Realtime && Calculate == Calculate.OnBarClose ? CurrentBar : CurrentBar - 1;
            _engine.ProcessTo(lastComplete, State == State.Realtime);
            DrawLevels();
            FlushLog();
        }

        private void DrawLevels()
        {
            if (ChartControl == null) return;
            foreach (var lv in _engine.Levels)
            {
                if (!lv.Dirty) continue;
                lv.Dirty = false;
                string tag = "PtPoc_" + lv.Key;
                if (lv.Removed) { RemoveDrawObject(tag); continue; }
                Brush b = lv.IsHigh ? PocShortBrush : PocLongBrush;
                TimeSpan half = TimeSpan.FromSeconds(BarSeconds() / 2.0);   // b: edges on the bar boundaries, not the bar centres
                var rect = Draw.Rectangle(this, tag, false, lv.BarStart + half, lv.PocLo, lv.BarEnd + half, lv.PocHi + TickSize, b, b,
                                          lv.Confirmed ? PocFillOpacity : PocProvisionalOpacity);
                if (rect != null) rect.OutlineStroke = new Stroke(b, lv.Confirmed ? DashStyleHelper.Solid : DashStyleHelper.Dash, PocOutlineWidth);
            }
        }

        private void FlushLog()
        {
            if (_engine.PendingLog.Count == 0) return;
            if (_logOn && (State == State.Realtime || LogHistoricalPivots))
            {
                try
                {
                    string folder = string.IsNullOrWhiteSpace(LogFolder) ? "Mirror Logs" : LogFolder.Trim();
                    if (!Path.IsPathRooted(folder)) folder = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, folder);
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "PtPocV0001.log"), string.Join(Environment.NewLine, _engine.PendingLog) + Environment.NewLine);
                }
                catch { _logOn = false; }
            }
            _engine.PendingLog.Clear();
        }

        private double BarSeconds()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Second: return BarsPeriod.Value;
                case BarsPeriodType.Minute: return BarsPeriod.Value * 60.0;
                default:                    return 0.0;
            }
        }

        private string TfTag()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Second: return BarsPeriod.Value + "s";
                case BarsPeriodType.Minute: return BarsPeriod.Value + "m";
                case BarsPeriodType.Day:    return BarsPeriod.Value + "d";
                default:                    return BarsPeriod.Value + BarsPeriod.BarsPeriodType.ToString();
            }
        }
    }
}
