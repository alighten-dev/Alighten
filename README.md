# Alighten NinjaTrader 8 Indicators

This repository contains a proprietary suite of custom NinjaTrader 8 indicators developed for advanced order flow analysis, multi-timeframe pattern recognition, and chart visualization. The suite is centered around the **Alighten Mirror Dashboard** ecosystem and high-performance order flow engines.

## Production Versions

The highest version number is **not** always the production version — abandoned experiments sometimes carry higher numbers (e.g., `RelativeDeltaMultiTFV0003` was an experiment; `V0002` remained production). This table is the source of truth; update it whenever a version is promoted or retired.

| Indicator family | Production version |
|---|---|
| Mirror Dashboard | `AlightenMirrorV0033` |
| Mirror Entry (research) | `AlightenMirrorEntryV0003` |
| Pattern A source | `AlightenMirrorPtAV0010` |
| Pattern B source | `AlightenMirrorPtBV0005` |
| Pattern G source | `AlightenMirrorPtGV0002` |
| Pattern H source | `AlightenMirrorPtHV0002` |
| Pattern F source | `AlightenMirrorPtFV0003` |
| Pattern J source | `AlightenMirrorPtJV0004` |
| Footprint OrderFlow | `AlightenFootprintOrderFlowV00021` |
| HTF Volume Profile | `AlightenHTFVPV0004` |
| Relative Delta | `AlightenRelativeDeltaV0001` |
| Relative Delta MultiTF | `AlightenRelativeDeltaMultiTFV0002` (V0003 = abandoned experiment) |
| Bias | `AlightenBiasV0003` |
| Bar Timer | `AlightenBarTimerV0004` |
| Button Panel | `AlightenButtonPanelV0005` |

## Repository Structure

The core scripts are located in the `NinjaTrader/Indicators/` directory.

### 📊 Alighten Mirror Dashboard Ecosystem
The Mirror ecosystem is a multi-timeframe pattern recognition and dashboard engine. It separates raw pattern detection (the hosted PtA/B/G/H/F/J sources) from the visualization, research-logging, and strategy-emission layer (Mirror).

