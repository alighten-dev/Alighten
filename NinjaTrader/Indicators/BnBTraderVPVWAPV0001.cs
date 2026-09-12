// =====================================================================================
// BnBTraderVPVWAPV0001
//
// Original work by BnBTrader. The VWAP engine — session anchoring, the cumulative
// price*volume / price^2*volume accumulators and the standard-deviation bands — is his,
// carried over from BnBTraderRbsScalperV9 verbatim. Full credit for that work is his.
//
// The Volume Profile portion was updated by Alighten: it is rebuilt on a 1-tick
// secondary series so POC / VAH / VAL match NinjaTrader OrderFlow+ Volume Profile
// without requiring Tick Replay.
// =====================================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	[Description("Session volume profile (POC / VAH / VAL), naked POCs, prior-day H/L and session VWAP with standard-deviation bands. Derived from BnBTraderRbsScalperV9: the VWAP math is carried over unchanged, the volume profile is rebuilt on a 1-tick secondary series so it matches NinjaTrader OrderFlow+ Volume Profile without requiring Tick Replay.")]
	public class BnBTraderVPVWAPV0001 : Indicator
	{
		// ---------------------------------------------------------------------------------
		// Index of the secondary 1-tick series added in State.Configure. This is the ONLY
		// source of volume-at-price. Tick Replay exists to rebuild historical bid/ask and
		// depth context, which this profile never uses, so a plain tick series is both
		// sufficient and far cheaper.
		// ---------------------------------------------------------------------------------
		private const int TickSeries = 1;

		// VWAP state - carried over from BnBTraderRbsScalperV9 verbatim.
		private double pdh, pdl, cumulativePV, cumulativeP2V, cumulativeVol, lastVolume;
		private double currentSessionMax, currentSessionMin;
		private double currentVWAP, currentVWAP_SD;

		// Profile state.
		private double curPOC, curVAH, curVAL;
		private bool profileDirty;

		private readonly object dataLock = new object();
		private SortedDictionary<double, long> volProfile = new SortedDictionary<double, long>();
		private List<double> nakedPOCs = new List<double>();
		private NinjaTrader.Gui.Tools.SimpleFont labelFont;

		#region Properties
		[NinjaScriptProperty][Display(Name="Show Volume Profile", Order=1, GroupName="1. Structural Settings")]
		public bool ShowVP { get; set; }
		[NinjaScriptProperty][Display(Name="Show VWAP & Dev Bands", Order=2, GroupName="1. Structural Settings")]
		public bool ShowVWAP { get; set; }
		[Range(10, 500), NinjaScriptProperty][Display(Name="VP Width (px)", Order=3, GroupName="1. Structural Settings")]
		public int VPWidth { get; set; }
		[Range(0, 100), NinjaScriptProperty][Display(Name="VP Opacity", Order=4, GroupName="1. Structural Settings")]
		public int VPOpacity { get; set; }
		[Range(10, 100), NinjaScriptProperty][Display(Name="Value Area %", Order=5, GroupName="1. Structural Settings")]
		public double VAPercentage { get; set; }

		[XmlIgnore][Display(Name="VP Value Area Color", Order=6, GroupName="1. Structural Settings")]
		public System.Windows.Media.Brush VPValueAreaColor { get; set; }
		[Browsable(false)] public string VPValueAreaColorSerialize { get { return Serialize.BrushToString(VPValueAreaColor); } set { VPValueAreaColor = Serialize.StringToBrush(value); } }

		[XmlIgnore][Display(Name="VP Outside Color", Order=7, GroupName="1. Structural Settings")]
		public System.Windows.Media.Brush VPOutsideColor { get; set; }
		[Browsable(false)] public string VPOutsideColorSerialize { get { return Serialize.BrushToString(VPOutsideColor); } set { VPOutsideColor = Serialize.StringToBrush(value); } }

		[Range(0.1, 10), NinjaScriptProperty][Display(Name="VWAP SD 1 Multiplier", Order=1, GroupName="2. Levels & Bands")]
		public double SD1_Mult { get; set; }
		[Range(0.1, 10), NinjaScriptProperty][Display(Name="VWAP SD 2 Multiplier", Order=2, GroupName="2. Levels & Bands")]
		public double SD2_Mult { get; set; }
		[Range(0.1, 10), NinjaScriptProperty][Display(Name="VWAP SD 3 Multiplier", Order=3, GroupName="2. Levels & Bands")]
		public double SD3_Mult { get; set; }
		[NinjaScriptProperty][Display(Name="Show Naked POCs", Order=4, GroupName="2. Levels & Bands")]
		public bool ShowNakedPOCs { get; set; }
		[NinjaScriptProperty][Display(Name="Show Prior Day H/L", Order=5, GroupName="2. Levels & Bands")]
		public bool ShowPriorDay { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "BnBTraderVPVWAPV0001";
				Description = "Session volume profile (POC / VAH / VAL), naked POCs, prior-day H/L and session VWAP with standard-deviation bands. Profile is built from a 1-tick secondary series to match NinjaTrader OrderFlow+ Volume Profile without Tick Replay.";
				Calculate = Calculate.OnEachTick; IsOverlay = true;

				ShowVP = true; ShowVWAP = true; VPWidth = 160; VPOpacity = 40;

				// OrderFlow+ Volume Profile defaults to a 70% value area. BnB used 68.0
				// (the true 1-sigma figure); 70 is the Market Profile convention and is
				// what you need for VAH/VAL to line up with OF+.
				VAPercentage = 70.0;

				ShowNakedPOCs = true; ShowPriorDay = true;
				SD1_Mult = 1.0; SD2_Mult = 2.0; SD3_Mult = 3.0;

				VPValueAreaColor = System.Windows.Media.Brushes.OrangeRed;
				VPOutsideColor = System.Windows.Media.Brushes.Gray;

				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Goldenrod, 2), PlotStyle.Line, "VWAP Curve");   // 0
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Yellow, 1), PlotStyle.Line, "POC Level");       // 1
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Red, 1), PlotStyle.Line, "VAH Level");          // 2
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Lime, 1), PlotStyle.Line, "VAL Level");         // 3
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DeepPink, 1), PlotStyle.Line, "PDH Level");     // 4
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DeepPink, 1), PlotStyle.Line, "PDL Level");     // 5

				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DimGray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD1 Upper");   // 6
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DimGray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD1 Lower");   // 7
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD2 Upper");      // 8
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.Gray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD2 Lower");      // 9
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DarkGray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD3 Upper");  // 10
				AddPlot(new NinjaTrader.Gui.Stroke(System.Windows.Media.Brushes.DarkGray, DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP SD3 Lower");  // 11
			}
			else if (State == State.Configure)
			{
				// Real traded prices and volumes over history, with no Tick Replay. The tick
				// series is loaded for the same range as the chart, so Days-to-Load is the
				// knob that controls how heavy this is.
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				labelFont = new NinjaTrader.Gui.Tools.SimpleFont("Consolas", 11);
				volProfile.Clear();
				nakedPOCs.Clear();
				profileDirty = true;
			}
		}

		protected override void OnBarUpdate()
		{
			// ---------------------------------------------------------------------------
			// SECONDARY 1-TICK SERIES: the single accumulation path for volume-at-price.
			// One code path for history and realtime means no smear, no reload drift and
			// no historical/realtime seam - the three ways BnB's profile could disagree
			// with itself.
			// ---------------------------------------------------------------------------
			if (BarsInProgress == TickSeries)
			{
				if (CurrentBars[TickSeries] < 0) return;

				lock (dataLock)
				{
					// Explicitly the TICK series, not whichever series Bars happens to track.
					if (BarsArray[TickSeries].IsFirstBarOfSession) RollProfileSession();

					long v = (long)Volumes[TickSeries][0];
					if (v > 0)
					{
						double pk = Instrument.MasterInstrument.RoundToTickSize(Closes[TickSeries][0]);
						long cur;
						volProfile.TryGetValue(pk, out cur);
						volProfile[pk] = cur + v;
						profileDirty = true;
					}
				}
				return;
			}

			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			lock (dataLock)
			{
				if (Bars.IsFirstBarOfSession && IsFirstTickOfBar)
				{
					if (currentSessionMax > 0) { pdh = currentSessionMax; pdl = currentSessionMin; }
					currentSessionMax = High[0]; currentSessionMin = Low[0];
					cumulativePV = 0; cumulativeP2V = 0; cumulativeVol = 0; lastVolume = 0;
				}

				currentSessionMax = Math.Max(currentSessionMax, High[0]);
				currentSessionMin = Math.Min(currentSessionMin, Low[0]);

				for (int i = nakedPOCs.Count - 1; i >= 0; i--)
					if (Low[0] <= nakedPOCs[i] && High[0] >= nakedPOCs[i]) nakedPOCs.RemoveAt(i);

				// ---------------------------------------------------------------------
				// VWAP - UNCHANGED from BnBTraderRbsScalperV9, including the two-branch
				// historical/realtime split and the E[x^2]-E[x]^2 variance identity.
				// This is the part that already matches OF+ VWAP; do not "fix" it.
				// ---------------------------------------------------------------------
				bool isRealtimeOrReplay = (State == State.Realtime || Bars.IsTickReplay);

				if (!isRealtimeOrReplay)
				{
					if (IsFirstTickOfBar)
					{
						long vol = (long)Volume[0];
						double typPrice = (High[0] + Low[0] + Close[0]) / 3.0;
						cumulativePV += typPrice * vol;
						cumulativeP2V += typPrice * typPrice * vol;
						cumulativeVol += vol;
					}
				}
				else
				{
					if (IsFirstTickOfBar) lastVolume = 0;

					double currentVol = Volume[0];
					double tickVol = currentVol - lastVolume;
					lastVolume = currentVol;

					if (tickVol > 0)
					{
						double tickPrice = Close[0];
						cumulativePV += tickPrice * tickVol;
						cumulativeP2V += tickPrice * tickPrice * tickVol;
						cumulativeVol += tickVol;
					}
				}

				currentVWAP = (cumulativeVol != 0) ? cumulativePV / cumulativeVol : Close[0];
				double variance = (cumulativeVol != 0) ? (cumulativeP2V / cumulativeVol) - (currentVWAP * currentVWAP) : 0;
				currentVWAP_SD = variance > 0 ? Math.Sqrt(variance) : 0;

				if (profileDirty) { ComputeValueArea(); profileDirty = false; }
			}

			Values[0][0] = currentVWAP;
			Values[1][0] = curPOC; Values[2][0] = curVAH; Values[3][0] = curVAL;
			Values[4][0] = pdh;    Values[5][0] = pdl;

			Values[6][0]  = currentVWAP + (currentVWAP_SD * SD1_Mult);
			Values[7][0]  = currentVWAP - (currentVWAP_SD * SD1_Mult);
			Values[8][0]  = currentVWAP + (currentVWAP_SD * SD2_Mult);
			Values[9][0]  = currentVWAP - (currentVWAP_SD * SD2_Mult);
			Values[10][0] = currentVWAP + (currentVWAP_SD * SD3_Mult);
			Values[11][0] = currentVWAP - (currentVWAP_SD * SD3_Mult);

			// Tags are prefixed so this can sit on the same chart as BnBTraderRbsScalperV9
			// without the two indicators fighting over identical drawing-object tags.
			if (ShowVP && curPOC > 0)
			{
				Draw.Text(this, "AVP_POCL", false, "POC " + curPOC, -5, curPOC, 15, Plots[1].Brush ?? System.Windows.Media.Brushes.Magenta, labelFont, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
				Draw.Text(this, "AVP_VAHL", false, "VAH " + curVAH, -5, curVAH, 15, Plots[2].Brush ?? System.Windows.Media.Brushes.White, labelFont, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
				Draw.Text(this, "AVP_VALL", false, "VAL " + curVAL, -5, curVAL, 15, Plots[3].Brush ?? System.Windows.Media.Brushes.White, labelFont, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
			}
			if (ShowPriorDay && pdh > 0) Draw.Text(this, "AVP_PDHL", false, "PDH " + pdh, -5, pdh, 15, Plots[4].Brush ?? System.Windows.Media.Brushes.DeepPink, labelFont, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
			if (ShowPriorDay && pdl > 0) Draw.Text(this, "AVP_PDLL", false, "PDL " + pdl, -5, pdl, 15, Plots[5].Brush ?? System.Windows.Media.Brushes.DeepPink, labelFont, System.Windows.TextAlignment.Left, System.Windows.Media.Brushes.Transparent, System.Windows.Media.Brushes.Transparent, 0);
		}

		/// <summary>
		/// Archive the closing POC as a naked POC and start a fresh session profile.
		/// Owned by the tick series so the profile resets in step with the ticks that
		/// feed it, independent of when the primary series rolls.
		/// </summary>
		private void RollProfileSession()
		{
			if (curPOC > 0 && !nakedPOCs.Contains(curPOC)) nakedPOCs.Add(curPOC);
			volProfile.Clear();
			curPOC = 0; curVAH = 0; curVAL = 0;
			profileDirty = true;
		}

		/// <summary>
		/// POC and value area over a DENSE ladder from the session low to the session high,
		/// so untraded interior prices count as zero-volume rows exactly as OrderFlow+ does.
		/// Expansion is the standard Market Profile paired-row walk: compare the sum of the
		/// two rows above against the two below and take the larger side. BnB compared single
		/// rows and bailed out at the first two-sided gap, which could leave the value area
		/// short of the target percentage.
		/// </summary>
		private void ComputeValueArea()
		{
			if (volProfile.Count < 2) return;

			double minK = volProfile.Keys.First();
			double maxK = volProfile.Keys.Last();
			double ts = TickSize <= 0 ? 1 : TickSize;

			int n = (int)Math.Round((maxK - minK) / ts) + 1;
			if (n < 2 || n > 50000) return;

			long[] vals = new long[n];
			long total = 0;
			foreach (var kv in volProfile)
			{
				int i = (int)Math.Round((kv.Key - minK) / ts);
				if (i >= 0 && i < n) { vals[i] += kv.Value; total += kv.Value; }
			}
			if (total <= 0) return;

			int pocIdx = 0;
			for (int i = 1; i < n; i++) if (vals[i] > vals[pocIdx]) pocIdx = i;

			curPOC = Instrument.MasterInstrument.RoundToTickSize(minK + pocIdx * ts);

			long target = (long)(total * (VAPercentage / 100.0));
			int lo = pocIdx, hi = pocIdx;
			long acc = vals[pocIdx];

			while (acc < target && (lo > 0 || hi < n - 1))
			{
				long up = 0, dn = 0;
				if (hi < n - 1) { up = vals[hi + 1]; if (hi + 2 < n) up += vals[hi + 2]; }
				if (lo > 0)     { dn = vals[lo - 1]; if (lo - 2 >= 0) dn += vals[lo - 2]; }

				if (hi >= n - 1)   { acc += dn; lo = Math.Max(0, lo - 2); }
				else if (lo <= 0)  { acc += up; hi = Math.Min(n - 1, hi + 2); }
				else if (up >= dn) { acc += up; hi = Math.Min(n - 1, hi + 2); }
				else               { acc += dn; lo = Math.Max(0, lo - 2); }
			}

			curVAH = Instrument.MasterInstrument.RoundToTickSize(minK + hi * ts);
			curVAL = Instrument.MasterInstrument.RoundToTickSize(minK + lo * ts);
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (RenderTarget == null || ChartBars == null) return;

			float xRight = (float)chartControl.CanvasRight;
			float xLeft = 0f;
			int firstB = ChartBars.FromIndex;
			int lastB = ChartBars.ToIndex;
			if (firstB < 0 || lastB < 0) return;

			Color4 vaColor4 = new Color4(1f, 0.27f, 0f, (float)VPOpacity / 100f);
			if (VPValueAreaColor != null && VPValueAreaColor is System.Windows.Media.SolidColorBrush scbVA)
				vaColor4 = new Color4(scbVA.Color.R / 255f, scbVA.Color.G / 255f, scbVA.Color.B / 255f, (float)VPOpacity / 100f);

			Color4 outColor4 = new Color4(0.5f, 0.5f, 0.5f, (float)VPOpacity / 100f);
			if (VPOutsideColor != null && VPOutsideColor is System.Windows.Media.SolidColorBrush scbOut)
				outColor4 = new Color4(scbOut.Color.R / 255f, scbOut.Color.G / 255f, scbOut.Color.B / 255f, (float)VPOpacity / 100f);

			// SECURE MEMORY CLONING FOR GRAPHICS THREAD (prevents disappearing charts)
			KeyValuePair<double, long>[] profileClone = null;
			double[] pocLinesClone = new double[3];
			double[] pdLinesClone = new double[2];
			double[] nakedPocClone = null;

			lock (dataLock)
			{
				if (volProfile.Count > 0) profileClone = volProfile.ToArray();
				if (nakedPOCs.Count > 0) nakedPocClone = nakedPOCs.ToArray();
				pocLinesClone[0] = curPOC; pocLinesClone[1] = curVAH; pocLinesClone[2] = curVAL;
				pdLinesClone[0] = pdh; pdLinesClone[1] = pdl;
			}

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			Action<int, int, float, float> drawNativePlot = (pIdx, idx, xPrev, xCurr) => {
				if (Plots[pIdx].BrushDX == null) return;
				float valPrev = (float)Values[pIdx].GetValueAt(idx - 1);
				float valCurr = (float)Values[pIdx].GetValueAt(idx);
				if (valPrev == 0 || valCurr == 0 || float.IsNaN(valPrev) || float.IsNaN(valCurr)) return;

				float vPrev = chartScale.GetYByValue(valPrev);
				float vCurr = chartScale.GetYByValue(valCurr);

				if (Plots[pIdx].DashStyleHelper == DashStyleHelper.Solid) {
					RenderTarget.DrawLine(new Vector2(xPrev, vPrev), new Vector2(xCurr, vCurr), Plots[pIdx].BrushDX, Plots[pIdx].Width);
				} else {
					var props = new SharpDX.Direct2D1.StrokeStyleProperties();
					if (Plots[pIdx].DashStyleHelper == DashStyleHelper.Dash) props.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
					else if (Plots[pIdx].DashStyleHelper == DashStyleHelper.Dot) props.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
					else props.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
					using (var style = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, props)) {
						RenderTarget.DrawLine(new Vector2(xPrev, vPrev), new Vector2(xCurr, vCurr), Plots[pIdx].BrushDX, Plots[pIdx].Width, style);
					}
				}
			};

			using (var npocBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(1f, 0.8f, 0.2f, 0.7f)))
			{
				for (int i = firstB + 1; i <= lastB; i++)
				{
					float x1 = chartControl.GetXByBarIndex(ChartBars, i - 1);
					float x2 = chartControl.GetXByBarIndex(ChartBars, i);

					if (ShowVWAP)
					{
						drawNativePlot(0, i, x1, x2);
						drawNativePlot(6, i, x1, x2);  drawNativePlot(7, i, x1, x2);
						drawNativePlot(8, i, x1, x2);  drawNativePlot(9, i, x1, x2);
						drawNativePlot(10, i, x1, x2); drawNativePlot(11, i, x1, x2);
					}
				}

				Action<double, SharpDX.Direct2D1.Brush, float> dL = (v, b, w) => {
					if (v <= 0 || b == null) return; float y = chartScale.GetYByValue(v);
					RenderTarget.DrawLine(new Vector2(xLeft, y), new Vector2(xRight, y), b, w);
				};

				if (ShowVP) { dL(pocLinesClone[0], Plots[1].BrushDX, Plots[1].Width); dL(pocLinesClone[1], Plots[2].BrushDX, Plots[2].Width); dL(pocLinesClone[2], Plots[3].BrushDX, Plots[3].Width); }
				if (ShowPriorDay && pdLinesClone[0] > 0) dL(pdLinesClone[0], Plots[4].BrushDX, Plots[4].Width);
				if (ShowPriorDay && pdLinesClone[1] > 0) dL(pdLinesClone[1], Plots[5].BrushDX, Plots[5].Width);

				if (ShowNakedPOCs && nakedPocClone != null)
				{
					var strokeStyle = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash });
					foreach (double npoc in nakedPocClone)
					{
						float ny = chartScale.GetYByValue(npoc);
						RenderTarget.DrawLine(new Vector2(xLeft, ny), new Vector2(xRight, ny), npocBrush, 1.5f, strokeStyle);
					}
					strokeStyle.Dispose();
				}

				// Continuous polygon paths and glowing contours - the BnB profile look, unchanged.
				if (ShowVP && profileClone != null && profileClone.Length > 0)
				{
					double maxV = profileClone.Max(k => k.Value);
					if (maxV <= 0) return;

					var stopsVA = new[] { new SharpDX.Direct2D1.GradientStop { Position = 0.0f, Color = new Color4(vaColor4.Red, vaColor4.Green, vaColor4.Blue, 0.05f) }, new SharpDX.Direct2D1.GradientStop { Position = 1.0f, Color = vaColor4 } };
					var stopsOut = new[] { new SharpDX.Direct2D1.GradientStop { Position = 0.0f, Color = new Color4(outColor4.Red, outColor4.Green, outColor4.Blue, 0.05f) }, new SharpDX.Direct2D1.GradientStop { Position = 1.0f, Color = outColor4 } };
					Vector2 startPt = new Vector2(xRight - VPWidth, 0); Vector2 endPt = new Vector2(xRight, 0);

					using (var gradColVA = new SharpDX.Direct2D1.GradientStopCollection(RenderTarget, stopsVA))
					using (var gradColOut = new SharpDX.Direct2D1.GradientStopCollection(RenderTarget, stopsOut))
					using (var gradBrushVA = new SharpDX.Direct2D1.LinearGradientBrush(RenderTarget, new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = startPt, EndPoint = endPt }, gradColVA))
					using (var gradBrushOut = new SharpDX.Direct2D1.LinearGradientBrush(RenderTarget, new SharpDX.Direct2D1.LinearGradientBrushProperties { StartPoint = startPt, EndPoint = endPt }, gradColOut))
					using (var outlineOut = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(outColor4.Red, outColor4.Green, outColor4.Blue, 0.8f)))
					using (var outlineVA = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new Color4(vaColor4.Red, vaColor4.Green, vaColor4.Blue, 1.0f)))
					using (var pathOut = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
					using (var pathVA = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
					{
						using (var sinkOut = pathOut.Open())
						{
							sinkOut.BeginFigure(new Vector2(xRight, chartScale.GetYByValue(profileClone[0].Key)), SharpDX.Direct2D1.FigureBegin.Filled);
							foreach (var entry in profileClone)
							{
								float y = chartScale.GetYByValue(entry.Key);
								float w = (float)((double)entry.Value / maxV * VPWidth);
								sinkOut.AddLine(new Vector2(xRight - w, y));
							}
							sinkOut.AddLine(new Vector2(xRight, chartScale.GetYByValue(profileClone[profileClone.Length - 1].Key)));
							sinkOut.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
							sinkOut.Close();
						}

						var vaPoints = profileClone.Where(k => k.Key >= pocLinesClone[2] && k.Key <= pocLinesClone[1]).ToArray();
						if (vaPoints.Length > 0)
						{
							using (var sinkVA = pathVA.Open())
							{
								sinkVA.BeginFigure(new Vector2(xRight, chartScale.GetYByValue(vaPoints[0].Key)), SharpDX.Direct2D1.FigureBegin.Filled);
								foreach (var entry in vaPoints)
								{
									float y = chartScale.GetYByValue(entry.Key);
									float w = (float)((double)entry.Value / maxV * VPWidth);
									sinkVA.AddLine(new Vector2(xRight - w, y));
								}
								sinkVA.AddLine(new Vector2(xRight, chartScale.GetYByValue(vaPoints[vaPoints.Length - 1].Key)));
								sinkVA.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
								sinkVA.Close();
							}
						}

						RenderTarget.FillGeometry(pathOut, gradBrushOut);
						RenderTarget.DrawGeometry(pathOut, outlineOut, 1.0f);
						if (vaPoints.Length > 0) { RenderTarget.FillGeometry(pathVA, gradBrushVA); RenderTarget.DrawGeometry(pathVA, outlineVA, 1.5f); }
					}
				}
			}
		}

		#region Exposed Series
		[Browsable(false)][XmlIgnore] public Series<double> VWAP_Curve { get { return Values[0]; } }
		[Browsable(false)][XmlIgnore] public Series<double> POC_Data   { get { return Values[1]; } }
		[Browsable(false)][XmlIgnore] public Series<double> VAH_Data   { get { return Values[2]; } }
		[Browsable(false)][XmlIgnore] public Series<double> VAL_Data   { get { return Values[3]; } }
		[Browsable(false)][XmlIgnore] public Series<double> PDH_Data   { get { return Values[4]; } }
		[Browsable(false)][XmlIgnore] public Series<double> PDL_Data   { get { return Values[5]; } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BnBTraderVPVWAPV0001[] cacheBnBTraderVPVWAPV0001;
		public BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			return BnBTraderVPVWAPV0001(Input, showVP, showVWAP, vPWidth, vPOpacity, vAPercentage, sD1_Mult, sD2_Mult, sD3_Mult, showNakedPOCs, showPriorDay);
		}

		public BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(ISeries<double> input, bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			if (cacheBnBTraderVPVWAPV0001 != null)
				for (int idx = 0; idx < cacheBnBTraderVPVWAPV0001.Length; idx++)
					if (cacheBnBTraderVPVWAPV0001[idx] != null && cacheBnBTraderVPVWAPV0001[idx].ShowVP == showVP && cacheBnBTraderVPVWAPV0001[idx].ShowVWAP == showVWAP && cacheBnBTraderVPVWAPV0001[idx].VPWidth == vPWidth && cacheBnBTraderVPVWAPV0001[idx].VPOpacity == vPOpacity && cacheBnBTraderVPVWAPV0001[idx].VAPercentage == vAPercentage && cacheBnBTraderVPVWAPV0001[idx].SD1_Mult == sD1_Mult && cacheBnBTraderVPVWAPV0001[idx].SD2_Mult == sD2_Mult && cacheBnBTraderVPVWAPV0001[idx].SD3_Mult == sD3_Mult && cacheBnBTraderVPVWAPV0001[idx].ShowNakedPOCs == showNakedPOCs && cacheBnBTraderVPVWAPV0001[idx].ShowPriorDay == showPriorDay && cacheBnBTraderVPVWAPV0001[idx].EqualsInput(input))
						return cacheBnBTraderVPVWAPV0001[idx];
			return CacheIndicator<BnBTraderVPVWAPV0001>(new BnBTraderVPVWAPV0001(){ ShowVP = showVP, ShowVWAP = showVWAP, VPWidth = vPWidth, VPOpacity = vPOpacity, VAPercentage = vAPercentage, SD1_Mult = sD1_Mult, SD2_Mult = sD2_Mult, SD3_Mult = sD3_Mult, ShowNakedPOCs = showNakedPOCs, ShowPriorDay = showPriorDay }, input, ref cacheBnBTraderVPVWAPV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			return indicator.BnBTraderVPVWAPV0001(Input, showVP, showVWAP, vPWidth, vPOpacity, vAPercentage, sD1_Mult, sD2_Mult, sD3_Mult, showNakedPOCs, showPriorDay);
		}

		public Indicators.BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(ISeries<double> input , bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			return indicator.BnBTraderVPVWAPV0001(input, showVP, showVWAP, vPWidth, vPOpacity, vAPercentage, sD1_Mult, sD2_Mult, sD3_Mult, showNakedPOCs, showPriorDay);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			return indicator.BnBTraderVPVWAPV0001(Input, showVP, showVWAP, vPWidth, vPOpacity, vAPercentage, sD1_Mult, sD2_Mult, sD3_Mult, showNakedPOCs, showPriorDay);
		}

		public Indicators.BnBTraderVPVWAPV0001 BnBTraderVPVWAPV0001(ISeries<double> input , bool showVP, bool showVWAP, int vPWidth, int vPOpacity, double vAPercentage, double sD1_Mult, double sD2_Mult, double sD3_Mult, bool showNakedPOCs, bool showPriorDay)
		{
			return indicator.BnBTraderVPVWAPV0001(input, showVP, showVWAP, vPWidth, vPOpacity, vAPercentage, sD1_Mult, sD2_Mult, sD3_Mult, showNakedPOCs, showPriorDay);
		}
	}
}

#endregion