* **`AlightenMirrorV0033.cs`** — **current Mirror of record**
  * **Description:** The core Multi-Timeframe Dashboard and visualization engine. Consolidates level signals from Patterns A, B, G, H, F, and J across seven timeframes (Daily, 240m, 60m, 30m, 15m, 10m, 5m) onto a single primary chart, each pattern hosted per-timeframe as a calc-only child indicator. Features: per-pattern/per-TF visibility (settings modal with live toggles), per-TF colors and per-pattern dash styles, transparent Databox plots for strategies/Bloodhound (`PtAD`…`PtJ5m`), **Daily Bias Levels** (the last N Daily levels from the AlightenBiasV0003 pivot engine, ported inline, colored by the last daily close's side), a **Research Log** (every confirmed signal with confluence context and forward MFE/MAE outcomes, exported to CSV via the "Export Levels"/"Export Research" toolbar buttons for offline mining), and level export to CSV.
* **`AlightenMirrorEntryV0003.cs`** — research companion
  * **Description:** Self-contained entry-bar confirmation engine at Mirror levels (10 orderflow confirmations: sweep-reclaim, POC-in-wick, absorption, stopping volume, etc.), an LTF support-lost/resistance-gained trigger, and a research log of EVERY level touch (confirmations + forward MFE/MAE), exportable for regime/entry-type mining.
* **`AlightenMirrorPtAV0010.cs`**
  * **Description:** Pattern A signal engine — ZigZag/level tracking with sequential wick-touch signals (consecutive touch bars all signal until the sequence breaks).
* **`AlightenMirrorPtBV0005.cs`**
  * **Description:** Pattern B signal engine.
* **`AlightenMirrorPtGV0002.cs`**
  * **Description:** Pattern G signal engine — level gained/lost arming with wick-retest signals.
* **`AlightenMirrorPtHV0002.cs`**
  * **Description:** Pattern H signal engine — the "flipped Pattern G": a completed G pattern whose level is then lost/gained arms the opposite-direction retest signal.
* **`AlightenMirrorPtFV0003.cs`** (Breakout + Immediate Retest)
  * **Description:** Pattern F signal engine tracking precise breakouts and immediate retests of critical structure levels.
* **`AlightenMirrorPtJV0004.cs`** — Pattern J (paired pivots)
  * **Description:** Paired-pivot pattern engine (Trends/PairBounds lineage): qualified zigzag pairs (minimum trend bars/ticks), levels at the pivot candle's body, endpoint gain/loss state machines with first-touch tests, triangle test markers with sequential consecutive-wick signals, and an optional flip-invalidation of past signals ("Remove Signals When Level Flips"). Serves both roles: standalone chart use (default settings draw triangles, tested-level stubs, and SG/RL structural pairs) and Mirror hosting via `PatternJLongLevel`/`PatternJShortLevel`/`PatternJSignal` series reporting the level being tested on each bar (with realtime provisional evaluation of the forming bar) and a Calc-Only mode.

### 🔬 Order Flow & Volume Analytics
High-performance indicators for analyzing Bid/Ask delta, volume nodes, and institutional footprints.

* **`AlightenFootprintOrderFlowV00021.cs`**
  * **Description:** A comprehensive Footprint Indicator that aggregates Bid, Ask, Delta, Volume, Point of Control (POC), and Value Area metrics natively within NinjaTrader. Emits clean data arrays for integration with Bloodhound/strategies.
* **`AlightenHTFVPV0004.cs`**
  * **Description:** Higher Timeframe Volume Profile (HTFVP). Calculates the volume profile of an HTF bar and natively projects its POC and Value Area onto lower timeframe charts for the duration of the subsequent HTF bar. Used heavily for trend bias filtering.
* **`AlightenRelativeDeltaMultiTFV0002.cs`**
  * **Description:** Multi-Timeframe Relative Delta Wick Heatmap. Provides audio alerts when specific delta conditions align across multiple tracked timeframes.
* **`AlightenRelativeDeltaV0001.cs`**
  * **Description:** Relative Delta Footprint visualization — the current production version of the Relative Delta indicator.
* **`VolumeDelta.cs`**
  * **Description:** Core foundational Volume Delta calculation engine.

### 📈 Market Structure & Trend Analysis
* **`AlightenBiasV0003.cs`**
  * **Description:** Evaluates multi-level FTG (Failed To Go) and FTL (Failed To Lower) structures to dynamically determine the current market trend/bias. Its pivot/level engine also powers the Mirror dashboard's Daily Bias Levels (ported inline in AlightenMirrorV0033).
* **`HigherTimeframeCandles.cs`**
  * **Description:** Projects higher-timeframe candlestick boundaries (Open, High, Low, Close) dynamically onto lower-timeframe charts.

### 🛠 UI / UX Utilities
Tools to enhance the NinjaTrader charting experience and streamline live execution.

* **`AlightenBarTimerV0004.cs`**
  * **Description:** An optimized Bar Timer that utilizes the Direct2D `OnRender` pipeline to completely eliminate the flashing/flickering common in standard UI-based bar timers.
* **`AlightenButtonPanelV0005.cs`**
  * **Description:** An interactive on-chart button panel providing quick access to Order Flow parameter controls and execution logic (e.g., "Breakeven + X Ticks").
* **`IndicatorVisualStyleHelper.cs`**
  * **Description:** Centralized helper class for managing uniform visual styling (brushes, strokes, fonts) across the Alighten indicator suite.
* **`LabelRemover.cs`**
  * **Description:** A clean-up utility that automatically removes cluttering text labels from all indicators loaded on a chart.
* **`OrderLineDecorator.cs`**
  * **Description:** Enhances the visual presentation of active order lines on the chart.

---
*Note: Legacy versions and WIP indicators designated with an `@` prefix have been excluded from this index.*
